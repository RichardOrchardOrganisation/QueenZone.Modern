using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Web.Health;

namespace QueenZone.Web.Tests;

public sealed class WarmupDiagnosticsTests
{
    [Fact]
    public async Task RunWarmupAsync_Failure_LogsOnlyExceptionTypeAndReturnsMinimal503()
    {
        const string sensitive = "Password=secret;AccountKey=storage-key;https://example.test/?token=private;person@example.test";
        var failure = new InvalidOperationException(sensitive, new Exception(sensitive));
        failure.Data["token"] = sensitive;
        var logger = new CollectingLogger<PublicWarmupService>();
        await using var provider = CreateProvider();
        var warmup = CreateWarmup(provider, failure);

        var result = await QueenZoneHealthEndpoints.RunWarmupAsync(
            new StubHealthCheckService(HealthStatus.Healthy), warmup,
            new CollectingLoggerFactory(logger), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(nameof(InvalidOperationException), entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(sensitive, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("storage-key", entry.Message, StringComparison.Ordinal);
        await AssertMinimalUnhealthyAsync(result, provider);
    }

    [Fact]
    public async Task RunWarmupAsync_Cancellation_PropagatesSameExceptionWithoutLoggingItsContents()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = new OperationCanceledException("https://example.test/?token=private", cancellation.Token);
        var logger = new CollectingLogger<PublicWarmupService>();
        await using var provider = CreateProvider();

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            QueenZoneHealthEndpoints.RunWarmupAsync(
                new StubHealthCheckService(HealthStatus.Healthy), CreateWarmup(provider, failure),
                new CollectingLoggerFactory(logger), cancellation.Token));

        Assert.Same(failure, actual);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("Warmup canceled", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("example.test", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWarmupAsync_UnhealthyReadiness_DoesNotPrimeCachesAndReturnsMinimal503()
    {
        var logger = new CollectingLogger<PublicWarmupService>();
        await using var provider = CreateProvider();

        var result = await QueenZoneHealthEndpoints.RunWarmupAsync(
            new StubHealthCheckService(HealthStatus.Unhealthy),
            CreateWarmup(provider, new InvalidOperationException("Cache priming must not run")),
            new CollectingLoggerFactory(logger), CancellationToken.None);

        Assert.Contains("failed readiness checks", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
        await AssertMinimalUnhealthyAsync(result, provider);
    }

    private static ServiceProvider CreateProvider() => new ServiceCollection().AddLogging().BuildServiceProvider();

    private static PublicWarmupService CreateWarmup(ServiceProvider provider, Exception failure) => new(
        provider.GetRequiredService<IServiceScopeFactory>(), new ThrowingTimeProvider(failure),
        NullLogger<PublicWarmupService>.Instance);

    private static async Task AssertMinimalUnhealthyAsync(IResult result, IServiceProvider provider)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        await using var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        body.Position = 0;
        Assert.Equal("{\"status\":\"unhealthy\"}", await new StreamReader(body).ReadToEndAsync());
    }

    private sealed class ThrowingTimeProvider(Exception failure) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw failure;
    }

    private sealed class StubHealthCheckService(HealthStatus status) : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry>(), status, TimeSpan.Zero));
    }
}
