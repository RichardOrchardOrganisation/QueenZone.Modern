namespace QueenZone.Data;

public sealed class InMemoryNewsAgentGuidanceRepository(SharedNewsAgentGuidanceStore store)
    : INewsAgentGuidanceRepository
{
    public Task<NewsAgentGuidanceRevision?> GetPublishedAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Map(store.GetPublished(type)));

    public Task<NewsAgentGuidanceRevision?> GetDraftAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Map(store.GetDraft(type)));

    public Task<NewsAgentGuidanceRevision?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Map(store.GetById(id)));

    public Task<IReadOnlyList<NewsAgentGuidanceRevision>> ListHistoryAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsAgentGuidanceRevision>>(
            store.ListHistory(type).Select(item => Map(item)!).ToList());

    public Task<NewsAgentGuidanceRevision> SaveDraftAsync(
        NewsAgentGuidanceType type,
        string content,
        string editorEmail,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        var sanitized = NewsAgentGuidanceText.Sanitize(content);

        var existing = store.GetDraft(type);
        var rowVersion = existing is null ? null : expectedRowVersion ?? existing.RowVersion;
        var saved = store.SaveDraft(
            type,
            sanitized,
            NewsAgentGuidanceText.ComputeContentHash(sanitized),
            NormalizeEmail(editorEmail),
            rowVersion);
        return Task.FromResult(Map(saved)!);
    }

    public Task<NewsAgentGuidanceRevision> PublishDraftAsync(
        NewsAgentGuidanceType type,
        string publisherEmail,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Map(store.PublishDraft(type, NormalizeEmail(publisherEmail), expectedRowVersion))!);

    public Task<NewsAgentGuidanceRevision> RollbackAsync(
        NewsAgentGuidanceType type,
        int sourceRevisionId,
        string publisherEmail,
        CancellationToken cancellationToken = default)
    {
        var source = store.GetById(sourceRevisionId)
            ?? throw new InvalidOperationException($"Guidance revision {sourceRevisionId} was not found.");
        if (source.Type != type)
        {
            throw new InvalidOperationException("The selected revision does not match the guidance type.");
        }

        var published = store.PublishNewRevision(
            type,
            source.Content,
            source.ContentHash,
            NormalizeEmail(publisherEmail));
        return Task.FromResult(Map(published)!);
    }

    public Task<NewsAgentGuidanceRevision> RestoreCompiledDefaultAsync(
        NewsAgentGuidanceType type,
        string publisherEmail,
        CancellationToken cancellationToken = default)
    {
        var content = string.Empty;
        var published = store.PublishNewRevision(
            type,
            content,
            NewsAgentGuidanceText.ComputeContentHash(content),
            NormalizeEmail(publisherEmail));
        return Task.FromResult(Map(published)!);
    }

    private static string NormalizeEmail(string email)
    {
        var trimmed = email.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new NewsAgentGuidanceValidationException("Editor email is required.");
        }

        return trimmed;
    }

    private static NewsAgentGuidanceRevision? Map(Entities.NewsAgentGuidanceRevisionEntity? entity) =>
        entity is null
            ? null
            : new NewsAgentGuidanceRevision(
                entity.Id,
                entity.Type,
                entity.RevisionNumber,
                entity.Content,
                entity.ContentHash,
                entity.Status,
                entity.CreatedAt,
                entity.CreatedByEmail,
                entity.PublishedAt,
                entity.PublishedByEmail,
                entity.RowVersion);
}
