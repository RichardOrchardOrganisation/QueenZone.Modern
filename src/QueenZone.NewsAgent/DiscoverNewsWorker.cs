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
        LastDiscoveryResult = null;
        LastTriageResult = null;
        LastDraftResult = null;
        var stepErrors = new List<string>();
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
            exitCode = await RunStepAsync("Discovery", () => RunDiscoveryAsync(options, cancellationToken), stepErrors, cancellationToken);
            discoveryResult = LastDiscoveryResult;
        }

        if (options.Triage)
        {
            var triageExit = await RunStepAsync("Triage", () => RunTriageAsync(options, cancellationToken), stepErrors, cancellationToken);
            exitCode = Math.Max(exitCode, triageExit);
            triageResult = LastTriageResult;
        }

        if (options.Draft)
        {
            var draftExit = await RunStepAsync("Draft generation", () => RunDraftGenerationAsync(options, cancellationToken), stepErrors, cancellationToken);
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
                ExitCode: exitCode)
        { StepErrors = stepErrors };
        NewsAgentRunTelemetry.LogRunCompleted(logger, LastRunSummary);

        return exitCode;
    }

    private async Task<int> RunStepAsync(string step, Func<Task<int>> run, List<string> errors, CancellationToken cancellationToken)
    {
        try
        {
            return await run();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            errors.Add($"{step}: {exception.Message}");
            logger.LogError(exception, "News agent step {Step} failed.", step);
            return 1;
        }
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
            logger.LogWarning("Discovery error: {Error}", error);
        }

        return result.SourcesChecked > 0 && result.Failures == result.SourcesChecked ? 1 : 0;
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
            logger.LogWarning("Triage error: {Error}", error);
        }

        return triageResult.CandidatesConsidered > 0 && triageResult.Failures == triageResult.CandidatesConsidered ? 1 : 0;
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
            logger.LogWarning("Draft generation error: {Error}", error);
        }

        return !draftGenerationService.IsAiEnabled
            || (draftResult.CandidatesAttempted > 0 && draftResult.Failures == draftResult.CandidatesAttempted) ? 1 : 0;
    }

}
