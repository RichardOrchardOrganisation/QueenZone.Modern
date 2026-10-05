using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class DiscoverNewsWorker(
    NewsDiscoveryService discoveryService,
    NewsTriageService triageService,
    NewsDraftGenerationService draftGenerationService,
    INewsDiscoveryRepository discoveryRepository,
    NewsAgentRunPreparation runPreparation,
    ILogger<DiscoverNewsWorker> logger)
{
    public NewsAgentRunSummary? LastRunSummary { get; private set; }

    public async Task<int> RunAsync(
        DiscoverNewsCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var runLease = await runPreparation.PrepareAsync(options, cancellationToken);
        if (runLease is null)
        {
            LastRunSummary = new NewsAgentRunSummary(
                    SkippedDueToLease: true,
                    AiEnabled: runPreparation.IsAiEnabled,
                    DryRun: runPreparation.IsDryRun(options),
                    Discovery: null,
                    Triage: null,
                    Draft: null,
                    EstimatedAiSpendUsd: 0,
                    ExitCode: 0);
            NewsAgentRunTelemetry.LogRunCompleted(logger, LastRunSummary);
            return 0;
        }

        NewsDiscoveryRunResult? discoveryResult = null;
        NewsTriageRunResult? triageResult = null;
        NewsDraftRunResult? draftResult = null;
        var exitCode = 0;

        if (!options.TriageOnly && !options.DraftOnly)
        {
            exitCode = await RunDiscoveryAsync(options, cancellationToken);
            discoveryResult = LastDiscoveryResult;
        }

        if (options.Triage)
        {
            var triageExit = await RunTriageAsync(options, cancellationToken);
            exitCode = Math.Max(exitCode, triageExit);
            triageResult = LastTriageResult;
        }

        if (options.Draft)
        {
            var draftExit = await RunDraftGenerationAsync(options, cancellationToken);
            exitCode = Math.Max(exitCode, draftExit);
            draftResult = LastDraftResult;
        }

        var spend = await discoveryRepository.GetEstimatedAiSpendUsdAsync(
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(1),
            cancellationToken);

        LastRunSummary = new NewsAgentRunSummary(
                SkippedDueToLease: false,
                AiEnabled: runPreparation.IsAiEnabled,
                DryRun: runPreparation.IsDryRun(options),
                Discovery: discoveryResult,
                Triage: triageResult,
                Draft: draftResult,
                EstimatedAiSpendUsd: spend,
                ExitCode: exitCode);
        NewsAgentRunTelemetry.LogRunCompleted(logger, LastRunSummary);

        return exitCode;
    }

    private NewsDiscoveryRunResult? LastDiscoveryResult { get; set; }

    private NewsTriageRunResult? LastTriageResult { get; set; }

    private NewsDraftRunResult? LastDraftResult { get; set; }

    private async Task<int> RunDiscoveryAsync(
        DiscoverNewsCommandOptions options,
        CancellationToken cancellationToken)
    {
        var runOptions = new NewsDiscoveryRunOptions(
            SeedSources: options.SeedSources,
            FetchOnly: true,
            DryRun: options.DryRun,
            Force: options.Force);

        var result = await discoveryService.RunFetchAsync(runOptions, cancellationToken);
        LastDiscoveryResult = result;

        logger.LogInformation(
            "Discovery finished. Sources checked={SourcesChecked}, skipped={SourcesSkipped}, items={ItemsFetched}, created={CandidatesCreated}, duplicates={DuplicatesSkipped}, keyword filtered={KeywordFiltered}, failures={Failures}.",
            result.SourcesChecked,
            result.SourcesSkipped,
            result.ItemsFetched,
            result.CandidatesCreated,
            result.DuplicatesSkipped,
            result.KeywordFiltered,
            result.Failures);

        foreach (var error in result.Errors)
        {
            logger.LogError("Discovery error: {Error}", error);
        }

        return result.Failures > 0 ? 1 : 0;
    }

    private async Task<int> RunTriageAsync(
        DiscoverNewsCommandOptions options,
        CancellationToken cancellationToken)
    {
        var triageResult = await triageService.RunTriageAsync(
            new NewsTriageRunOptions(DryRun: options.DryRun),
            cancellationToken);
        LastTriageResult = triageResult;

        logger.LogInformation(
            "Triage finished. Considered={CandidatesConsidered}, promoted={PromotedToReview}, rejected={Rejected}, duplicates={MarkedDuplicate}, skipped={Skipped}, failures={Failures}.",
            triageResult.CandidatesConsidered,
            triageResult.PromotedToReview,
            triageResult.Rejected,
            triageResult.MarkedDuplicate,
            triageResult.Skipped,
            triageResult.Failures);

        foreach (var error in triageResult.Errors)
        {
            logger.LogError("Triage error: {Error}", error);
        }

        return triageResult.Failures > 0 ? 1 : 0;
    }

    private async Task<int> RunDraftGenerationAsync(
        DiscoverNewsCommandOptions options,
        CancellationToken cancellationToken)
    {
        var draftResult = await draftGenerationService.RunDraftGenerationAsync(
            new NewsDraftRunOptions(
                DryRun: options.DryRun,
                ForceRegenerate: options.Force),
            cancellationToken);
        LastDraftResult = draftResult;

        logger.LogInformation(
            "Draft generation finished. Considered={CandidatesConsidered}, created={DraftsCreated}, skipped={Skipped}, failures={Failures}.",
            draftResult.CandidatesConsidered,
            draftResult.DraftsCreated,
            draftResult.Skipped,
            draftResult.Failures);

        foreach (var error in draftResult.Errors)
        {
            logger.LogError("Draft generation error: {Error}", error);
        }

        return draftResult.Failures > 0 ? 1 : 0;
    }

}
