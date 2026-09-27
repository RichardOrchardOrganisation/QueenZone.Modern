using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class SharedNewsDiscoveryStore
{
    private readonly object sync = new();
    private readonly List<NewsDiscoverySourceEntity> sources = [];
    private readonly List<NewsCandidateEntity> candidates = [];
    private readonly List<NewsCandidateEvidenceEntity> evidence = [];
    private readonly List<NewsAiRunEntity> aiRuns = [];
    private readonly List<NewsAgentDraftEntity> drafts = [];
    private int nextSourceId = 1;
    private int nextCandidateId = 1;
    private int nextEvidenceId = 1;
    private int nextAiRunId = 1;
    private int nextDraftId = 1;

    public IReadOnlyList<NewsDiscoverySourceEntity> GetSources(bool enabledOnly)
    {
        lock (sync)
        {
            var query = sources.AsEnumerable();
            if (enabledOnly)
            {
                query = query.Where(source => source.Enabled);
            }

            return query.OrderBy(source => source.DisplayName).ToList();
        }
    }

    public NewsDiscoverySourceEntity? GetSourceByKey(string key)
    {
        lock (sync)
        {
            return sources.SingleOrDefault(source => string.Equals(source.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public NewsDiscoverySourceEntity? GetSourceById(int sourceId)
    {
        lock (sync)
        {
            return sources.SingleOrDefault(source => source.Id == sourceId);
        }
    }

    public int UpsertSource(NewsDiscoverySourceDraft draft)
    {
        lock (sync)
        {
            var existing = sources.SingleOrDefault(source => string.Equals(source.Key, draft.Key, StringComparison.OrdinalIgnoreCase));
            var timestamp = DateTime.UtcNow;
            if (existing is null)
            {
                var entity = new NewsDiscoverySourceEntity
                {
                    Id = nextSourceId++,
                    Key = draft.Key,
                    DisplayName = draft.DisplayName,
                    HomepageUrl = draft.HomepageUrl,
                    FeedOrSiteUrl = draft.FeedOrSiteUrl,
                    SourceType = draft.SourceType,
                    TrustTier = draft.TrustTier,
                    PollIntervalMinutes = draft.PollIntervalMinutes,
                    Enabled = draft.Enabled,
                    RelevanceKeywords = draft.RelevanceKeywords,
                    CreatedAt = timestamp,
                    UpdatedAt = timestamp
                };
                sources.Add(entity);
                return entity.Id;
            }

            existing.DisplayName = draft.DisplayName;
            existing.HomepageUrl = draft.HomepageUrl;
            existing.FeedOrSiteUrl = draft.FeedOrSiteUrl;
            existing.SourceType = draft.SourceType;
            existing.TrustTier = draft.TrustTier;
            existing.PollIntervalMinutes = draft.PollIntervalMinutes;
            existing.Enabled = draft.Enabled;
            existing.RelevanceKeywords = draft.RelevanceKeywords;
            existing.UpdatedAt = timestamp;
            return existing.Id;
        }
    }

    public void MarkSourceFetched(int sourceId, DateTime fetchedAt)
    {
        lock (sync)
        {
            var source = sources.SingleOrDefault(item => item.Id == sourceId)
                ?? throw new InvalidOperationException($"News discovery source {sourceId} was not found.");
            source.LastFetchedAt = fetchedAt;
            source.UpdatedAt = fetchedAt;
        }
    }

    public NewsCandidateEntity? GetCandidateByCanonicalUrlHash(string canonicalUrlHash)
    {
        lock (sync)
        {
            return candidates.SingleOrDefault(candidate => candidate.CanonicalUrlHash == canonicalUrlHash);
        }
    }

    public NewsCandidateEntity? GetCandidateByContentHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return null;
        }

        lock (sync)
        {
            return candidates.SingleOrDefault(candidate => candidate.ContentHash == contentHash);
        }
    }

    public NewsCandidateEntity? FindEarlierDuplicateCandidate(int candidateId, string sourceTitle, string? contentHash)
    {
        lock (sync)
        {
            if (!string.IsNullOrWhiteSpace(contentHash))
            {
                var byContent = candidates.SingleOrDefault(candidate => candidate.ContentHash == contentHash);
                if (byContent is not null
                    && byContent.Id != candidateId
                    && NewsCandidateDuplicateRules.IsActiveDuplicateSource(byContent.Status))
                {
                    return byContent;
                }
            }

            var titleHash = NewsCandidateDedupe.ComputeContentHash(sourceTitle, null);
            return candidates
                .Where(candidate =>
                    candidate.Id != candidateId
                    && NewsCandidateDuplicateRules.IsActiveDuplicateSource(candidate.Status)
                    && NewsCandidateDedupe.ComputeContentHash(candidate.SourceTitle, null) == titleHash)
                .OrderBy(candidate => candidate.DiscoveredAt)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();
        }
    }

    public NewsCandidateEntity? GetCandidateById(int candidateId)
    {
        lock (sync)
        {
            return candidates.SingleOrDefault(candidate => candidate.Id == candidateId);
        }
    }

    public NewsCandidateEntity? GetCandidateByPromotedNewsId(int promotedNewsId)
    {
        lock (sync)
        {
            return candidates
                .Where(candidate => candidate.PromotedNewsId == promotedNewsId)
                .OrderByDescending(candidate => candidate.Id)
                .FirstOrDefault();
        }
    }

    public void ClearPromotedNewsLinks(int promotedNewsId)
    {
        lock (sync)
        {
            var timestamp = DateTime.UtcNow;
            foreach (var candidate in candidates.Where(candidate => candidate.PromotedNewsId == promotedNewsId))
            {
                candidate.PromotedNewsId = null;
                candidate.UpdatedAt = timestamp;
            }
        }
    }

    public IReadOnlyList<NewsCandidateEntity> GetCandidates(
        NewsCandidateStatus? status,
        int? sourceId,
        int? take = null,
        decimal? primaryMinConfidence = null,
        decimal? secondaryMinConfidence = null)
    {
        lock (sync)
        {
            var query = candidates.AsEnumerable();
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
                {
                    var trustTier = sources.SingleOrDefault(item => item.Id == candidate.SourceId)?.TrustTier
                        ?? NewsDiscoveryTrustTier.LowConfidence;
                    var score = candidate.ConfidenceScore ?? 0m;
                    return trustTier == NewsDiscoveryTrustTier.Primary
                        ? score >= primaryMin
                        : score >= secondaryMin;
                });
            }

            query = query
                .OrderByDescending(candidate => candidate.DiscoveredAt)
                .ThenByDescending(candidate => candidate.Id);

            if (take is not null)
            {
                query = query.Take(Math.Max(take.Value, 0));
            }

            return query.ToList();
        }
    }

    public IReadOnlyList<NewsCandidateReviewListItem> ListCandidatesForReview(NewsCandidateListQuery query) =>
        ListCandidatesForReviewPage(query).Items;

    public NewsCandidateReviewPage ListCandidatesForReviewPage(NewsCandidateListQuery query)
    {
        lock (sync)
        {
            var (page, pageSize) = NewsCandidateListQueryDefaults.Normalize(query.Page, query.PageSize);

            var queryable = NewsCandidateReviewQuery.ApplyActiveQueueFilter(
                NewsCandidateReviewQuery.ApplyEntityFilters(
                    candidates,
                    query,
                    sourceId => sources.SingleOrDefault(item => item.Id == sourceId)),
                query);

            if (query.HasDraft == true)
            {
                queryable = queryable.Where(candidate => drafts.Any(draft => draft.CandidateId == candidate.Id));
            }
            else if (query.HasDraft == false)
            {
                queryable = queryable.Where(candidate => drafts.All(draft => draft.CandidateId != candidate.Id));
            }

            if (!string.IsNullOrWhiteSpace(query.Entity))
            {
                var entity = query.Entity.Trim();
                queryable = queryable.Where(candidate =>
                    aiRuns.Any(run =>
                        run.CandidateId == candidate.Id
                        && run.Kind == NewsAiRunKind.Triage
                        && run.StructuredResultJson != null
                        && run.StructuredResultJson.Contains(entity, StringComparison.OrdinalIgnoreCase)));
            }

            var ordered = queryable
                .OrderByDescending(candidate => candidate.DiscoveredAt)
                .ThenByDescending(candidate => candidate.Id)
                .ToList();

            var pageItems = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(candidate =>
                {
                    var source = sources.Single(item => item.Id == candidate.SourceId);
                    var draft = drafts.SingleOrDefault(item => item.CandidateId == candidate.Id);
                    return new NewsCandidateReviewListItem(
                        candidate.Id,
                        candidate.SourceTitle,
                        candidate.SourceUrl,
                        source.DisplayName,
                        source.TrustTier,
                        candidate.Status,
                        candidate.RelevanceScore,
                        candidate.ConfidenceScore,
                        candidate.DiscoveredAt,
                        candidate.DuplicateOfCandidateId,
                        draft?.ProposedTitle);
                })
                .ToList();

            return new NewsCandidateReviewPage(pageItems, ordered.Count, page, pageSize);
        }
    }

    public int CreateCandidate(NewsCandidateCreateRequest request)
    {
        lock (sync)
        {
            var source = sources.SingleOrDefault(item => item.Id == request.SourceId)
                ?? throw new InvalidOperationException($"News discovery source {request.SourceId} was not found.");

            var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(request.SourceUrl);
            var canonicalUrlHash = NewsCandidateDedupe.ComputeUrlHash(canonicalUrl);
            if (candidates.Any(candidate => candidate.CanonicalUrlHash == canonicalUrlHash))
            {
                throw new InvalidOperationException($"A candidate already exists for canonical URL hash {canonicalUrlHash}.");
            }

            var timestamp = request.DiscoveredAt;
            var contentHash = NewsCandidateDedupe.ComputeContentHash(request.SourceTitle, request.Excerpt);
            var candidate = new NewsCandidateEntity
            {
                Id = nextCandidateId++,
                SourceId = source.Id,
                SourceUrl = request.SourceUrl,
                CanonicalUrl = canonicalUrl,
                CanonicalUrlHash = canonicalUrlHash,
                SourceTitle = request.SourceTitle,
                SourcePublishedAt = request.SourcePublishedAt,
                DiscoveredAt = request.DiscoveredAt,
                ContentHash = contentHash,
                Status = NewsCandidateStatus.Discovered,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                Source = source
            };
            candidates.Add(candidate);

            evidence.Add(new NewsCandidateEvidenceEntity
            {
                Id = nextEvidenceId++,
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
                CreatedAt = timestamp,
                Candidate = candidate
            });

            return candidate.Id;
        }
    }

    public bool TryUpdateCandidateStatus(int candidateId, NewsCandidateStatusUpdate update)
    {
        lock (sync)
        {
            var candidate = candidates.SingleOrDefault(item => item.Id == candidateId);
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
            return true;
        }
    }

    public int AddCandidateEvidence(int candidateId, NewsCandidateEvidenceDraft draft)
    {
        lock (sync)
        {
            var candidate = candidates.SingleOrDefault(item => item.Id == candidateId)
                ?? throw new InvalidOperationException($"News candidate {candidateId} was not found.");
            var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(draft.SourceUrl);
            var timestamp = DateTime.UtcNow;
            var entity = new NewsCandidateEvidenceEntity
            {
                Id = nextEvidenceId++,
                CandidateId = candidateId,
                SourceUrl = draft.SourceUrl,
                CanonicalUrl = canonicalUrl,
                SourceName = draft.SourceName,
                SourceTrustTier = draft.SourceTrustTier,
                FetchedTitle = draft.FetchedTitle,
                FetchedPublishedAt = draft.FetchedPublishedAt,
                Excerpt = draft.Excerpt,
                ContentHash = NewsCandidateDedupe.ComputeContentHash(draft.FetchedTitle, draft.Excerpt),
                FetchedAt = draft.FetchedAt,
                Etag = draft.Etag,
                CreatedAt = timestamp,
                Candidate = candidate
            };
            evidence.Add(entity);
            candidate.ContentHash = entity.ContentHash ?? candidate.ContentHash;
            candidate.UpdatedAt = timestamp;
            return entity.Id;
        }
    }

    public IReadOnlyList<NewsCandidateEvidenceEntity> GetCandidateEvidence(int candidateId)
    {
        lock (sync)
        {
            return evidence
                .Where(item => item.CandidateId == candidateId)
                .OrderByDescending(item => item.FetchedAt)
                .ThenByDescending(item => item.Id)
                .ToList();
        }
    }

    public int CreateAiRun(NewsAiRunCreateRequest request)
    {
        lock (sync)
        {
            var candidate = candidates.SingleOrDefault(item => item.Id == request.CandidateId)
                ?? throw new InvalidOperationException($"News candidate {request.CandidateId} was not found.");
            var entity = new NewsAiRunEntity
            {
                Id = nextAiRunId++,
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
                CreatedAt = request.StartedAt,
                Candidate = candidate
            };
            aiRuns.Add(entity);
            return entity.Id;
        }
    }

    public void CompleteAiRun(int aiRunId, NewsAiRunCompletion completion)
    {
        lock (sync)
        {
            var run = aiRuns.SingleOrDefault(item => item.Id == aiRunId)
                ?? throw new InvalidOperationException($"News AI run {aiRunId} was not found.");
            run.Status = completion.Status;
            run.InputTokens = completion.InputTokens;
            run.OutputTokens = completion.OutputTokens;
            run.EstimatedCostUsd = completion.EstimatedCostUsd;
            run.StructuredResultJson = completion.StructuredResultJson;
            run.ErrorMessage = completion.ErrorMessage;
            run.CompletedAt = completion.CompletedAt;
        }
    }

    public IReadOnlyList<NewsAiRunEntity> GetAiRunsForCandidate(int candidateId)
    {
        lock (sync)
        {
            return aiRuns
                .Where(item => item.CandidateId == candidateId)
                .OrderByDescending(item => item.StartedAt)
                .ThenByDescending(item => item.Id)
                .ToList();
        }
    }

    public decimal GetEstimatedAiSpendUsd(DateTime fromUtc, DateTime toUtc)
    {
        lock (sync)
        {
            return aiRuns
                .Where(run =>
                    run.CompletedAt is not null
                    && run.CompletedAt >= fromUtc
                    && run.CompletedAt < toUtc
                    && run.EstimatedCostUsd is not null)
                .Sum(run => run.EstimatedCostUsd!.Value);
        }
    }

    public NewsAgentDraftEntity? GetDraftByCandidateId(int candidateId)
    {
        lock (sync)
        {
            return drafts.SingleOrDefault(item => item.CandidateId == candidateId);
        }
    }

    public int UpsertDraft(int candidateId, NewsAgentDraftUpsert draft)
    {
        lock (sync)
        {
            var candidate = candidates.SingleOrDefault(item => item.Id == candidateId)
                ?? throw new InvalidOperationException($"News candidate {candidateId} was not found.");
            var timestamp = DateTime.UtcNow;
            var existing = drafts.SingleOrDefault(item => item.CandidateId == candidateId);
            if (existing is null)
            {
                var entity = new NewsAgentDraftEntity
                {
                    Id = nextDraftId++,
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
                    UpdatedAt = timestamp,
                    Candidate = candidate
                };
                drafts.Add(entity);
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
            return existing.Id;
        }
    }

    internal void Clear()
    {
        lock (sync)
        {
            sources.Clear();
            candidates.Clear();
            evidence.Clear();
            aiRuns.Clear();
            drafts.Clear();
            nextSourceId = 1;
            nextCandidateId = 1;
            nextEvidenceId = 1;
            nextAiRunId = 1;
            nextDraftId = 1;
        }
    }
}
