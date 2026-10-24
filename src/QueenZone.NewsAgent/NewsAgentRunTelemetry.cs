using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed record NewsAgentRunSummary(
    bool SkippedDueToLease,
    bool AiEnabled,
    bool DryRun,
    NewsDiscoveryRunResult? Discovery,
    NewsTriageRunResult? Triage,
    NewsDraftRunResult? Draft,
    decimal EstimatedAiSpendUsd,
    int ExitCode)
{
    public IReadOnlyList<string> StepErrors { get; init; } = [];

    public IReadOnlyList<string> Errors =>
        [.. Discovery?.Errors ?? [], .. Triage?.Errors ?? [], .. Draft?.Errors ?? [], .. StepErrors];

    public int TotalFailures =>
        StepErrors.Count +
        (Discovery?.Failures ?? 0)
        + (Triage?.Failures ?? 0)
        + (Draft?.Failures ?? 0);
}

public static class NewsAgentRunTelemetry
{
    public static readonly EventId RunCompleted = new(4100, "NewsAgentRunCompleted");

    public static void LogRunCompleted(ILogger logger, NewsAgentRunSummary summary)
    {
        if (summary.SkippedDueToLease)
        {
            logger.LogInformation(
                RunCompleted,
                "News agent run skipped because another instance holds the run lease.");
            return;
        }

        logger.LogInformation(
            RunCompleted,
            "News agent run completed. ExitCode={ExitCode} AiEnabled={AiEnabled} DryRun={DryRun} " +
            "SourcesChecked={SourcesChecked} CandidatesCreated={CandidatesCreated} " +
            "TriageConsidered={TriageConsidered} TriagePromoted={TriagePromoted} TriageRejected={TriageRejected} TriageDuplicates={TriageDuplicates} " +
            "DraftsCreated={DraftsCreated} TotalFailures={TotalFailures} EstimatedAiSpendUsd={EstimatedAiSpendUsd} Errors={Errors}",
            summary.ExitCode,
            summary.AiEnabled,
            summary.DryRun,
            summary.Discovery?.SourcesChecked ?? 0,
            summary.Discovery?.CandidatesCreated ?? 0,
            summary.Triage?.CandidatesConsidered ?? 0,
            summary.Triage?.PromotedToReview ?? 0,
            summary.Triage?.Rejected ?? 0,
            summary.Triage?.MarkedDuplicate ?? 0,
            summary.Draft?.DraftsCreated ?? 0,
            summary.TotalFailures,
            summary.EstimatedAiSpendUsd,
            string.Join("; ", summary.Errors));
    }
}
