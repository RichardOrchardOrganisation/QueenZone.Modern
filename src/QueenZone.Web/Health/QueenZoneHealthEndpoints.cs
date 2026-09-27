using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QueenZone.Web.Health;

public static class QueenZoneHealthEndpoints
{
    public const string ReadyTag = "ready";
    public const string ReadyPath = "/health/ready";

    // Deploy.yml polls this after recycle to prime public caches.
    // WEBSITE_WARMUP_PATH points at /health, not this path (#673 / #674).
    public const string WarmupPath = "/warmup";

    public static bool IsProbePath(PathString path) =>
        string.Equals(path.Value, "/health", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, ReadyPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, WarmupPath, StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddQueenZoneHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SqlReadyHealthCheck>(
                SqlReadyHealthCheck.Name,
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag])
            .AddCheck<BlobReadyHealthCheck>(
                BlobReadyHealthCheck.Name,
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapQueenZoneHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: cheap, no dependency I/O — used by CI smoke and App Service pings.
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // Readiness: SQL + blob when configured. Response is minimal (no secrets, no exception text).
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReadyResponseAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });

        // Warmup: dependency readiness plus representative public query cache priming.
        endpoints.MapGet(WarmupPath, RunWarmupAsync);

        return endpoints;
    }

    internal static async Task<IResult> RunWarmupAsync(
        HealthCheckService healthCheckService,
        PublicWarmupService publicWarmup,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("QueenZone.Web.Health.Warmup");
        var started = Stopwatch.StartNew();

        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains(ReadyTag),
            cancellationToken);
        var readinessMs = started.ElapsedMilliseconds;
        if (report.Status is HealthStatus.Unhealthy)
        {
            logger.LogWarning(
                "Warmup failed readiness checks in {WarmupDurationMs}ms (sql {SqlDurationMs}ms, blob {BlobDurationMs}ms).",
                readinessMs,
                GetDurationMs(report, SqlReadyHealthCheck.Name),
                GetDurationMs(report, BlobReadyHealthCheck.Name));
            return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await publicWarmup.WarmPublicCachesAsync(cancellationToken);
            logger.LogInformation(
                "Warmup completed in {WarmupDurationMs}ms (readiness {WarmupReadinessMs}ms, cache {WarmupCacheMs}ms, sql {SqlDurationMs}ms, blob {BlobDurationMs}ms).",
                started.ElapsedMilliseconds,
                readinessMs,
                started.ElapsedMilliseconds - readinessMs,
                GetDurationMs(report, SqlReadyHealthCheck.Name),
                GetDurationMs(report, BlobReadyHealthCheck.Name));
            return Results.Ok(new { status = "ok" });
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Warmup canceled after {WarmupDurationMs}ms (readiness {WarmupReadinessMs}ms).",
                started.ElapsedMilliseconds,
                readinessMs);
            throw;
        }
        catch
        {
            logger.LogWarning(
                "Warmup failed after {WarmupDurationMs}ms (readiness {WarmupReadinessMs}ms).",
                started.ElapsedMilliseconds,
                readinessMs);
            return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    // Handles probe paths directly, bypassing routing/endpoint dispatch entirely. Mapped
    // endpoints (MapQueenZoneHealthEndpoints below) only execute at the position of the
    // implicit UseEndpoints() — always the very end of the middleware pipeline, regardless
    // of where Map* is called in source — so nothing registered as endpoint routing could
    // ever actually skip earlier middleware (forwarded headers, static files, and so on).
    // Program.cs calls this from a short-circuiting middleware registered as literally the
    // first thing in the pipeline, so probe paths never traverse anything else in this file.
    // The MapQueenZoneHealthEndpoints registrations stay in place as a defense-in-depth
    // fallback: if a path check here ever has a bug, the request still falls through to a
    // correct (if slower, un-bypassed) response instead of a silent 404 (#666).
    public static async Task<bool> TryHandleProbeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (string.Equals(path.Value, "/health", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("""{"status":"ok"}""", context.RequestAborted);
            return true;
        }

        if (string.Equals(path.Value, ReadyPath, StringComparison.OrdinalIgnoreCase))
        {
            var healthCheckService = context.RequestServices.GetRequiredService<HealthCheckService>();
            var report = await healthCheckService.CheckHealthAsync(
                registration => registration.Tags.Contains(ReadyTag),
                context.RequestAborted);
            context.Response.StatusCode = report.Status is HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK;
            await WriteReadyResponseAsync(context, report);
            return true;
        }

        if (string.Equals(path.Value, WarmupPath, StringComparison.OrdinalIgnoreCase))
        {
            var result = await RunWarmupAsync(
                context.RequestServices.GetRequiredService<HealthCheckService>(),
                context.RequestServices.GetRequiredService<PublicWarmupService>(),
                context.RequestServices.GetRequiredService<ILoggerFactory>(),
                context.RequestAborted);
            await result.ExecuteAsync(context);
            return true;
        }

        return false;
    }

    private static long? GetDurationMs(HealthReport report, string name) =>
        report.Entries.TryGetValue(name, out var entry)
            ? (long)entry.Duration.TotalMilliseconds
            : null;

    internal static async Task WriteReadyResponseAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        var payload = new Dictionary<string, object?>
        {
            ["status"] = report.Status.ToString(),
            ["entries"] = report.Entries.ToDictionary(
                static pair => pair.Key,
                static pair => new
                {
                    status = pair.Value.Status.ToString(),
                    description = pair.Value.Description,
                }),
        };

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
            httpContext.RequestAborted);
    }
}
