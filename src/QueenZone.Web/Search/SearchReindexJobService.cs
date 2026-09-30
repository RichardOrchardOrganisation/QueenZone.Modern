using Microsoft.Extensions.Hosting;
using QueenZone.Data;
using QueenZone.Search.Shared;

namespace QueenZone.Web.Search;

public enum SearchReindexJobPhase
{
    Idle = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>
/// Snapshot of the in-process admin reindex job. Safe to expose to the admin UI / status JSON.
/// </summary>
public sealed record SearchReindexJobSnapshot(
    SearchReindexJobPhase Phase,
    string? CurrentContentType,
    string Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Single-flight background reindex for <c>/admin/search</c>. Starts work that outlives the
/// HTTP request so reverse-proxy timeouts cannot abort a multi-minute full rebuild after news.
/// In-process only (production is single-instance B1) — not durable across app restarts.
/// </summary>
public sealed class SearchReindexJobService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime hostApplicationLifetime,
    TimeProvider timeProvider,
    ILogger<SearchReindexJobService> logger)
{
    private readonly object gate = new();
    private int isRunning;
    private Task currentRun = Task.CompletedTask;
    private SearchReindexJobSnapshot snapshot = new(
        SearchReindexJobPhase.Idle,
        CurrentContentType: null,
        Message: "No reindex has been run yet in this process.",
        StartedAt: null,
        FinishedAt: null);

    public SearchReindexJobSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return snapshot;
        }
    }

    /// <summary>
    /// Completes when the most recently started job has finished (successfully or not).
    /// Already completed when no job has run. Lets callers await the job instead of polling.
    /// </summary>
    public Task WaitForCurrentRunAsync()
    {
        lock (gate)
        {
            return currentRun;
        }
    }

    /// <summary>
    /// Starts a full reindex if none is running. Returns <c>false</c> when a job is already active.
    /// </summary>
    public bool TryStart()
    {
        if (Interlocked.CompareExchange(ref isRunning, 1, 0) != 0)
        {
            return false;
        }

        var startedAt = timeProvider.GetUtcNow();
        SetSnapshot(new SearchReindexJobSnapshot(
            SearchReindexJobPhase.Running,
            CurrentContentType: null,
            Message: "Starting reindex…",
            StartedAt: startedAt,
            FinishedAt: null));

        // Do not pass HttpContext.RequestAborted — the browser/proxy disconnect must not cancel work.
        var run = Task.Run(() => RunAsync(startedAt, hostApplicationLifetime.ApplicationStopping));
        lock (gate)
        {
            currentRun = run;
        }

        return true;
    }

    private async Task RunAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var builder = scope.ServiceProvider.GetRequiredService<SearchReindexBuilder>();

            await builder.ReindexAllAsync(
                cancellationToken,
                contentType =>
                {
                    var label = SiteSearchContentType.DisplayLabel(contentType);
                    SetSnapshot(new SearchReindexJobSnapshot(
                        SearchReindexJobPhase.Running,
                        contentType,
                        $"Indexing {label}…",
                        startedAt,
                        FinishedAt: null));
                });

            SetSnapshot(new SearchReindexJobSnapshot(
                SearchReindexJobPhase.Succeeded,
                CurrentContentType: null,
                Message: "Search index rebuilt.",
                StartedAt: startedAt,
                FinishedAt: timeProvider.GetUtcNow()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetSnapshot(new SearchReindexJobSnapshot(
                SearchReindexJobPhase.Failed,
                CurrentContentType: null,
                Message: "Reindex cancelled (application stopping).",
                StartedAt: startedAt,
                FinishedAt: timeProvider.GetUtcNow()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background search reindex failed");
            SetSnapshot(new SearchReindexJobSnapshot(
                SearchReindexJobPhase.Failed,
                CurrentContentType: null,
                Message: "Reindex failed — see application logs for details.",
                StartedAt: startedAt,
                FinishedAt: timeProvider.GetUtcNow()));
        }
        finally
        {
            Interlocked.Exchange(ref isRunning, 0);
        }
    }

    private void SetSnapshot(SearchReindexJobSnapshot next)
    {
        lock (gate)
        {
            snapshot = next;
        }
    }
}
