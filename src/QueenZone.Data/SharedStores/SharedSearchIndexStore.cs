using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>In-memory equivalent of the <c>SearchDocument</c> table, keyed by <c>SourceKey</c>.</summary>
public sealed class SharedSearchIndexStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, SearchDocumentEntity> documentsBySourceKey = [];

    public void Upsert(SearchDocumentEntity document)
    {
        lock (sync)
        {
            document.IndexedAt = DateTimeOffset.UtcNow;
            documentsBySourceKey[document.SourceKey] = document;
        }
    }

    public void Remove(string sourceKey)
    {
        lock (sync)
        {
            documentsBySourceKey.Remove(sourceKey);
        }
    }

    public void ReplaceContentType(string contentType, IReadOnlyList<SearchDocumentEntity> documents)
    {
        lock (sync)
        {
            foreach (var key in documentsBySourceKey
                         .Where(pair => pair.Value.ContentType == contentType)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                documentsBySourceKey.Remove(key);
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var document in documents)
            {
                document.ContentType = contentType;
                document.IndexedAt = now;
                documentsBySourceKey[document.SourceKey] = document;
            }
        }
    }

    public IReadOnlyList<SearchDocumentEntity> GetAll()
    {
        lock (sync)
        {
            return documentsBySourceKey.Values.ToList();
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            documentsBySourceKey.Clear();
        }
    }
}
