using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsAgentRunPreparation(
    NewsAiRunExecutor aiRunExecutor,
    INewsAgentRunLeaseService runLeaseService,
    IOptions<OpenRouterOptions> openRouterOptions,
    IOptions<NewsAgentSchedulerOptions> schedulerOptions,
    ILogger<DiscoverNewsWorker> logger)
{
    public bool IsAiEnabled => aiRunExecutor.IsAiEnabled;

    public bool IsDryRun(DiscoverNewsCommandOptions options) => options.DryRun || openRouterOptions.Value.DryRun;

    public async Task<INewsAgentRunLease?> PrepareAsync(
        DiscoverNewsCommandOptions options,
        CancellationToken cancellationToken)
    {
        LogAiStatus();
        var scheduler = schedulerOptions.Value;
        if (!scheduler.UseRunLease || options.Force)
        {
            return NoOpNewsAgentRunLease.Instance;
        }

        var lease = await runLeaseService.TryAcquireAsync(
            scheduler.LeaseName,
            TimeSpan.FromMinutes(scheduler.LeaseDurationMinutes),
            cancellationToken);
        if (lease is null)
        {
            logger.LogWarning(
                "Skipping discover-news run because lease {LeaseName} is held by another instance.",
                scheduler.LeaseName);
        }

        return lease;
    }

    private void LogAiStatus()
    {
        if (!aiRunExecutor.IsAiEnabled)
        {
            logger.LogWarning("OpenRouter AI processing is disabled. Fetch-only discovery will continue without AI triage or drafting.");
            return;
        }

        if (openRouterOptions.Value.DryRun)
        {
            logger.LogInformation("OpenRouter dry-run mode is enabled. AI requests will be logged without calling the provider.");
        }
    }

    private sealed class NoOpNewsAgentRunLease : INewsAgentRunLease
    {
        public static readonly NoOpNewsAgentRunLease Instance = new();

        public string LeaseName => string.Empty;

        public string HolderId => string.Empty;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
