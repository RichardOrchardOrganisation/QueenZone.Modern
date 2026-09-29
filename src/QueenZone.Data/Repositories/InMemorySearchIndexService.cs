using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemorySearchIndexService(SharedSearchIndexStore store) : ISearchIndexService
{
    public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.SourceKey);
        if (IsExcludedTribute(document.ContentType, document.SourceKey))
        {
            store.Remove(document.SourceKey);
            return Task.CompletedTask;
        }

        store.Upsert(document);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        store.Remove(sourceKey);
        return Task.CompletedTask;
    }

    public Task ReplaceContentTypeAsync(
        string contentType,
        IReadOnlyList<SearchDocumentEntity> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(documents);
        if (SiteSearchContentType.IsExcludedFromSiteSearch(contentType))
        {
            store.ReplaceContentType(contentType, []);
            return Task.CompletedTask;
        }

        store.ReplaceContentType(contentType, documents);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, int> counts = store.GetAll()
            .GroupBy(d => d.ContentType)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(counts);
    }

    private static bool IsExcludedTribute(string? contentType, string? sourceKey) =>
        SiteSearchContentType.IsExcludedFromSiteSearch(contentType)
        || SearchDocumentSourceKey.IsTribute(sourceKey);
}
