using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace QueenZone.Web;

public static class QueenZoneTelemetryServiceCollectionExtensions
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static IServiceCollection AddQueenZoneApplicationInsights(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILoggingBuilder logging)
    {
        if (QueenZoneEnvironments.IsAutomatedTestHost(environment))
        {
            return services;
        }

        var connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        var section = configuration.GetSection("ApplicationInsights");
        var tracesPerSecond = section.GetValue<double?>("TracesPerSecond") ?? 0.2;
        var enableLiveMetrics = section.GetValue<bool?>("EnableLiveMetrics") ?? false;
        var enableTraceBasedLogsSampler = section.GetValue<bool?>("EnableTraceBasedLogsSampler") ?? true;
        var exportedLogLevel = section.GetValue<LogLevel?>("ExportedLogLevel") ?? LogLevel.Warning;

        logging.AddFilter<OpenTelemetryLoggerProvider>(null, exportedLogLevel);

        // Azure Monitor 1.6 defaults these to true when unset. Set them before
        // its options initialize, and redact again before exporters receive spans.
        configuration["OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"] = "false";
        configuration["OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION"] = "false";

        services.AddOpenTelemetry()
            // Processor order matters: this must precede UseAzureMonitor's exporters.
            .WithTracing(tracing => HttpQueryValueRedactionProcessor.AddTo(tracing))
            .ConfigureResource(resource => resource.AddService(
                serviceName: "QueenZone.Web",
                serviceNamespace: "QueenZone"))
            .UseAzureMonitor(options =>
            {
                options.ConnectionString = connectionString;
                options.TracesPerSecond = tracesPerSecond;
                options.EnableLiveMetrics = enableLiveMetrics;
                options.EnableTraceBasedLogsSampler = enableTraceBasedLogsSampler;
            })
            .WithTracing(tracing => tracing.AddSource(QueenZoneTelemetry.ActivitySourceName));

        return services;
    }
}
