using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsDraftGenerationService(
    INewsDiscoveryRepository repository,
    NewsAiRunExecutor aiRunExecutor,
    INewsDiscoveryHttpClient httpClient,
    INewsAgentGuidanceProvider guidanceProvider,
    IOptions<NewsDraftGenerationOptions> draftOptions,
    ILogger<NewsDraftGenerationService> logger)
{
    public bool IsAiEnabled => aiRunExecutor.IsAiEnabled;

    public async Task<NewsDraftRunResult> RunDraftGenerationAsync(
        NewsDraftRunOptions options,
        CancellationToken cancellationToken = default)
    {
        draftOptions.Value.Validate();

        if (!aiRunExecutor.IsAiEnabled)
        {
            logger.LogWarning("OpenRouter is disabled; draft generation skipped.");
            return new NewsDraftRunResult(0, 0, 0, 0, []);
        }

        var limit = options.PerRunCandidateLimit ?? draftOptions.Value.PerRunCandidateLimit;
        var toProcess = await repository.GetCandidatesAsync(
            NewsCandidateStatus.NeedsReview,
            take: limit,
            primaryMinConfidence: draftOptions.Value.PrimaryMinConfidenceScore,
            secondaryMinConfidence: draftOptions.Value.SecondaryMinConfidenceScore,
            cancellationToken: cancellationToken);

        if (toProcess.Count == 0)
        {
            logger.LogInformation("No needs-review candidates are ready for draft generation.");
            return new NewsDraftRunResult(0, 0, 0, 0, []);
        }

        aiRunExecutor.BeginRun();

        var created = 0;
        var skipped = 0;
        var failures = 0;
        var errors = new List<string>();

        foreach (var candidate in toProcess)
        {
            try
            {
                var result = await GenerateDraftAsync(candidate, options, cancellationToken);
                if (result.Succeeded && result.DraftId is not null)
                {
                    created++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                failures++;
                errors.Add($"Candidate {candidate.Id}: {ex.Message}");
                logger.LogError(ex, "Draft generation failed for candidate {CandidateId}.", candidate.Id);
            }
        }

        return new NewsDraftRunResult(toProcess.Count, created, skipped, failures, errors);
    }

    public async Task<NewsDraftCandidateResult> GenerateDraftAsync(
        NewsCandidate candidate,
        NewsDraftRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateDraftCandidate(candidate, options);

        var existingDraft = await repository.GetDraftByCandidateIdAsync(candidate.Id, cancellationToken);
        if (existingDraft is not null && !options.ForceRegenerate)
        {
            logger.LogInformation("Skipping candidate {CandidateId}; draft already exists.", candidate.Id);
            return new NewsDraftCandidateResult(candidate.Id, existingDraft.Id, false, null);
        }

        var source = await repository.GetSourceByIdAsync(candidate.SourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Discovery source {candidate.SourceId} was not found.");

        var evidence = await GetDraftEvidenceAsync(candidate, source, options.DryRun, cancellationToken);
        var guidance = await guidanceProvider.GetPublishedAsync(
            NewsAgentGuidanceType.Draft,
            cancellationToken);
        var messages = NewsDraftPrompt.BuildMessages(
            candidate,
            source,
            evidence,
            guidance.HasOverlay ? guidance.Content : null);
        var execution = await aiRunExecutor.ExecuteAsync(
            candidate.Id,
            NewsAiRunKind.DraftGeneration,
            NewsAiModelRole.Drafting,
            NewsDraftPrompt.Version,
            messages,
            cancellationToken: cancellationToken,
            guidance: guidance);

        if (execution.Completion.DryRun || string.IsNullOrWhiteSpace(execution.Completion.Content))
        {
            logger.LogInformation(
                "Skipped draft persistence for candidate {CandidateId} because dry-run returned no structured content.",
                candidate.Id);
            return new NewsDraftCandidateResult(candidate.Id, null, false, null);
        }

        var structuredDraft = NewsDraftMediaLinkPolicy.Enforce(
            NewsDraftQuotePolicy.Enforce(
                NewsDraftResultParser.Parse(execution.Completion.Content),
                evidence),
            evidence);
        var attribution = NewsDraftAttributionBuilder.Build(structuredDraft, candidate, source, evidence);
        var proposedSlug = NewsSlug.Slugify(
            structuredDraft.Slug ?? structuredDraft.Title);
        var sourceNotes = AppendPreservedQuotes(attribution.SourceNotes, structuredDraft.PreservedQuotes);

        if (!options.DryRun)
        {
            var draftId = await repository.UpsertDraftAsync(
                candidate.Id,
                new NewsAgentDraftUpsert(
                    structuredDraft.Title,
                    proposedSlug,
                    structuredDraft.Excerpt,
                    structuredDraft.Body,
                    attribution.AttributionText,
                    sourceNotes,
                    attribution.ConfidenceNotes,
                    structuredDraft.SuggestedPublishAt,
                    execution.AiRunId),
                cancellationToken);

            await MarkCandidateDraftedAsync(candidate, cancellationToken);

            logger.LogInformation(
                "Generated draft {DraftId} for candidate {CandidateId}.",
                draftId,
                candidate.Id);

            return new NewsDraftCandidateResult(candidate.Id, draftId, true, null);
        }

        logger.LogInformation("Dry-run draft generated for candidate {CandidateId}.", candidate.Id);
        return new NewsDraftCandidateResult(candidate.Id, null, true, null);
    }

    private async Task MarkCandidateDraftedAsync(NewsCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Status != NewsCandidateStatus.Drafted
            && !NewsCandidateWorkflow.TryValidateStatusChange(
                candidate.Status,
                NewsCandidateStatus.Drafted,
                out var markDraftedError))
        {
            throw new InvalidOperationException(
                $"Candidate {candidate.Id}: {markDraftedError}");
        }

        if (candidate.Status != NewsCandidateStatus.Drafted)
        {
            var updated = await repository.TryUpdateCandidateStatusAsync(
                candidate.Id,
                new NewsCandidateStatusUpdate(NewsCandidateStatus.Drafted),
                cancellationToken);

            if (!updated)
            {
                throw new InvalidOperationException($"Failed to mark candidate {candidate.Id} as drafted.");
            }
        }
    }

    private void ValidateDraftCandidate(NewsCandidate candidate, NewsDraftRunOptions options)
    {
        var draftStatusError = NewsCandidateWorkflow.GetDraftGenerationError(candidate.Status);
        if (!string.IsNullOrEmpty(draftStatusError))
        {
            throw new InvalidOperationException($"Candidate {candidate.Id}: {draftStatusError}");
        }

        if (candidate.Status == NewsCandidateStatus.Drafted && !options.ForceRegenerate)
        {
            throw new InvalidOperationException($"Candidate {candidate.Id} already has a draft. Use force regenerate.");
        }

        if (!options.BypassConfidenceThreshold && !MeetsConfidenceThreshold(candidate))
        {
            throw new InvalidOperationException($"Candidate {candidate.Id} is below the configured confidence threshold.");
        }

    }

    private bool MeetsConfidenceThreshold(NewsCandidate candidate)
    {
        var minimum = draftOptions.Value.MinConfidenceScore(candidate.SourceTrustTier);
        return (candidate.ConfidenceScore ?? 0m) >= minimum;
    }

    private async Task<IReadOnlyList<NewsCandidateEvidence>> GetDraftEvidenceAsync(
        NewsCandidate candidate,
        NewsDiscoverySource source,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var evidence = await repository.GetCandidateEvidenceAsync(candidate.Id, cancellationToken);

        try
        {
            var fetched = await httpClient.GetAsync(candidate.SourceUrl, cancellationToken);
            if (!OutboundUrlSafety.TryValidatePublicHttpUrl(
                    fetched.FinalUrl,
                    out var finalUrlError,
                    out var finalNormalized)
                || string.IsNullOrWhiteSpace(finalNormalized))
            {
                throw new InvalidOperationException(finalUrlError);
            }

            var parsed = NewsArticlePageParser.Parse(fetched.Body, finalNormalized);
            var evidenceExcerpt = NewsArticlePageParser.BuildEvidenceExcerpt(parsed);
            if (string.IsNullOrWhiteSpace(evidenceExcerpt))
            {
                return evidence;
            }

            var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(finalNormalized);
            var existing = evidence.FirstOrDefault(item =>
                string.Equals(item.CanonicalUrl, canonicalUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Excerpt, evidenceExcerpt, StringComparison.Ordinal));
            if (existing is not null)
            {
                return evidence;
            }

            var now = DateTime.UtcNow;
            var refreshed = new NewsCandidateEvidence(
                0,
                candidate.Id,
                finalNormalized,
                canonicalUrl,
                source.DisplayName,
                source.TrustTier,
                parsed.Title,
                candidate.SourcePublishedAt,
                evidenceExcerpt,
                NewsCandidateDedupe.ComputeContentHash(parsed.Title, evidenceExcerpt),
                now,
                null,
                now);

            if (!dryRun)
            {
                await repository.AddCandidateEvidenceAsync(
                    candidate.Id,
                    new NewsCandidateEvidenceDraft(
                        refreshed.SourceUrl,
                        refreshed.SourceName,
                        refreshed.SourceTrustTier,
                        refreshed.FetchedTitle,
                        refreshed.FetchedPublishedAt,
                        refreshed.Excerpt,
                        refreshed.Etag,
                        refreshed.FetchedAt),
                    cancellationToken);
            }

            return [.. evidence, refreshed];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not refresh source evidence for candidate {CandidateId}; drafting will use stored evidence.",
                candidate.Id);
            return evidence;
        }
    }

    private static string AppendPreservedQuotes(
        string sourceNotes,
        IReadOnlyList<NewsDraftPreservedQuote> preservedQuotes)
    {
        if (preservedQuotes.Count == 0)
        {
            return sourceNotes;
        }

        var lines = preservedQuotes.Select(quote =>
            $"- {quote.Speaker}: \"{quote.ExactText}\" ({quote.SourceUrl})");
        var block = "Preserved quotes:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(sourceNotes)
            ? block
            : sourceNotes.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
    }
}
