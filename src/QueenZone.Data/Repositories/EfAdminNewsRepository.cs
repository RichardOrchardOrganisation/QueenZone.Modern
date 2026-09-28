using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfAdminNewsRepository : IAdminNewsRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly string latestNewsSql;
    private readonly string latestNewsCountSql;
    private readonly string connectionString;

    public EfAdminNewsRepository(QueenZoneDbContext dbContext)
        : this(dbContext, latestNewsSqlOverride: null)
    {
    }

    internal EfAdminNewsRepository(QueenZoneDbContext dbContext, string? latestNewsSqlOverride)
    {
        this.dbContext = dbContext;
        connectionString = dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("QueenZone legacy database connection string is not configured.");
        if (latestNewsSqlOverride is not null)
        {
            latestNewsSql = latestNewsSqlOverride;
            latestNewsCountSql = "SELECT COUNT(*) AS [Value] FROM NEWS_T";
        }
        else
        {
            var columns = LegacyNewsSchema.GetNewsColumnAvailability(connectionString);
            latestNewsSql = PublishedNewsQuery.BuildAdminLatestNewsSql(columns);
            latestNewsCountSql = PublishedNewsQuery.BuildAdminLatestNewsCountSql(columns);
        }
    }

    public async Task<IReadOnlyList<AdminNewsArticle>> GetAllAsync(CancellationToken cancellationToken = default)
    {
#pragma warning disable EF1003 // SQL is generated from fixed schema-detection branches, not user input.
        var rows = await dbContext.NewsRows
            .FromSqlRaw(latestNewsSql + " ORDER BY PublishedAt DESC, NewsId DESC")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
#pragma warning restore EF1003

        return rows.Select(NewsTableRowMapper.ToAdminArticle).ToList();
    }

    public async Task<AdminNewsArticlePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Max(pageSize, 1);
        var offset = (normalizedPage - 1) * normalizedPageSize;

        var totalCount = await GetAdminNewsTotalCountAsync(cancellationToken);

#pragma warning disable EF1003 // SQL is generated from fixed schema-detection branches, not user input.
        var rows = await dbContext.NewsRows
            .FromSqlRaw(
                latestNewsSql + " ORDER BY PublishedAt DESC, NewsId DESC OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY",
                offset,
                normalizedPageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
#pragma warning restore EF1003

        return new AdminNewsArticlePage(
            rows.Select(NewsTableRowMapper.ToAdminArticle).ToList(),
            totalCount,
            normalizedPage,
            normalizedPageSize);
    }

    public async Task<AdminNewsArticle?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
#pragma warning disable EF1003 // SQL is generated from fixed schema-detection branches, not user input.
        // Materialize on the client: EF cannot compose SingleOrDefault over this CTE-based SQL.
        var rows = await dbContext.NewsRows
            .FromSqlRaw(latestNewsSql + " AND NEWS_ID = {0}", id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
#pragma warning restore EF1003

        var row = rows.FirstOrDefault();
        return row is null ? null : NewsTableRowMapper.ToAdminArticle(row);
    }

    public async Task<int> CreateDraftAsync(AdminNewsDraft draft, string editorEmail, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow;

        var row = new NewsTableRow
        {
            Title = draft.Title,
            Excerpt = draft.Excerpt,
            Body = draft.Body,
            PublishedAt = draft.PublishedAt,
            SourceUrl = draft.SourceUrl,
            IsPublished = false,
            Slug = NewsSlug.Resolve(draft.Title, draft.Slug),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            EditorEmail = editorEmail,
            Type = 0,
            QueenOnline = 0,
            ImageBlobKey = draft.ImageBlobKey,
            ImageGalleryPicId = draft.ImageGalleryPicId
        };

        dbContext.NewsRows.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.NewsId;
    }

    public async Task UpdateAsync(
        int id,
        AdminNewsDraft draft,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        var query = ConcurrencyQuery(id, expectedUpdatedAt);
        var updated = await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.Title, draft.Title)
                .SetProperty(row => row.Excerpt, draft.Excerpt)
                .SetProperty(row => row.Body, draft.Body)
                .SetProperty(row => row.PublishedAt, draft.PublishedAt)
                .SetProperty(row => row.SourceUrl, draft.SourceUrl)
                .SetProperty(row => row.Slug, NewsSlug.Resolve(draft.Title, draft.Slug))
                .SetProperty(row => row.UpdatedAt, DateTime.UtcNow)
                .SetProperty(row => row.EditorEmail, editorEmail)
                .SetProperty(row => row.ImageBlobKey, draft.ImageBlobKey)
                .SetProperty(row => row.ImageGalleryPicId, draft.ImageGalleryPicId),
            cancellationToken);

        await EnsureNewsWriteAsync(id, updated, cancellationToken);
    }

    public async Task PublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow;
        var publishedAt = timestamp.Date;

        var updated = await ConcurrencyQuery(id, expectedUpdatedAt).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.IsPublished, true)
                .SetProperty(row => row.PublishedAt, publishedAt)
                .SetProperty(row => row.UpdatedAt, timestamp)
                .SetProperty(row => row.EditorEmail, editorEmail),
            cancellationToken);

        await EnsureNewsWriteAsync(id, updated, cancellationToken);
    }

    public async Task UnpublishAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        var updated = await ConcurrencyQuery(id, expectedUpdatedAt).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.IsPublished, false)
                .SetProperty(row => row.UpdatedAt, DateTime.UtcNow)
                .SetProperty(row => row.EditorEmail, editorEmail),
            cancellationToken);

        await EnsureNewsWriteAsync(id, updated, cancellationToken);
    }

    public async Task DeleteAsync(
        int id,
        string editorEmail,
        DateTime? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        var deleted = await ConcurrencyQuery(id, expectedUpdatedAt).ExecuteDeleteAsync(cancellationToken);
        await EnsureNewsWriteAsync(id, deleted, cancellationToken);
    }

    private IQueryable<NewsTableRow> ConcurrencyQuery(int id, DateTime? expectedUpdatedAt)
    {
        var query = dbContext.NewsRows.Where(row => row.NewsId == id);
        if (expectedUpdatedAt is DateTime expected)
        {
            query = query.Where(row => row.UpdatedAt == expected);
        }

        return query;
    }

    private async Task EnsureNewsWriteAsync(int id, int affected, CancellationToken cancellationToken)
    {
        if (affected > 0)
        {
            return;
        }

        var exists = await dbContext.NewsRows.AnyAsync(row => row.NewsId == id, cancellationToken);
        QueenZoneConcurrency.EnsureUpdated(affected, exists, $"News article {id} was not found.");
    }

    public async Task<bool> IsSlugInUseAsync(string slug, int? excludeNewsId = null, CancellationToken cancellationToken = default)
    {
        var normalized = NewsSlug.Slugify(slug);
        var rows = dbContext.NewsRows.AsNoTracking();
        if (excludeNewsId is int excludeId)
        {
            rows = rows.Where(row => row.NewsId != excludeId);
        }

        // Parameterized existence check against stored SLUG — never loads ARTICLE bodies.
        var slugExists = await rows
            .Where(row => row.Slug != null && row.Slug.Trim() != string.Empty)
            .AnyAsync(row => row.Slug!.ToLower() == normalized, cancellationToken);
        if (slugExists)
        {
            return true;
        }

        // Legacy rows without a stored slug still resolve from TITLE in the app.
        var sluglessTitles = await rows
            .Where(row => row.Slug == null || row.Slug.Trim() == string.Empty)
            .Select(row => row.Title)
            .ToListAsync(cancellationToken);

        return sluglessTitles.Any(title =>
            string.Equals(NewsSlug.Slugify(title), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> TrySetForumTopicIdAsync(
        int newsId,
        int topicId,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.NewsRows
            .Where(row => row.NewsId == newsId && row.ForumTopicId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ForumTopicId, topicId),
                cancellationToken);
        return updated > 0;
    }

    private async Task<int> GetAdminNewsTotalCountAsync(CancellationToken cancellationToken)
    {
        var values = await dbContext.Database
            .SqlQueryRaw<int>(latestNewsCountSql)
            .ToListAsync(cancellationToken);
        return values.FirstOrDefault();
    }

}
