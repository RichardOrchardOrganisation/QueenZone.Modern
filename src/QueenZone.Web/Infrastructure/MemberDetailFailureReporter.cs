using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace QueenZone.Web;

// A dedicated provider prevents request scopes or exception messages from entering this event.
// Only these rare, sanitized failures bypass trace-based log sampling.
public sealed class MemberDetailFailureReporter : IDisposable
{
    private readonly ILoggerFactory? factory;
    private readonly ILogger logger;

    public MemberDetailFailureReporter(IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!QueenZoneEnvironments.IsAutomatedTestHost(environment) && !string.IsNullOrWhiteSpace(connectionString))
        {
            factory = CreateLoggerFactory(connectionString);
            logger = factory.CreateLogger("QueenZone.MemberDetailFailure");
        }
        else
        {
            logger = NullLogger.Instance;
        }
    }

    internal MemberDetailFailureReporter(ILoggerFactory factory)
    {
        this.factory = factory;
        logger = factory.CreateLogger("QueenZone.MemberDetailFailure");
    }

    internal static ILoggerFactory CreateLoggerFactory(string connectionString, Action<AzureMonitorExporterOptions>? configure = null) =>
        LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Error).AddOpenTelemetry(options =>
        {
            options.IncludeScopes = false;
            options.IncludeFormattedMessage = false;
            options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("QueenZone.Web", "QueenZone"));
            options.AddAzureMonitorLogExporter(exporter =>
            {
                exporter.ConnectionString = connectionString;
                exporter.EnableTraceBasedLogsSampler = false;
                configure?.Invoke(exporter);
            });
        }));

    internal void Report(HttpContext context, Exception exception, int status = StatusCodes.Status500InternalServerError)
    {
        var stage = context.Items[MemberDetailFailureMiddleware.StageKey] as string;
        stage = stage is "member-lookup" or "forum-summary" or "render" ? stage : "pipeline";
        // Never pass the exception object, URL, member data, or ambient logging scopes.
        logger.LogError(new EventId(50062, "MemberDetailFailure"),
            "MemberDetailFailure Route={Route} Stage={Stage} RequestId={RequestId} TraceId={TraceId} Status={Status} ExceptionType={ExceptionType} SqlNumber={SqlNumber}",
            "/admin/members/{id}", stage, context.TraceIdentifier, Activity.Current?.TraceId.ToString(),
            status, exception.GetType().Name, (exception as SqlException)?.Number);
    }

    public void Dispose() => factory?.Dispose();
}

public sealed class MemberDetailFailureMiddleware(RequestDelegate next)
{
    internal static readonly object StageKey = new();

    public async Task InvokeAsync(HttpContext context, MemberDetailFailureReporter reporter)
    {
        var path = context.Request.Path.Value;
        const string prefix = "/admin/members/";
        var matches = HttpMethods.IsGet(context.Request.Method) && path is not null
            && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(path[prefix.Length..].TrimEnd('/'), out _);
        if (!matches)
        {
            await next(context);
            return;
        }

        context.Items[StageKey] = "pipeline";
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
        {
            // A telemetry outage must not replace the original route exception.
            try { reporter.Report(context, exception); }
            catch { /* The existing exception handler still handles the original failure. */ }
            throw;
        }
    }
}
