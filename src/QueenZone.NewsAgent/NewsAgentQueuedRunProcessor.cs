using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsAgentQueuedRunProcessor(
    INewsAgentRunRequestRepository runRequestRepository,
    INewsAgentQueuedRunExecutor executor,
    NewsAgentUrlIngestionService urlIngestionService,
    ILogger<NewsAgentQueuedRunProcessor> logger)
{
    public async Task<int> RunOnceAsync(string runnerId, CancellationToken cancellationToken = default)
    {
        var request = await runRequestRepository.ClaimNextAsync(runnerId, cancellationToken);
        if (request is null)
        {
            logger.LogInformation("No pending news gathering requests were found for runner {RunnerId}.", runnerId);
            return 0;
        }

        logger.LogInformation(
            "Runner {RunnerId} claimed news request {RequestId} ({Kind}) from {RequestedBy}.",
            runnerId,
            request.Id,
            request.Kind,
            request.RequestedBy);

        try
        {
            if (request.Kind == NewsAgentRunRequestKind.UrlIngestion)
            {
                return await RunUrlIngestionAsync(request, cancellationToken);
            }

            var result = await executor.RunAsync(cancellationToken);
            if (result.Summary?.SkippedDueToLease == true)
            {
                await runRequestRepository.ReturnToPendingAsync(request.Id, cancellationToken);
                logger.LogInformation(
                    "Returned news gathering request {RequestId} to pending because another run holds the lease.",
                    request.Id);
                return 0;
            }

            var summaryText = FormatSummary(result.Summary, result.ExitCode);
            if (result.ExitCode == 0)
            {
                await runRequestRepository.CompleteAsync(request.Id, summaryText, cancellationToken);
            }
            else
            {
                await runRequestRepository.FailAsync(request.Id, summaryText, cancellationToken);
            }

            return result.ExitCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "News gathering request {RequestId} failed.", request.Id);
            await runRequestRepository.FailAsync(
                request.Id,
                $"Run failed unexpectedly ({exception.GetType().Name}). Check the local worker logs.",
                cancellationToken);
            return 1;
        }
    }

    private async Task<int> RunUrlIngestionAsync(
        NewsAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ArticleUrl))
        {
            await runRequestRepository.FailAsync(
                request.Id,
                "URL ingestion request is missing ArticleUrl.",
                cancellationToken);
            return 1;
        }

        var result = await urlIngestionService.IngestAsync(
            request.ArticleUrl,
            request.GenerateDraft,
            cancellationToken);

        if (result.ExitCode == 0)
        {
            await runRequestRepository.CompleteAsync(request.Id, result.Summary, cancellationToken);
        }
        else
        {
            await runRequestRepository.FailAsync(request.Id, result.Summary, cancellationToken);
        }

        return result.ExitCode;
    }

    internal static string FormatSummary(NewsAgentRunSummary? summary, int exitCode)
    {
        if (summary is null)
        {
            return exitCode == 0 ? "Triage run completed." : $"Triage run failed with exit code {exitCode}.";
        }

        var discovery = summary.Discovery;
        var triage = summary.Triage;
        return $"Triage run finished with exit code {exitCode}. "
            + $"Fetched {discovery?.ItemsFetched ?? 0} items; created {discovery?.CandidatesCreated ?? 0} candidates. "
            + $"Triaged {triage?.CandidatesConsidered ?? 0}; sent {triage?.PromotedToReview ?? 0} for review; "
            + $"rejected {triage?.Rejected ?? 0}; duplicates {triage?.MarkedDuplicate ?? 0}. "
            + $"Drafts created {summary.Draft?.DraftsCreated ?? 0}; failures {summary.TotalFailures}. "
            + $"Estimated AI spend ${summary.EstimatedAiSpendUsd:0.0000}. "
            + string.Join("; ", summary.Errors);
    }
}

public interface INewsAgentQueuedRunExecutor
{
    Task<NewsAgentQueuedRunResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed record NewsAgentQueuedRunResult(int ExitCode, NewsAgentRunSummary? Summary);

public sealed class NewsAgentQueuedRunExecutor(DiscoverNewsWorker worker) : INewsAgentQueuedRunExecutor
{
    public async Task<NewsAgentQueuedRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var options = DiscoverNewsCommandOptions.Parse(["discover-news", "--scheduled"])
            ?? throw new InvalidOperationException("The scheduled triage preset is invalid.");
        var exitCode = await worker.RunAsync(options, cancellationToken);
        return new NewsAgentQueuedRunResult(exitCode, worker.LastRunSummary);
    }
}
