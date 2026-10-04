using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Keeps <c>SearchDocument</c> in sync with the visible/published content it's indexed from.
/// Implementations must never write a document for content that isn't currently visible —
/// <see cref="Entities.SearchDocumentEntity"/> has no visibility column, so an upsert here is a
/// direct claim that the row is safe to surface to anonymous search.
/// </summary>
public interface ISearchIndexService
{
    Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces every indexed document for <paramref name="contentType"/> with
    /// <paramref name="documents"/>. Used by the batch reindex builder so a partial run can't
    /// leave stale rows mixed in with fresh ones.
    /// </summary>
    Task ReplaceContentTypeAsync(
        string contentType,
        IReadOnlyList<SearchDocumentEntity> documents,
        CancellationToken cancellationToken = default);

    /// <summary>Total indexed document count, grouped by content type. Used by the admin reindex page.</summary>
    Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents whose title equals <paramref name="title"/> (ordinal ignore-case). Used for
    /// song-page related content. Default is empty so test doubles stay source-compatible.
    /// </summary>
    Task<IReadOnlyList<SearchDocumentEntity>> FindByExactTitleAsync(
        string title,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchDocumentEntity>>([]);
}
