namespace QueenZone.Data;

public sealed class InMemoryNewsDiscoveryRepository(SharedNewsDiscoveryStore store) : INewsDiscoveryRepository
{
    public Task<IReadOnlyList<NewsDiscoverySource>> GetSourcesAsync(bool enabledOnly = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsDiscoverySource>>(store.GetSources(enabledOnly).Select(MapSource).ToList());

    public Task<NewsDiscoverySource?> GetSourceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var source = store.GetSourceByKey(key);
        return Task.FromResult(source is null ? null : MapSource(source));
    }

    public Task<NewsDiscoverySource?> GetSourceByIdAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        var source = store.GetSourceById(sourceId);
        return Task.FromResult(source is null ? null : MapSource(source));
    }

    public Task<int> UpsertSourceAsync(NewsDiscoverySourceDraft source, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.UpsertSource(source));

    public Task<int> MarkSourceFailedAsync(int sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.MarkSourceFailed(sourceId));

    public Task MarkSourceFetchedAsync(int sourceId, DateTime fetchedAt, CancellationToken cancellationToken = default)
    {
        store.MarkSourceFetched(sourceId, fetchedAt);
        return Task.CompletedTask;
    }

    public Task<NewsCandidate?> GetCandidateByCanonicalUrlHashAsync(string canonicalUrlHash, CancellationToken cancellationToken = default)
    {
        var candidate = store.GetCandidateByCanonicalUrlHash(canonicalUrlHash);
        return Task.FromResult(candidate is null ? null : MapCandidate(candidate));
    }

    public Task<NewsCandidate?> GetCandidateByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        var candidate = store.GetCandidateByContentHash(contentHash);
        return Task.FromResult(candidate is null ? null : MapCandidate(candidate));
    }

    public Task<NewsCandidate?> FindEarlierDuplicateCandidateAsync(
        int candidateId,
        string sourceTitle,
        string? contentHash,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.FindEarlierDuplicateCandidate(candidateId, sourceTitle, contentHash) is { } candidate
            ? MapCandidate(candidate)
            : null);

    public Task<NewsCandidate?> GetCandidateByIdAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = store.GetCandidateById(candidateId);
        return Task.FromResult(candidate is null ? null : MapCandidate(candidate));
    }

    public Task<NewsCandidate?> GetCandidateByPromotedNewsIdAsync(int promotedNewsId, CancellationToken cancellationToken = default)
    {
        var candidate = store.GetCandidateByPromotedNewsId(promotedNewsId);
        return Task.FromResult(candidate is null ? null : MapCandidate(candidate));
    }

    public Task ClearPromotedNewsLinksAsync(int promotedNewsId, CancellationToken cancellationToken = default)
    {
        store.ClearPromotedNewsLinks(promotedNewsId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NewsCandidate>> GetCandidatesAsync(
        NewsCandidateStatus? status = null,
        int? sourceId = null,
        int? take = null,
        decimal? primaryMinConfidence = null,
        decimal? secondaryMinConfidence = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsCandidate>>(
            store.GetCandidates(status, sourceId, take, primaryMinConfidence, secondaryMinConfidence)
                .Select(MapCandidate)
                .ToList());

    public Task<NewsCandidateReviewPage> ListCandidatesForReviewAsync(
        NewsCandidateListQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ListCandidatesForReviewPage(query));

    public Task<int> CreateCandidateAsync(NewsCandidateCreateRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.CreateCandidate(request));

    public Task<bool> TryUpdateCandidateStatusAsync(
        int candidateId,
        NewsCandidateStatusUpdate update,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.TryUpdateCandidateStatus(candidateId, update));

    public Task<int> AddCandidateEvidenceAsync(int candidateId, NewsCandidateEvidenceDraft evidence, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.AddCandidateEvidence(candidateId, evidence));

    public Task<IReadOnlyList<NewsCandidateEvidence>> GetCandidateEvidenceAsync(int candidateId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsCandidateEvidence>>(store.GetCandidateEvidence(candidateId).Select(MapEvidence).ToList());

    public Task<int> CreateAiRunAsync(NewsAiRunCreateRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.CreateAiRun(request));

    public Task CompleteAiRunAsync(int aiRunId, NewsAiRunCompletion completion, CancellationToken cancellationToken = default)
    {
        store.CompleteAiRun(aiRunId, completion);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NewsAiRun>> GetAiRunsForCandidateAsync(int candidateId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsAiRun>>(store.GetAiRunsForCandidate(candidateId).Select(MapAiRun).ToList());

    public Task<decimal> GetEstimatedAiSpendUsdAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetEstimatedAiSpendUsd(fromUtc, toUtc));

    public Task<NewsAgentDraft?> GetDraftByCandidateIdAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var draft = store.GetDraftByCandidateId(candidateId);
        return Task.FromResult(draft is null ? null : MapDraft(draft));
    }

    public Task<int> UpsertDraftAsync(int candidateId, NewsAgentDraftUpsert draft, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.UpsertDraft(candidateId, draft));

    private static NewsDiscoverySource MapSource(Entities.NewsDiscoverySourceEntity source) =>
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

    private static NewsCandidate MapCandidate(Entities.NewsCandidateEntity candidate) =>
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

    private static NewsCandidateEvidence MapEvidence(Entities.NewsCandidateEvidenceEntity evidence) =>
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

    private static NewsAiRun MapAiRun(Entities.NewsAiRunEntity run) =>
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

    private static NewsAgentDraft MapDraft(Entities.NewsAgentDraftEntity draft) =>
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
