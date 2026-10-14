using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using Azure.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class MemberDetailFailureTelemetryTests
{
    private const string Secret = "SENSITIVE-SENTINEL-name-email-query-sql";
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000062;IngestionEndpoint=https://diagnostic.invalid/";

    [Theory]
    [InlineData("pipeline", false)]
    [InlineData("member-lookup", false)]
    [InlineData("forum-summary", false)]
    [InlineData("render", false)]
    [InlineData("forum-summary", true)]
    public async Task Sanitized_failure_crosses_real_Azure_exporter_despite_unsampled_trace(string stage, bool sampled)
    {
        using var wire = new CaptureTransport();
        using var client = new HttpClient(wire);
        using var activity = new Activity("synthetic-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = sampled ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
        var factory = CreateFactory(client);
        var reporter = new MemberDetailFailureReporter(factory);
        var context = Context();
        var exception = SqlExceptionFactory.Create(-2, Secret);
        var middleware = new MemberDetailFailureMiddleware(ctx =>
        {
            ctx.Items[MemberDetailFailureMiddleware.StageKey] = stage;
            throw exception;
        });
        using (factory.CreateLogger("ambient").BeginScope(new Dictionary<string, object?> { ["Email"] = Secret, ["MemberId"] = Secret }))
        {
            var thrown = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => middleware.InvokeAsync(context, reporter));
            Assert.Same(exception, thrown);
        }
        reporter.Dispose(); // Flush the actual Azure Monitor batch exporter to the fake HTTP transport.
        var payload = Assert.Single(wire.Payloads);
        AssertPropertyAllowlist(payload);
        Assert.Contains("MemberDetailFailure", payload);
        Assert.Contains(stage, payload);
        Assert.Contains("request-reference-62", payload);
        Assert.Contains(activity.TraceId.ToString(), payload);
        Assert.Contains("SqlException", payload);
        Assert.Contains("-2", payload);
        Assert.DoesNotContain(Secret, payload);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000063", payload);
        Assert.DoesNotContain("ExceptionData", payload);
        Assert.DoesNotContain("Email", payload);
        Assert.DoesNotContain("MemberId", payload);
        var directory = Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory, "TestResults", "member-diagnostic-export");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{stage}-sampled-{sampled}.json"), payload);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Existing_trace_based_log_sampler_drops_unsampled_error(bool sampled, int expectedBatches)
    {
        using var wire = new CaptureTransport();
        using var client = new HttpClient(wire);
        using var activity = new Activity("synthetic-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = sampled ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
        using (var factory = CreateFactory(client, traceBasedSampling: true))
        {
            factory.CreateLogger("baseline").LogError("Synthetic baseline error");
        }
        Assert.Equal(expectedBatches, wire.Payloads.Count);
    }

    [Theory]
    [InlineData("GET", "/admin/members")]
    [InlineData("POST", "/admin/members/00000000-0000-0000-0000-000000000063")]
    [InlineData("GET", "/admin/members/00000000-0000-0000-0000-000000000063/hide")]
    public async Task Unrelated_routes_do_not_export(string method, string path)
    {
        using var wire = new CaptureTransport();
        using var client = new HttpClient(wire);
        var reporter = new MemberDetailFailureReporter(CreateFactory(client));
        var context = Context();
        context.Request.Method = method;
        context.Request.Path = path;
        var middleware = new MemberDetailFailureMiddleware(_ => throw new InvalidOperationException(Secret));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context, reporter));
        reporter.Dispose();
        Assert.Empty(wire.Payloads);
    }

    [Fact]
    public async Task Actual_Review_route_exports_summary_stage_and_displayed_reference()
    {
        using var wire = new CaptureTransport();
        using var client = new HttpClient(wire);
        var reporter = new MemberDetailFailureReporter(CreateFactory(client));
        var memberId = Guid.Parse("00000000-0000-0000-0000-000000000063");
        await using var factory = new QueenZoneWebApplicationFactory();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<MemberDetailFailureReporter>();
            services.AddSingleton(reporter);
            services.RemoveAll<IForumWriteRepository>();
            services.AddSingleton<IForumWriteRepository>(new TimeoutHideForumWriteRepository(new InMemoryForumWriteRepository(), SqlExceptionFactory.Create(-2, Secret), summaryTimeout: true));
        }));
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>().CreateAsync(new MemberAccount
            {
                Id = memberId,
                Email = Secret + "@example.invalid",
                DisplayName = Secret,
                CreatedAt = DateTime.UtcNow
            });
        }
        using var web = AdminHttpTestHelpers.CreateClient(host, AdminHttpTestHelpers.AdminEmail);
        var list = await web.GetStringAsync("/admin/members?query=" + Secret);
        Assert.Contains($"href=\"/admin/members/{memberId}\">Review</a>", list);
        using var response = await web.GetAsync($"/admin/members/{memberId}?query={Secret}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(Secret, body);
        var reference = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(body, @"Error reference:\s*([^<]+)").Groups[1].Value).Trim();
        Assert.NotEmpty(reference);
        reporter.Dispose();
        var payload = Assert.Single(wire.Payloads);
        AssertPropertyAllowlist(payload);
        Assert.Contains("forum-summary", payload);
        Assert.Contains(reference, payload);
        Assert.DoesNotContain(Secret, payload);
        Assert.DoesNotContain(memberId.ToString(), payload);
        var directory = Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory, "TestResults", "member-diagnostic-export");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "actual-review-route.json"), payload);
    }

    [Fact]
    public async Task Dedicated_logger_coexists_with_sampled_logger_at_same_destination()
    {
        using var wire = new CaptureTransport();
        using var client = new HttpClient(wire);
        using var activity = new Activity("synthetic-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = ActivityTraceFlags.None;
        var connection = ConnectionString.Replace("00000000-0000-0000-0000-000000000062", Guid.NewGuid().ToString());
        using var baseline = CreateFactory(client, traceBasedSampling: true, connection: connection);
        var reporter = new MemberDetailFailureReporter(CreateFactory(client, connection: connection));
        baseline.CreateLogger("baseline").LogError("Synthetic baseline must be sampled out");
        var middleware = new MemberDetailFailureMiddleware(_ => throw SqlExceptionFactory.Create(-2, Secret));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => middleware.InvokeAsync(Context(), reporter));
        reporter.Dispose();
        baseline.Dispose();
        var payload = Assert.Single(wire.Payloads);
        AssertPropertyAllowlist(payload);
        Assert.DoesNotContain("Synthetic baseline", payload);
        Assert.DoesNotContain(Secret, payload);
    }
    internal static ILoggerFactory CreateFactory(HttpClient client, bool traceBasedSampling = false, string? connection = null) =>
        MemberDetailFailureReporter.CreateLoggerFactory(connection ?? ConnectionString.Replace("00000000-0000-0000-0000-000000000062", Guid.NewGuid().ToString()), options =>
        {
            options.Transport = new HttpClientTransport(client);
            options.DisableOfflineStorage = true;
            if (traceBasedSampling)
            {
                options.EnableTraceBasedLogsSampler = true;
            }
        });

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "request-reference-62" };
        context.Request.Method = "GET";
        context.Request.Path = "/admin/members/00000000-0000-0000-0000-000000000063";
        context.Request.QueryString = new QueryString("?query=" + Secret);
        return context;
    }

    private static void AssertPropertyAllowlist(string payload)
    {
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        var properties = json.RootElement.GetProperty("data").GetProperty("baseData").GetProperty("properties");
        string[] expected = ["Route", "Stage", "RequestId", "TraceId", "Status", "ExceptionType", "SqlNumber", "CategoryName", "EventId", "EventName"];
        Assert.Equal(expected.Order(), properties.EnumerateObject().Select(property => property.Name).Order());
    }

    internal sealed class CaptureTransport : HttpMessageHandler
    {
        internal ConcurrentQueue<string> Payloads { get; } = new();
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => Capture(request);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Capture(request));
        private HttpResponseMessage Capture(HttpRequestMessage request)
        {
            if (request.Content is not null)
            {
                var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                using var input = new MemoryStream(bytes);
                using var decoded = request.Content.Headers.ContentEncoding.Contains("gzip") ? (Stream)new GZipStream(input, CompressionMode.Decompress) : input;
                using var reader = new StreamReader(decoded, Encoding.UTF8);
                Payloads.Enqueue(reader.ReadToEnd());
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"itemsReceived\":1,\"itemsAccepted\":1,\"errors\":[]}") };
        }
    }
}
