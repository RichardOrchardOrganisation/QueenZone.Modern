using System.Diagnostics;
using QueenZone.Search.Shared;

namespace QueenZone.Web.Search;

/// <summary>
/// Populates the in-memory <c>SearchDocument</c> index once at startup so the unified search
/// page has something to query in the "Testing" environment and local no-database dev, where
/// there is no separate reindex worker to have already done it. Registered only alongside the
/// in-memory data composition — the real SQL Server-backed environment reindexes via a
/// dedicated batch job, not on every app boot.
/// </summary>
public sealed class SearchIndexSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SearchIndexSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Best-effort: some test suites substitute a repository stub that deliberately throws
        // (to exercise error-handling paths unrelated to search). An unhandled exception here
        // would abort the whole host's startup, failing every test in that WebApplicationFactory
        // instance — so a seeding failure just leaves the index empty instead.
        using var activity = QueenZone.Application.QueenZoneTelemetry.ActivitySource.StartActivity(
            "SearchIndexSeed",
            ActivityKind.Internal);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var builder = scope.ServiceProvider.GetRequiredService<SearchReindexBuilder>();
            await builder.ReindexAllAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Initial search index seeding failed; the in-memory search index will start empty.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
