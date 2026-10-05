using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsTriageService(
    INewsDiscoveryRepository repository,
    NewsAiRunExecutor aiRunExecutor,
    NewsTriageDeterministicAnalyzer deterministicAnalyzer,
    INewsAgentGuidanceProvider guidanceProvider,
    IOptions<NewsTriageOptions> triageOptions,
    ILogger<NewsTriageService> logger)
{
    public bool IsAiEnabled => aiRunExecutor.IsAiEnabled;

    public async Task<NewsTriageRunResult> RunTriageAsync(
        NewsTriageRunOptions options,
        CancellationToken cancellationToken = default)
    {
        triageOptions.Value.Validate();

        var limit = options.PerRunCandidateLimit ?? triageOptions.Value.PerRunCandidateLimit;
        var toProcess = await repository.GetCandidatesAsync(
            NewsCandidateStatus.Discovered,
            take: limit,
            cancellationToken: cancellationToken);

        if (toProcess.Count == 0)
        {
            logger.LogInformation("No discovered candidates require triage.");
            return new NewsTriageRunResult(0, 0, 0, 0, 0, 0, []);
        }

        if (aiRunExecutor.IsAiEnabled)
        {
            aiRunExecutor.BeginRun();
        }
        else
        {
            logger.LogWarning("OpenRouter is disabled; running deterministic triage checks only.");
        }

        var promoted = 0;
        var rejected = 0;
        var duplicates = 0;
        var skipped = 0;
        var failures = 0;
        var errors = new List<string>();

        foreach (var candidate in toProcess)
        {
            try
            {
                var result = await TriageCandidateAsync(candidate, options, cancellationToken);
                switch (result.Decision.TargetStatus)
                {
                    case NewsCandidateStatus.NeedsReview:
                        promoted++;
                        break;
                    case NewsCandidateStatus.Rejected:
                        rejected++;
                        break;
                    case NewsCandidateStatus.IgnoredDuplicate:
                        duplicates++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                failures++;
                errors.Add($"Candidate {candidate.Id}: {ex.Message}");
                logger.LogError(ex, "Triage failed for candidate {CandidateId}.", candidate.Id);
            }
        }

        return new NewsTriageRunResult(
            toProcess.Count,
            promoted,
            rejected,
            duplicates,
            skipped,
            failures,
            errors);
    }

    public async Task<NewsTriageCandidateResult> TriageCandidateAsync(
        NewsCandidate candidate,
        NewsTriageRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var triageError = NewsCandidateWorkflow.GetTriageError(candidate.Status);
        if (!string.IsNullOrEmpty(triageError))
        {
            throw new InvalidOperationException($"Candidate {candidate.Id}: {triageError}");
        }

        var source = await repository.GetSourceByIdAsync(candidate.SourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Discovery source {candidate.SourceId} was not found.");

        var evidence = await repository.GetCandidateEvidenceAsync(candidate.Id, cancellationToken);
        var deterministic = await deterministicAnalyzer.AnalyzeAsync(candidate, source, evidence, cancellationToken);
        NewsTriageStructuredResult? aiResult = null;

        if (deterministic.DuplicateOfCandidateId is null && aiRunExecutor.IsAiEnabled)
        {
            var guidance = await guidanceProvider.GetPublishedAsync(
                NewsAgentGuidanceType.Triage,
                cancellationToken);
            var messages = NewsTriagePrompt.BuildMessages(
                candidate,
                source,
                evidence,
                guidance.HasOverlay ? guidance.Content : null);
            var execution = await aiRunExecutor.ExecuteAsync(
                candidate.Id,
                NewsAiRunKind.Triage,
                new NewsAiChatRequest(NewsAiModelRole.Triage, NewsTriagePrompt.Version, messages),
                options.RunAtUtc,
                cancellationToken,
                guidance);

            if (!execution.Completion.DryRun && !string.IsNullOrWhiteSpace(execution.Completion.Content))
            {
                aiResult = NewsTriageResultParser.Parse(execution.Completion.Content);
            }
            else
            {
                logger.LogInformation(
                    "Skipped AI triage parsing for candidate {CandidateId} because dry-run returned no structured content.",
                    candidate.Id);
            }
        }

        var decision = NewsTriageDecisionEngine.Decide(
            source.TrustTier,
            deterministic,
            aiResult,
            triageOptions.Value);

        if (!options.DryRun && decision.TargetStatus != NewsCandidateStatus.Discovered)
        {
            if (!NewsCandidateWorkflow.TryValidateStatusChange(
                    candidate.Status,
                    decision.TargetStatus,
                    out var transitionError))
            {
                throw new InvalidOperationException(
                    $"Candidate {candidate.Id}: {transitionError}");
            }

            var updated = await repository.TryUpdateCandidateStatusAsync(
                candidate.Id,
                new NewsCandidateStatusUpdate(
                    decision.TargetStatus,
                    decision.ReviewNotes,
                    decision.RelevanceScore,
                    decision.ConfidenceScore,
                    decision.DuplicateOfCandidateId),
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException($"Failed to update candidate {candidate.Id} to {decision.TargetStatus}.");
            }
        }

        logger.LogInformation(
            "Triaged candidate {CandidateId} to {TargetStatus} with verdict {Verdict}. AI used={UsedAi}.",
            candidate.Id,
            decision.TargetStatus,
            decision.Verdict,
            decision.UsedAi);

        return new NewsTriageCandidateResult(candidate.Id, decision, true, null);
    }
}
