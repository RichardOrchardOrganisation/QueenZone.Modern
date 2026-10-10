using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace QueenZone.Web.Tests;

public sealed class HttpQueryValueRedactionTests
{
    [Theory]
    [InlineData("url.query", "?q=private%20search&token=a%26b&q=second&empty=", "?q=Redacted&token=Redacted&q=Redacted&empty=Redacted")]
    [InlineData("url.query", "q=private+search&type=articles", "q=Redacted&type=Redacted")]
    [InlineData("url.full", "https://example.invalid/api/v1/search?q=private&key=a=b", "https://example.invalid/api/v1/search?q=Redacted&key=Redacted")]
    [InlineData("http.url", "https://example.invalid/search?q=private#fragment", "https://example.invalid/search?q=Redacted#fragment")]
    [InlineData("http.target", "/search?q=private&flag", "/search?q=Redacted&flag")]
    [InlineData("url.full", "https://example.invalid/path", "https://example.invalid/path")]
    [InlineData("url.query", "", "")]
    [InlineData("url.query", "q=Redacted", "q=Redacted")]
    public void RedactsAllQueryValuesWithoutChangingPathOrNames(string tag, string input, string expected)
    {
        using var activity = new Activity("synthetic HTTP span");
        activity.SetTag(tag, input);
        new HttpQueryValueRedactionProcessor().OnEnd(activity);
        Assert.Equal(expected, activity.GetTagItem(tag));
    }

    [Theory]
    [InlineData(ActivityKind.Server)]
    [InlineData(ActivityKind.Client)]
    public void ExporterReceivesRedactedValuesAndUsefulHttpMetadata(ActivityKind kind)
    {
        var sourceName = "synthetic-query-redaction-" + Guid.NewGuid();
        using var source = new ActivitySource(sourceName);
        using var exporter = new RecordingExporter();
        using var provider = HttpQueryValueRedactionProcessor.AddTo(Sdk.CreateTracerProviderBuilder())
            .AddSource(sourceName)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        using (var activity = source.StartActivity("GET /api/v1/search", kind))
        {
            Assert.NotNull(activity);
            activity.SetTag("url.query", "?q=synthetic-secret&other=synthetic-token");
            activity.SetTag("url.full", "https://example.invalid/api/v1/search?q=synthetic-secret");
            activity.SetTag("http.request.method", "GET");
            activity.SetTag("http.route", "/api/v1/search");
            activity.SetTag("http.response.status_code", 200);
            activity.SetStatus(ActivityStatusCode.Ok);
        }

        Assert.Equal("?q=Redacted&other=Redacted", exporter.Tags["url.query"]);
        Assert.Equal("https://example.invalid/api/v1/search?q=Redacted", exporter.Tags["url.full"]);
        Assert.Equal("GET", exporter.Tags["http.request.method"]);
        Assert.Equal("/api/v1/search", exporter.Tags["http.route"]);
        Assert.Equal(200, exporter.Tags["http.response.status_code"]);
        Assert.Equal(ActivityStatusCode.Ok, exporter.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public void AzureOptionsInitializationCannotDisableTheExplicitRedaction(string? existingOverride)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000001",
            ["OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"] = existingOverride,
            ["OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION"] = existingOverride
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        ILoggingBuilder? logging = null;
        services.AddLogging(builder => logging = builder);
        services.AddQueenZoneApplicationInsights(configuration, new FakeHostEnvironment("Production"), logging!);
        using var serviceProvider = services.BuildServiceProvider();
        // Initialize the actual distro options, whose defaults otherwise turn redaction off.
        _ = serviceProvider.GetRequiredService<IOptions<AzureMonitorOptions>>().Value;

        Assert.Equal("false", configuration["OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"]);
        Assert.Equal("false", configuration["OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION"]);
    }

    private sealed class RecordingExporter : BaseExporter<Activity>
    {
        public Dictionary<string, object?> Tags { get; } = [];
        public ActivityStatusCode Status { get; private set; }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                foreach (var tag in activity.TagObjects)
                {
                    Tags[tag.Key] = tag.Value;
                }
                Status = activity.Status;
            }
            return ExportResult.Success;
        }
    }
}
