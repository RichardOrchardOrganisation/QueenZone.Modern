namespace QueenZone.Data;

public interface INewsDiscoveryRepository
{
    Task<IReadOnlyList<NewsDiscoverySource>> GetSourcesAsync(bool enabledOnly = false, CancellationToken cancellationToken = default);

    Task<NewsDiscoverySource?> GetSourceByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<NewsDiscoverySource?> GetSourceByIdAsync(int sourceId, CancellationToken cancellationToken = default);

    Task<int> UpsertSourceAsync(NewsDiscoverySourceDraft source, CancellationToken cancellationToken = default);

    Task<int> MarkSourceFailedAsync(int sourceId, CancellationToken cancellationToken = default);

    Task MarkSourceFetchedAsync(int sourceId, DateTime fetchedAt, CancellationToken cancellationToken = default);

    Task<NewsCandidate?> GetCandidateByCanonicalUrlHashAsync(string canonicalUrlHash, CancellationToken cancellationToken = default);

    Task<NewsCandidate?> GetCandidateByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);

    Task<NewsCandidate?> FindEarlierDuplicateCandidateAsync(
        int candidateId,
        string sourceTitle,
        string? contentHash,
        CancellationToken cancellationToken = default);

    Task<NewsCandidate?> GetCandidateByIdAsync(int candidateId, CancellationToken cancellationToken = default);

    Task<NewsCandidate?> GetCandidateByPromotedNewsIdAsync(int promotedNewsId, CancellationToken cancellationToken = default);

    Task ClearPromotedNewsLinksAsync(int promotedNewsId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NewsCandidate>> GetCandidatesAsync(
        NewsCandidateStatus? status = null,
        int? sourceId = null,
        int? take = null,
        decimal? primaryMinConfidence = null,
        decimal? secondaryMinConfidence = null,
        CancellationToken cancellationToken = default);

    Task<NewsCandidateReviewPage> ListCandidatesForReviewAsync(
        NewsCandidateListQuery query,
        CancellationToken cancellationToken = default);

    Task<int> CreateCandidateAsync(NewsCandidateCreateRequest request, CancellationToken cancellationToken = default);

    Task<bool> TryUpdateCandidateStatusAsync(
        int candidateId,
        NewsCandidateStatusUpdate update,
        CancellationToken cancellationToken = default);

    Task<int> AddCandidateEvidenceAsync(int candidateId, NewsCandidateEvidenceDraft evidence, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NewsCandidateEvidence>> GetCandidateEvidenceAsync(int candidateId, CancellationToken cancellationToken = default);

    Task<int> CreateAiRunAsync(NewsAiRunCreateRequest request, CancellationToken cancellationToken = default);

    Task CompleteAiRunAsync(int aiRunId, NewsAiRunCompletion completion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NewsAiRun>> GetAiRunsForCandidateAsync(int candidateId, CancellationToken cancellationToken = default);

    Task<decimal> GetEstimatedAiSpendUsdAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<NewsAgentDraft?> GetDraftByCandidateIdAsync(int candidateId, CancellationToken cancellationToken = default);

    Task<int> UpsertDraftAsync(int candidateId, NewsAgentDraftUpsert draft, CancellationToken cancellationToken = default);
}
