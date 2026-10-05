using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed record NewsAgentUrlIngestionResult(
    int ExitCode,
    string Summary,
    int? CandidateId = null,
    bool WasDuplicate = false,
    bool DraftGenerated = false);

public class NewsAgentUrlIngestionService(
    INewsDiscoveryRepository discoveryRepository,
    INewsDiscoveryHttpClient httpClient,
    NewsTriageService triageService,
    NewsDraftGenerationService draftGenerationService,
    ILogger<NewsAgentUrlIngestionService> logger)
{
    public const string ManualSourceKey = "admin-manual-url";

    public virtual async Task<NewsAgentUrlIngestionResult> IngestAsync(
        string articleUrl,
        bool generateDraft,
        CancellationToken cancellationToken = default)
    {
        if (!OutboundUrlSafety.TryValidatePublicHttpUrl(articleUrl, out var validationError, out var normalizedUrl)
            || string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return new NewsAgentUrlIngestionResult(1, validationError);
        }

        try
        {
            var fetched = await httpClient.GetAsync(normalizedUrl, cancellationToken);
            if (!OutboundUrlSafety.TryValidatePublicHttpUrl(fetched.FinalUrl, out var finalUrlError, out var finalNormalized)
                || string.IsNullOrWhiteSpace(finalNormalized))
            {
                return new NewsAgentUrlIngestionResult(1, finalUrlError);
            }

            var parsed = NewsArticlePageParser.Parse(fetched.Body, finalNormalized);
            var evidenceExcerpt = NewsArticlePageParser.BuildEvidenceExcerpt(parsed);
            var source = await EnsureManualSourceAsync(cancellationToken);
            var now = DateTime.UtcNow;

            var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(finalNormalized);
            var canonicalUrlHash = NewsCandidateDedupe.ComputeUrlHash(canonicalUrl);
            var contentHash = NewsCandidateDedupe.ComputeContentHash(parsed.Title, evidenceExcerpt);

            var existingByUrl = await discoveryRepository.GetCandidateByCanonicalUrlHashAsync(
                canonicalUrlHash,
                cancellationToken);
            var existingByContent = existingByUrl is null
                ? await discoveryRepository.GetCandidateByContentHashAsync(contentHash, cancellationToken)
                : null;

            NewsCandidate candidate;
            var wasDuplicate = false;
            if (existingByUrl is not null || existingByContent is not null)
            {
                candidate = existingByUrl ?? existingByContent!;
                wasDuplicate = true;
                await discoveryRepository.AddCandidateEvidenceAsync(
                    candidate.Id,
                    new NewsCandidateEvidenceDraft(
                        finalNormalized,
                        parsed.SourceName,
                        source.TrustTier,
                        parsed.Title,
                        null,
                        evidenceExcerpt,
                        null,
                        now),
                    cancellationToken);
                logger.LogInformation(
                    "Reused existing candidate {CandidateId} for admin URL {ArticleUrl}.",
                    candidate.Id,
                    finalNormalized);
            }
            else
            {
                var candidateId = await discoveryRepository.CreateCandidateAsync(
                    new NewsCandidateCreateRequest(
                        source.Id,
                        finalNormalized,
                        parsed.Title,
                        null,
                        evidenceExcerpt,
                        now),
                    cancellationToken);
                candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId, cancellationToken)
                    ?? throw new InvalidOperationException($"Created candidate {candidateId} could not be reloaded.");
                logger.LogInformation(
                    "Created candidate {CandidateId} from admin URL {ArticleUrl}.",
                    candidate.Id,
                    finalNormalized);
            }

            candidate = await discoveryRepository.GetCandidateByIdAsync(candidate.Id, cancellationToken)
                ?? candidate;

            if (candidate.Status == NewsCandidateStatus.Discovered)
            {
                await triageService.TriageCandidateAsync(
                    candidate,
                    new NewsTriageRunOptions(DryRun: false),
                    cancellationToken);
                candidate = await discoveryRepository.GetCandidateByIdAsync(candidate.Id, cancellationToken)
                    ?? candidate;
            }

            var draftGenerated = false;
            if (generateDraft)
            {
                if (candidate.Status is NewsCandidateStatus.NeedsReview or NewsCandidateStatus.Drafted)
                {
                    // Explicit override may draft even when confidence is low; never auto-publish.
                    var draftResult = await draftGenerationService.GenerateDraftAsync(
                        candidate,
                        new NewsDraftRunOptions(
                            DryRun: false,
                            ForceRegenerate: candidate.Status == NewsCandidateStatus.Drafted,
                            BypassConfidenceThreshold: true),
                        cancellationToken);
                    draftGenerated = draftResult.Succeeded && draftResult.DraftId is not null;
                    candidate = await discoveryRepository.GetCandidateByIdAsync(candidate.Id, cancellationToken)
                        ?? candidate;
                }
                else if (candidate.Status == NewsCandidateStatus.Discovered)
                {
                    return new NewsAgentUrlIngestionResult(
                        1,
                        $"Fetched candidate #{candidate.Id}, but triage did not promote it for drafting (status remains Discovered).",
                        candidate.Id,
                        wasDuplicate);
                }
                else
                {
                    return new NewsAgentUrlIngestionResult(
                        0,
                        $"URL matched candidate #{candidate.Id} with status {candidate.Status}; draft generation was skipped.",
                        candidate.Id,
                        wasDuplicate);
                }
            }

            var summary = wasDuplicate
                ? $"Reused candidate #{candidate.Id} for {finalNormalized} (status {candidate.Status}"
                : $"Created candidate #{candidate.Id} for {finalNormalized} (status {candidate.Status}";
            var skippedDraftSummary = generateDraft ? "; draft not generated)." : "; triage-only, no draft).";
            summary += draftGenerated ? "; draft generated)." : skippedDraftSummary;

            return new NewsAgentUrlIngestionResult(
                0,
                summary,
                candidate.Id,
                wasDuplicate,
                draftGenerated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Admin URL ingestion failed for {ArticleUrl}.", normalizedUrl);
            return new NewsAgentUrlIngestionResult(1, $"URL ingestion failed: {ex.Message}");
        }
    }

    private async Task<NewsDiscoverySource> EnsureManualSourceAsync(CancellationToken cancellationToken)
    {
        var existing = await discoveryRepository.GetSourceByKeyAsync(ManualSourceKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var sourceId = await discoveryRepository.UpsertSourceAsync(
            new NewsDiscoverySourceDraft(
                ManualSourceKey,
                "Admin manual URL",
                "https://www.queenzone.org/",
                null,
                NewsDiscoverySourceType.AllowlistedPage,
                NewsDiscoveryTrustTier.Secondary,
                PollIntervalMinutes: 10_080,
                Enabled: true,
                RelevanceKeywords: null),
            cancellationToken);

        return await discoveryRepository.GetSourceByIdAsync(sourceId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to create the admin manual URL discovery source.");
    }
}
