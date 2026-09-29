using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>Plain EF Core writes against the modern <c>SearchDocument</c> table.</summary>
/// <remarks>
/// Article publish (#1211) calls <see cref="UpsertAsync"/> / <see cref="RemoveAsync"/> for one
/// <c>SourceKey</c> after the editorial save has already committed. That is a short
/// single-row UPDATE/INSERT — not <see cref="ReplaceContentTypeAsync"/>, not a user
/// transaction, and not FTS DDL. The SearchDocument full-text index is
/// <c>CHANGE_TRACKING AUTO</c> (background crawl), so these writes do not hold table
/// locks or trigger a synchronous catalog rebuild.
/// </remarks>
public sealed class EfSearchIndexService(QueenZoneDbContext dbContext) : ISearchIndexService
{
    public async Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.SourceKey);

        if (IsExcludedTribute(document.ContentType, document.SourceKey))
        {
            await RemoveAsync(document.SourceKey, cancellationToken);
            return;
        }

        var existing = await dbContext.SearchDocuments
            .SingleOrDefaultAsync(d => d.SourceKey == document.SourceKey, cancellationToken);

        document.IndexedAt = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            document.Id = document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;
            dbContext.SearchDocuments.Add(document);
        }
        else
        {
            existing.ContentType = document.ContentType;
            existing.Title = document.Title;
            existing.Body = document.Body;
            existing.Summary = document.Summary;
            existing.Url = document.Url;
            existing.PublishedAt = document.PublishedAt;
            existing.ImageUrl = document.ImageUrl;
            existing.Category = document.Category;
            existing.AuthorDisplayName = document.AuthorDisplayName;
            existing.IndexedAt = document.IndexedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        await dbContext.SearchDocuments
            .Where(d => d.SourceKey == sourceKey)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ReplaceContentTypeAsync(
        string contentType,
        IReadOnlyList<SearchDocumentEntity> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(documents);

        if (SiteSearchContentType.IsExcludedFromSiteSearch(contentType))
        {
            await dbContext.SearchDocuments
                .Where(d => d.ContentType == contentType)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // Explicit transactions under EnableRetryOnFailure must run inside the execution strategy
        // so Azure SQL transient failures can retry the whole unit of work (see QueenZoneSqlServerOptions).
        // Without this wrapper, ExecuteDelete/SaveChanges under a user-initiated transaction throws
        // InvalidOperationException ("does not support user-initiated transactions") and the admin
        // /admin/search reindex fails immediately — observed in production App Insights 2026-08-04.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Detach any leftover SearchDocument entries so a retry after a failed SaveChanges
            // can re-Add the same document instances without "already tracked" errors.
            foreach (var entry in dbContext.ChangeTracker.Entries<SearchDocumentEntity>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            await dbContext.SearchDocuments
                .Where(d => d.ContentType == contentType)
                .ExecuteDeleteAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var document in documents)
            {
                document.Id = document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;
                document.ContentType = contentType;
                document.IndexedAt = now;
                dbContext.SearchDocuments.Add(document);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SearchDocuments
            .GroupBy(d => d.ContentType)
            .Select(g => new { ContentType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ContentType, g => g.Count, cancellationToken);

    private static bool IsExcludedTribute(string? contentType, string? sourceKey) =>
        SiteSearchContentType.IsExcludedFromSiteSearch(contentType)
        || SearchDocumentSourceKey.IsTribute(sourceKey);
}
