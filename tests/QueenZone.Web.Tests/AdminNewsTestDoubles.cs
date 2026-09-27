using QueenZone.Data;

namespace QueenZone.Web.Tests;

internal sealed class AdminNewsMutationOverrides
{
    public Exception? CreateException { get; set; }

    public Exception? DeleteException { get; set; }

    public void Reset()
    {
        CreateException = null;
        DeleteException = null;
    }
}

internal sealed class GatedAdminNewsRepository(IAdminNewsRepository inner, AdminNewsMutationOverrides overrides)
    : IAdminNewsRepository
{
    public Task<IReadOnlyList<AdminNewsArticle>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllAsync(cancellationToken);

    public Task<AdminNewsArticlePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        inner.GetPageAsync(page, pageSize, cancellationToken);

    public Task<AdminNewsArticle?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<int> CreateDraftAsync(AdminNewsDraft draft, string editorEmail, CancellationToken cancellationToken = default) =>
        overrides.CreateException is { } createException
            ? Task.FromException<int>(createException)
            : inner.CreateDraftAsync(draft, editorEmail, cancellationToken);

    public Task UpdateAsync(
        int id,
        AdminNewsDraft draft,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(id, draft, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task PublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.PublishAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task UnpublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UnpublishAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task DeleteAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        overrides.DeleteException is { } deleteException
            ? Task.FromException(deleteException)
            : inner.DeleteAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task<bool> IsSlugInUseAsync(string slug, int? excludeNewsId = null, CancellationToken cancellationToken = default) =>
        inner.IsSlugInUseAsync(slug, excludeNewsId, cancellationToken);

    public Task<bool> TrySetForumTopicIdAsync(int newsId, int topicId, CancellationToken cancellationToken = default) =>
        inner.TrySetForumTopicIdAsync(newsId, topicId, cancellationToken);
}

internal sealed class FailingCreateAdminNewsRepository(InMemoryAdminNewsRepository inner, Exception createException) : IAdminNewsRepository
{
    public Task<IReadOnlyList<AdminNewsArticle>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllAsync(cancellationToken);

    public Task<AdminNewsArticlePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        inner.GetPageAsync(page, pageSize, cancellationToken);

    public Task<AdminNewsArticle?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<int> CreateDraftAsync(AdminNewsDraft draft, string editorEmail, CancellationToken cancellationToken = default) =>
        Task.FromException<int>(createException);

    public Task UpdateAsync(
        int id,
        AdminNewsDraft draft,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(id, draft, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task PublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.PublishAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task UnpublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UnpublishAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task DeleteAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task<bool> IsSlugInUseAsync(string slug, int? excludeNewsId = null, CancellationToken cancellationToken = default) =>
        inner.IsSlugInUseAsync(slug, excludeNewsId, cancellationToken);

    public Task<bool> TrySetForumTopicIdAsync(int newsId, int topicId, CancellationToken cancellationToken = default) =>
        inner.TrySetForumTopicIdAsync(newsId, topicId, cancellationToken);
}

internal sealed class FailingDeleteAdminNewsRepository(InMemoryAdminNewsRepository inner, Exception deleteException) : IAdminNewsRepository
{
    public Task<IReadOnlyList<AdminNewsArticle>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllAsync(cancellationToken);

    public Task<AdminNewsArticlePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        inner.GetPageAsync(page, pageSize, cancellationToken);

    public Task<AdminNewsArticle?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<int> CreateDraftAsync(AdminNewsDraft draft, string editorEmail, CancellationToken cancellationToken = default) =>
        inner.CreateDraftAsync(draft, editorEmail, cancellationToken);

    public Task UpdateAsync(
        int id,
        AdminNewsDraft draft,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(id, draft, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task PublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.PublishAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task UnpublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UnpublishAsync(id, editorEmail, expectedUpdatedAt, cancellationToken);

    public Task DeleteAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(deleteException);

    public Task<bool> IsSlugInUseAsync(string slug, int? excludeNewsId = null, CancellationToken cancellationToken = default) =>
        inner.IsSlugInUseAsync(slug, excludeNewsId, cancellationToken);

    public Task<bool> TrySetForumTopicIdAsync(int newsId, int topicId, CancellationToken cancellationToken = default) =>
        inner.TrySetForumTopicIdAsync(newsId, topicId, cancellationToken);
}

internal sealed class ConfigurableNewsDiscoveryRepository(INewsDiscoveryRepository inner) : INewsDiscoveryRepository
{
    public Func<int, CancellationToken, Task<NewsCandidate?>>? GetCandidateByPromotedNewsIdHandler { get; set; }

    public Func<int, CancellationToken, Task>? ClearPromotedNewsLinksHandler { get; set; }

    public Func<int, NewsCandidateStatusUpdate, CancellationToken, Task<bool>>? TryUpdateCandidateStatusHandler { get; set; }

    public Func<int, CancellationToken, Task<NewsAgentDraft?>>? GetDraftByCandidateIdHandler { get; set; }

    public void ResetHandlers()
    {
        GetCandidateByPromotedNewsIdHandler = null;
        ClearPromotedNewsLinksHandler = null;
        TryUpdateCandidateStatusHandler = null;
        GetDraftByCandidateIdHandler = null;
    }

    public Task<IReadOnlyList<NewsDiscoverySource>> GetSourcesAsync(bool enabledOnly = false, CancellationToken cancellationToken = default) =>
        inner.GetSourcesAsync(enabledOnly, cancellationToken);

    public Task<NewsDiscoverySource?> GetSourceByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        inner.GetSourceByKeyAsync(key, cancellationToken);

    public Task<NewsDiscoverySource?> GetSourceByIdAsync(int sourceId, CancellationToken cancellationToken = default) =>
        inner.GetSourceByIdAsync(sourceId, cancellationToken);

    public Task<int> UpsertSourceAsync(NewsDiscoverySourceDraft source, CancellationToken cancellationToken = default) =>
        inner.UpsertSourceAsync(source, cancellationToken);

    public Task MarkSourceFetchedAsync(int sourceId, DateTime fetchedAt, CancellationToken cancellationToken = default) =>
        inner.MarkSourceFetchedAsync(sourceId, fetchedAt, cancellationToken);

    public Task<NewsCandidate?> GetCandidateByCanonicalUrlHashAsync(string canonicalUrlHash, CancellationToken cancellationToken = default) =>
        inner.GetCandidateByCanonicalUrlHashAsync(canonicalUrlHash, cancellationToken);

    public Task<NewsCandidate?> GetCandidateByContentHashAsync(string contentHash, CancellationToken cancellationToken = default) =>
        inner.GetCandidateByContentHashAsync(contentHash, cancellationToken);

    public Task<NewsCandidate?> FindEarlierDuplicateCandidateAsync(
        int candidateId,
        string sourceTitle,
        string? contentHash,
        CancellationToken cancellationToken = default) =>
        inner.FindEarlierDuplicateCandidateAsync(candidateId, sourceTitle, contentHash, cancellationToken);

    public Task<NewsCandidate?> GetCandidateByIdAsync(int candidateId, CancellationToken cancellationToken = default) =>
        inner.GetCandidateByIdAsync(candidateId, cancellationToken);

    public Task<NewsCandidate?> GetCandidateByPromotedNewsIdAsync(int promotedNewsId, CancellationToken cancellationToken = default) =>
        GetCandidateByPromotedNewsIdHandler?.Invoke(promotedNewsId, cancellationToken)
        ?? inner.GetCandidateByPromotedNewsIdAsync(promotedNewsId, cancellationToken);

    public Task ClearPromotedNewsLinksAsync(int promotedNewsId, CancellationToken cancellationToken = default) =>
        ClearPromotedNewsLinksHandler?.Invoke(promotedNewsId, cancellationToken)
        ?? inner.ClearPromotedNewsLinksAsync(promotedNewsId, cancellationToken);

    public Task<IReadOnlyList<NewsCandidate>> GetCandidatesAsync(
        NewsCandidateStatus? status = null,
        int? sourceId = null,
        int? take = null,
        decimal? primaryMinConfidence = null,
        decimal? secondaryMinConfidence = null,
        CancellationToken cancellationToken = default) =>
        inner.GetCandidatesAsync(status, sourceId, take, primaryMinConfidence, secondaryMinConfidence, cancellationToken);

    public Task<NewsCandidateReviewPage> ListCandidatesForReviewAsync(
        NewsCandidateListQuery query,
        CancellationToken cancellationToken = default) =>
        inner.ListCandidatesForReviewAsync(query, cancellationToken);

    public Task<int> CreateCandidateAsync(NewsCandidateCreateRequest request, CancellationToken cancellationToken = default) =>
        inner.CreateCandidateAsync(request, cancellationToken);

    public Task<bool> TryUpdateCandidateStatusAsync(
        int candidateId,
        NewsCandidateStatusUpdate update,
        CancellationToken cancellationToken = default) =>
        TryUpdateCandidateStatusHandler?.Invoke(candidateId, update, cancellationToken)
        ?? inner.TryUpdateCandidateStatusAsync(candidateId, update, cancellationToken);

    public Task<int> AddCandidateEvidenceAsync(int candidateId, NewsCandidateEvidenceDraft evidence, CancellationToken cancellationToken = default) =>
        inner.AddCandidateEvidenceAsync(candidateId, evidence, cancellationToken);

    public Task<IReadOnlyList<NewsCandidateEvidence>> GetCandidateEvidenceAsync(int candidateId, CancellationToken cancellationToken = default) =>
        inner.GetCandidateEvidenceAsync(candidateId, cancellationToken);

    public Task<int> CreateAiRunAsync(NewsAiRunCreateRequest request, CancellationToken cancellationToken = default) =>
        inner.CreateAiRunAsync(request, cancellationToken);

    public Task CompleteAiRunAsync(int aiRunId, NewsAiRunCompletion completion, CancellationToken cancellationToken = default) =>
        inner.CompleteAiRunAsync(aiRunId, completion, cancellationToken);

    public Task<IReadOnlyList<NewsAiRun>> GetAiRunsForCandidateAsync(int candidateId, CancellationToken cancellationToken = default) =>
        inner.GetAiRunsForCandidateAsync(candidateId, cancellationToken);

    public Task<decimal> GetEstimatedAiSpendUsdAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        inner.GetEstimatedAiSpendUsdAsync(fromUtc, toUtc, cancellationToken);

    public Task<NewsAgentDraft?> GetDraftByCandidateIdAsync(int candidateId, CancellationToken cancellationToken = default) =>
        GetDraftByCandidateIdHandler?.Invoke(candidateId, cancellationToken)
        ?? inner.GetDraftByCandidateIdAsync(candidateId, cancellationToken);

    public Task<int> UpsertDraftAsync(int candidateId, NewsAgentDraftUpsert draft, CancellationToken cancellationToken = default) =>
        inner.UpsertDraftAsync(candidateId, draft, cancellationToken);
}

internal sealed class ConfigurableNewsSuggestionRepository(INewsSuggestionRepository inner) : INewsSuggestionRepository
{
    public Func<Guid, int, string, string?, CancellationToken, Task<NewsSuggestion?>>? PromoteHandler { get; init; }

    public Task<NewsSuggestion> CreateAsync(NewsSuggestion suggestion, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(suggestion, cancellationToken);

    public Task<IReadOnlyList<NewsSuggestionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        inner.GetPendingAsync(page, pageSize, cancellationToken);

    public Task<NewsSuggestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<SubmissionListPage<NewsSuggestion>> GetBySubmitterAsync(
        Guid submitterMemberId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        inner.GetBySubmitterAsync(submitterMemberId, page, pageSize, cancellationToken);

    public Task<NewsSuggestion?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        inner.UpdateStatusAsync(id, status, reviewerEmail, reviewNotes, cancellationToken);

    public Task<bool> HasActiveDuplicateAsync(string urlHash, CancellationToken cancellationToken = default) =>
        inner.HasActiveDuplicateAsync(urlHash, cancellationToken);

    public Task<int> CountBySubmitterSinceAsync(
        Guid submitterMemberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        inner.CountBySubmitterSinceAsync(submitterMemberId, sinceUtc, cancellationToken);

    public Task<NewsSuggestion?> PromoteAsync(
        Guid id,
        int promotedNewsId,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        PromoteHandler?.Invoke(id, promotedNewsId, reviewerEmail, reviewNotes, cancellationToken)
        ?? inner.PromoteAsync(id, promotedNewsId, reviewerEmail, reviewNotes, cancellationToken);

    public Task<NewsSuggestion?> MarkDuplicateAsync(
        Guid id,
        int duplicateCandidateId,
        string reviewerEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default) =>
        inner.MarkDuplicateAsync(id, duplicateCandidateId, reviewerEmail, reviewNotes, cancellationToken);

    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        inner.GetDashboardCountsAsync(utcNow, cancellationToken);

    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken cancellationToken = default) =>
        inner.GetTopContributorsThisMonthAsync(monthStart, maxCount, cancellationToken);
}
