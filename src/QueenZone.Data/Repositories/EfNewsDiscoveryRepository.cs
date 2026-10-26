using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfNewsDiscoveryRepository(QueenZoneDbContext dbContext) : INewsDiscoveryRepository
{
    public async Task<IReadOnlyList<NewsDiscoverySource>> GetSourcesAsync(bool enabledOnly = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.NewsDiscoverySources.AsNoTracking();
        if (enabledOnly)
        {
            query = query.Where(source => source.Enabled);
        }

        return await query
            .OrderBy(source => source.DisplayName)
            .Select(source => MapSource(source))
            .ToListAsync(cancellationToken);
    }

    public async Task<NewsDiscoverySource?> GetSourceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var source = await dbContext.NewsDiscoverySources
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);

        return source is null ? null : MapSource(source);
    }

    public async Task<NewsDiscoverySource?> GetSourceByIdAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        var source = await dbContext.NewsDiscoverySources
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sourceId, cancellationToken);

        return source is null ? null : MapSource(source);
    }

    public async Task<int> UpsertSourceAsync(NewsDiscoverySourceDraft source, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.NewsDiscoverySources
            .SingleOrDefaultAsync(item => item.Key == source.Key, cancellationToken);
        var timestamp = DateTime.UtcNow;
        if (existing is null)
        {
            var entity = new NewsDiscoverySourceEntity
            {
                Key = source.Key,
                DisplayName = source.DisplayName,
                HomepageUrl = source.HomepageUrl,
                FeedOrSiteUrl = source.FeedOrSiteUrl,
                SourceType = source.SourceType,
                TrustTier = source.TrustTier,
                PollIntervalMinutes = source.PollIntervalMinutes,
                Enabled = source.Enabled,
                RelevanceKeywords = source.RelevanceKeywords,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            };
            dbContext.NewsDiscoverySources.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }

        existing.DisplayName = source.DisplayName;
        existing.HomepageUrl = source.HomepageUrl;
        existing.FeedOrSiteUrl = source.FeedOrSiteUrl;
        existing.SourceType = source.SourceType;
        existing.TrustTier = source.TrustTier;
        existing.PollIntervalMinutes = source.PollIntervalMinutes;
        existing.Enabled = source.Enabled;
        existing.RelevanceKeywords = source.RelevanceKeywords;
        existing.UpdatedAt = timestamp;
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing.Id;
    }

    public async Task<int> MarkSourceFailedAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.NewsDiscoverySources
            .Where(source => source.Id == sourceId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(source => source.ConsecutiveFailureCount, source => source.ConsecutiveFailureCount + 1),
                cancellationToken);
        if (updated == 0)
        {
            throw new InvalidOperationException($"News discovery source {sourceId} was not found.");
        }

        return await dbContext.NewsDiscoverySources.Where(source => source.Id == sourceId)
            .Select(source => source.ConsecutiveFailureCount).SingleAsync(cancellationToken);
    }

    public async Task MarkSourceFetchedAsync(int sourceId, DateTime fetchedAt, CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.NewsDiscoverySources
            .Where(source => source.Id == sourceId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.ConsecutiveFailureCount, 0)
                    .SetProperty(source => source.LastFetchedAt, fetchedAt)
                    .SetProperty(source => source.UpdatedAt, fetchedAt),
                cancellationToken);

        if (updated == 0)
        {
            throw new InvalidOperationException($"News discovery source {sourceId} was not found.");
        }
    }

    public async Task<NewsCandidate?> GetCandidateByCanonicalUrlHashAsync(string canonicalUrlHash, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.NewsCandidates
            .AsNoTracking()
            .Include(item => item.Source)
            .SingleOrDefaultAsync(item => item.CanonicalUrlHash == canonicalUrlHash, cancellationToken);

        return candidate is null ? null : MapCandidate(candidate);
    }

    public async Task<NewsCandidate?> GetCandidateByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return null;
        }

        var candidate = await dbContext.NewsCandidates
            .AsNoTracking()
            .Include(item => item.Source)
            .SingleOrDefaultAsync(item => item.ContentHash == contentHash, cancellationToken);

        return candidate is null ? null : MapCandidate(candidate);
    }

    public async Task<NewsCandidate?> FindEarlierDuplicateCandidateAsync(
        int candidateId,
        string sourceTitle,
        string? contentHash,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            var byContent = await GetCandidateByContentHashAsync(contentHash, cancellationToken);
            if (byContent is not null
                && byContent.Id != candidateId
                && NewsCandidateDuplicateRules.IsActiveDuplicateSource(byContent.Status))
            {
                return byContent;
            }
        }

        var titleHash = NewsCandidateDedupe.ComputeContentHash(sourceTitle, null);
        var candidates = await dbContext.NewsCandidates
            .AsNoTracking()
            .Include(item => item.Source)
            .Where(candidate =>
                candidate.Id != candidateId
                && candidate.Status != NewsCandidateStatus.Rejected
                && candidate.Status != NewsCandidateStatus.IgnoredDuplicate)
            .ToListAsync(cancellationToken);

        var duplicate = candidates
            .Where(candidate => NewsCandidateDedupe.ComputeContentHash(candidate.SourceTitle, null) == titleHash)
            .OrderBy(candidate => candidate.DiscoveredAt)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();

        return duplicate is null ? null : MapCandidate(duplicate);
    }

    public async Task<NewsCandidate?> GetCandidateByIdAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.NewsCandidates
            .AsNoTracking()
            .Include(item => item.Source)
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);

        return candidate is null ? null : MapCandidate(candidate);
    }

    public async Task<NewsCandidate?> GetCandidateByPromotedNewsIdAsync(int promotedNewsId, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.NewsCandidates
            .AsNoTracking()
            .Include(item => item.Source)
            .Where(item => item.PromotedNewsId == promotedNewsId)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return candidate is null ? null : MapCandidate(candidate);
    }

    public async Task ClearPromotedNewsLinksAsync(int promotedNewsId, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow;
        await dbContext.NewsCandidates
            .Where(candidate => candidate.PromotedNewsId == promotedNewsId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.PromotedNewsId, (int?)null)
                    .SetProperty(candidate => candidate.UpdatedAt, timestamp),
                cancellationToken);
    }

    public async Task<IReadOnlyList<NewsCandidate>> GetCandidatesAsync(
        NewsCandidateStatus? status = null,
        int? sourceId = null,
        int? take = null,
        decimal? primaryMinConfidence = null,
        decimal? secondaryMinConfidence = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.NewsCandidates.AsNoTracking().Include(item => item.Source).AsQueryable();
        if (status is not null)
        {
            query = query.Where(candidate => candidate.Status == status);
        }

        if (sourceId is not null)
        {
            query = query.Where(candidate => candidate.SourceId == sourceId);
        }

        if (primaryMinConfidence is not null && secondaryMinConfidence is not null)
        {
            var primaryMin = primaryMinConfidence.Value;
            var secondaryMin = secondaryMinConfidence.Value;
            query = query.Where(candidate =>
                (candidate.Source.TrustTier == NewsDiscoveryTrustTier.Primary
                    && (candidate.ConfidenceScore ?? 0m) >= primaryMin)
                || (candidate.Source.TrustTier != NewsDiscoveryTrustTier.Primary
                    && (candidate.ConfidenceScore ?? 0m) >= secondaryMin));
        }

        query = query
            .OrderByDescending(candidate => candidate.DiscoveredAt)
            .ThenByDescending(candidate => candidate.Id);

        if (take is not null)
        {
            query = query.Take(Math.Max(take.Value, 0));
        }

        return await query
            .Select(candidate => MapCandidate(candidate))
            .ToListAsync(cancellationToken);
    }

    public async Task<NewsCandidateReviewPage> ListCandidatesForReviewAsync(
        NewsCandidateListQuery query,
        CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = NewsCandidateListQueryDefaults.Normalize(query.Page, query.PageSize);

        var candidates = NewsCandidateReviewQuery.ApplyActiveQueueFilter(
            NewsCandidateReviewQuery.ApplyEntityFilters(
                dbContext.NewsCandidates.AsNoTracking().Include(item => item.Source).AsQueryable(),
                query),
            query);

        if (query.HasDraft == true)
        {
            candidates = candidates.Where(candidate =>
                dbContext.NewsAgentDrafts.Any(draft => draft.CandidateId == candidate.Id));
        }
        else if (query.HasDraft == false)
        {
            candidates = candidates.Where(candidate =>
                !dbContext.NewsAgentDrafts.Any(draft => draft.CandidateId == candidate.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            var entity = query.Entity.Trim();
            candidates = candidates.Where(candidate =>
                dbContext.NewsAiRuns.Any(run =>
                    run.CandidateId == candidate.Id
                    && run.Kind == NewsAiRunKind.Triage
                    && run.StructuredResultJson != null
                    && run.StructuredResultJson.Contains(entity)));
        }

        var totalCount = await candidates.CountAsync(cancellationToken);

        var items = await candidates
            .OrderByDescending(candidate => candidate.DiscoveredAt)
            .ThenByDescending(candidate => candidate.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(candidate => new NewsCandidateReviewListItem(
                candidate.Id,
                candidate.SourceTitle,
                candidate.SourceUrl,
                candidate.Source.DisplayName,
                candidate.Source.TrustTier,
                candidate.Status,
                candidate.RelevanceScore,
                candidate.ConfidenceScore,
                candidate.DiscoveredAt,
                candidate.DuplicateOfCandidateId,
                dbContext.NewsAgentDrafts
                    .Where(draft => draft.CandidateId == candidate.Id)
                    .Select(draft => draft.ProposedTitle)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new NewsCandidateReviewPage(items, totalCount, page, pageSize);
    }

    public async Task<int> CreateCandidateAsync(NewsCandidateCreateRequest request, CancellationToken cancellationToken = default)
    {
        var source = await dbContext.NewsDiscoverySources
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.SourceId, cancellationToken)
            ?? throw new InvalidOperationException($"News discovery source {request.SourceId} was not found.");

        var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(request.SourceUrl);
        var canonicalUrlHash = NewsCandidateDedupe.ComputeUrlHash(canonicalUrl);
        var duplicateExists = await dbContext.NewsCandidates
            .AnyAsync(candidate => candidate.CanonicalUrlHash == canonicalUrlHash, cancellationToken);
        if (duplicateExists)
        {
            throw new InvalidOperationException($"A candidate already exists for canonical URL hash {canonicalUrlHash}.");
        }

        var contentHash = NewsCandidateDedupe.ComputeContentHash(request.SourceTitle, request.Excerpt);
        var candidate = new NewsCandidateEntity
        {
            SourceId = source.Id,
            SourceUrl = request.SourceUrl,
            CanonicalUrl = canonicalUrl,
            CanonicalUrlHash = canonicalUrlHash,
            SourceTitle = request.SourceTitle,
            SourcePublishedAt = request.SourcePublishedAt,
            DiscoveredAt = request.DiscoveredAt,
            ContentHash = contentHash,
            Status = NewsCandidateStatus.Discovered,
            CreatedAt = request.DiscoveredAt,
            UpdatedAt = request.DiscoveredAt
        };

        dbContext.NewsCandidates.Add(candidate);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.NewsCandidateEvidence.Add(new NewsCandidateEvidenceEntity
        {
            CandidateId = candidate.Id,
            SourceUrl = request.SourceUrl,
            CanonicalUrl = canonicalUrl,
            SourceName = source.DisplayName,
            SourceTrustTier = source.TrustTier,
            FetchedTitle = request.SourceTitle,
            FetchedPublishedAt = request.SourcePublishedAt,
            Excerpt = request.Excerpt,
            ContentHash = contentHash,
            FetchedAt = request.DiscoveredAt,
            CreatedAt = request.DiscoveredAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return candidate.Id;
    }

    public async Task<bool> TryUpdateCandidateStatusAsync(
        int candidateId,
        NewsCandidateStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.NewsCandidates
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            return false;
        }

        if (!NewsCandidateWorkflow.TryTransition(candidate.Status, update.Status, out _))
        {
            return false;
        }

        candidate.Status = update.Status;
        candidate.ReviewNotes = update.ReviewNotes ?? candidate.ReviewNotes;
        candidate.RelevanceScore = update.RelevanceScore ?? candidate.RelevanceScore;
        candidate.ConfidenceScore = update.ConfidenceScore ?? candidate.ConfidenceScore;
        candidate.DuplicateOfCandidateId = update.DuplicateOfCandidateId ?? candidate.DuplicateOfCandidateId;
        candidate.PromotedNewsId = update.PromotedNewsId ?? candidate.PromotedNewsId;
        candidate.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> AddCandidateEvidenceAsync(int candidateId, NewsCandidateEvidenceDraft evidence, CancellationToken cancellationToken = default)
    {
        var candidate = await dbContext.NewsCandidates
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null)
        {
            throw new InvalidOperationException($"News candidate {candidateId} was not found.");
        }

        var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(evidence.SourceUrl);
        var timestamp = DateTime.UtcNow;
        var entity = new NewsCandidateEvidenceEntity
        {
            CandidateId = candidateId,
            SourceUrl = evidence.SourceUrl,
            CanonicalUrl = canonicalUrl,
            SourceName = evidence.SourceName,
            SourceTrustTier = evidence.SourceTrustTier,
            FetchedTitle = evidence.FetchedTitle,
            FetchedPublishedAt = evidence.FetchedPublishedAt,
            Excerpt = evidence.Excerpt,
            ContentHash = NewsCandidateDedupe.ComputeContentHash(evidence.FetchedTitle, evidence.Excerpt),
            FetchedAt = evidence.FetchedAt,
            Etag = evidence.Etag,
            CreatedAt = timestamp
        };
        dbContext.NewsCandidateEvidence.Add(entity);

        // Update via the change tracker so the evidence INSERT and candidate UPDATE
        // are committed atomically in the same SaveChangesAsync call.
        candidate.ContentHash = entity.ContentHash;
        candidate.UpdatedAt = timestamp;

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<NewsCandidateEvidence>> GetCandidateEvidenceAsync(int candidateId, CancellationToken cancellationToken = default) =>
        await dbContext.NewsCandidateEvidence
            .AsNoTracking()
            .Where(item => item.CandidateId == candidateId)
            .OrderByDescending(item => item.FetchedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => MapEvidence(item))
            .ToListAsync(cancellationToken);

    public async Task<int> CreateAiRunAsync(NewsAiRunCreateRequest request, CancellationToken cancellationToken = default)
    {
        var candidateExists = await dbContext.NewsCandidates.AnyAsync(item => item.Id == request.CandidateId, cancellationToken);
        if (!candidateExists)
        {
            throw new InvalidOperationException($"News candidate {request.CandidateId} was not found.");
        }

        var entity = new NewsAiRunEntity
        {
            CandidateId = request.CandidateId,
            Kind = request.Kind,
            ModelProvider = request.ModelProvider,
            ModelId = request.ModelId,
            PromptVersion = request.PromptVersion,
            GuidanceRevisionId = request.GuidanceRevisionId,
            GuidanceRevisionNumber = request.GuidanceRevisionNumber,
            GuidanceContentHash = request.GuidanceContentHash,
            Status = NewsAiRunStatus.Pending,
            StartedAt = request.StartedAt,
            CreatedAt = request.StartedAt
        };
        dbContext.NewsAiRuns.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task CompleteAiRunAsync(int aiRunId, NewsAiRunCompletion completion, CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.NewsAiRuns
            .Where(run => run.Id == aiRunId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, completion.Status)
                    .SetProperty(run => run.InputTokens, completion.InputTokens)
                    .SetProperty(run => run.OutputTokens, completion.OutputTokens)
                    .SetProperty(run => run.EstimatedCostUsd, completion.EstimatedCostUsd)
                    .SetProperty(run => run.StructuredResultJson, completion.StructuredResultJson)
                    .SetProperty(run => run.ErrorMessage, completion.ErrorMessage)
                    .SetProperty(run => run.CompletedAt, completion.CompletedAt),
                cancellationToken);

        if (updated == 0)
        {
            throw new InvalidOperationException($"News AI run {aiRunId} was not found.");
        }
    }

    public async Task<IReadOnlyList<NewsAiRun>> GetAiRunsForCandidateAsync(int candidateId, CancellationToken cancellationToken = default) =>
        await dbContext.NewsAiRuns
            .AsNoTracking()
            .Where(run => run.CandidateId == candidateId)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => MapAiRun(run))
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetEstimatedAiSpendUsdAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var spend = await dbContext.NewsAiRuns
            .AsNoTracking()
            .Where(run =>
                run.CompletedAt != null
                && run.CompletedAt >= fromUtc
                && run.CompletedAt < toUtc
                && run.EstimatedCostUsd != null)
            .Select(run => run.EstimatedCostUsd!.Value)
            .SumAsync(cancellationToken);

        return spend;
    }

    public async Task<NewsAgentDraft?> GetDraftByCandidateIdAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var draft = await dbContext.NewsAgentDrafts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CandidateId == candidateId, cancellationToken);

        return draft is null ? null : MapDraft(draft);
    }

    public async Task<int> UpsertDraftAsync(int candidateId, NewsAgentDraftUpsert draft, CancellationToken cancellationToken = default)
    {
        var candidateExists = await dbContext.NewsCandidates.AnyAsync(item => item.Id == candidateId, cancellationToken);
        if (!candidateExists)
        {
            throw new InvalidOperationException($"News candidate {candidateId} was not found.");
        }

        var existing = await dbContext.NewsAgentDrafts
            .SingleOrDefaultAsync(item => item.CandidateId == candidateId, cancellationToken);
        var timestamp = DateTime.UtcNow;
        if (existing is null)
        {
            var entity = new NewsAgentDraftEntity
            {
                CandidateId = candidateId,
                AiRunId = draft.AiRunId,
                ProposedTitle = draft.ProposedTitle,
                ProposedSlug = draft.ProposedSlug,
                ProposedExcerpt = draft.ProposedExcerpt,
                ProposedBody = draft.ProposedBody,
                AttributionText = draft.AttributionText,
                SourceNotes = draft.SourceNotes,
                ConfidenceNotes = draft.ConfidenceNotes,
                SuggestedPublishAt = draft.SuggestedPublishAt,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            };
            dbContext.NewsAgentDrafts.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }

        existing.AiRunId = draft.AiRunId ?? existing.AiRunId;
        existing.ProposedTitle = draft.ProposedTitle;
        existing.ProposedSlug = draft.ProposedSlug;
        existing.ProposedExcerpt = draft.ProposedExcerpt;
        existing.ProposedBody = draft.ProposedBody;
        existing.AttributionText = draft.AttributionText;
        existing.SourceNotes = draft.SourceNotes;
        existing.ConfidenceNotes = draft.ConfidenceNotes;
        existing.SuggestedPublishAt = draft.SuggestedPublishAt;
        existing.UpdatedAt = timestamp;
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing.Id;
    }

    private static NewsDiscoverySource MapSource(NewsDiscoverySourceEntity source) =>
        new(
            source.Id,
            source.Key,
            source.DisplayName,
            source.HomepageUrl,
            source.FeedOrSiteUrl,
            source.SourceType,
            source.TrustTier,
            source.PollIntervalMinutes,
            source.Enabled,
            source.RelevanceKeywords,
            source.LastFetchedAt,
            source.CreatedAt,
            source.UpdatedAt);

    private static NewsCandidate MapCandidate(NewsCandidateEntity candidate) =>
        new(
            candidate.Id,
            candidate.SourceId,
            candidate.SourceUrl,
            candidate.CanonicalUrl,
            candidate.CanonicalUrlHash,
            candidate.SourceTitle,
            candidate.SourcePublishedAt,
            candidate.DiscoveredAt,
            candidate.ContentHash,
            candidate.Status,
            candidate.RelevanceScore,
            candidate.ConfidenceScore,
            candidate.DuplicateOfCandidateId,
            candidate.PromotedNewsId,
            candidate.ReviewNotes,
            candidate.CreatedAt,
            candidate.UpdatedAt,
            candidate.Source?.Key ?? "unknown",
            candidate.Source?.DisplayName ?? "Unknown source",
            candidate.Source?.TrustTier ?? NewsDiscoveryTrustTier.LowConfidence);

    private static NewsCandidateEvidence MapEvidence(NewsCandidateEvidenceEntity evidence) =>
        new(
            evidence.Id,
            evidence.CandidateId,
            evidence.SourceUrl,
            evidence.CanonicalUrl,
            evidence.SourceName,
            evidence.SourceTrustTier,
            evidence.FetchedTitle,
            evidence.FetchedPublishedAt,
            evidence.Excerpt,
            evidence.ContentHash,
            evidence.FetchedAt,
            evidence.Etag,
            evidence.CreatedAt);

    private static NewsAiRun MapAiRun(NewsAiRunEntity run) =>
        new(
            run.Id,
            run.CandidateId,
            run.Kind,
            run.ModelProvider,
            run.ModelId,
            run.PromptVersion,
            run.Status,
            run.InputTokens,
            run.OutputTokens,
            run.EstimatedCostUsd,
            run.StructuredResultJson,
            run.ErrorMessage,
            run.StartedAt,
            run.CompletedAt,
            run.CreatedAt,
            run.GuidanceRevisionId,
            run.GuidanceRevisionNumber,
            run.GuidanceContentHash);

    private static NewsAgentDraft MapDraft(NewsAgentDraftEntity draft) =>
        new(
            draft.Id,
            draft.CandidateId,
            draft.AiRunId,
            draft.ProposedTitle,
            draft.ProposedSlug,
            draft.ProposedExcerpt,
            draft.ProposedBody,
            draft.AttributionText,
            draft.SourceNotes,
            draft.ConfidenceNotes,
            draft.SuggestedPublishAt,
            draft.CreatedAt,
            draft.UpdatedAt);
}
