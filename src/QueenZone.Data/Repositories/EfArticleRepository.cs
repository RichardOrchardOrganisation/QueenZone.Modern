using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfArticleRepository(QueenZoneDbContext dbContext, IEditorialArticleRepository? editorialArticles = null) : IArticleRepository
{
    public Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default) =>
        WithTag(PublishedListQuery(), tag).CountAsync(ct);

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
        int page, int pageSize, string? tag = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = WithTag(PublishedListQuery(), tag);

        List<PublishedArticleListRow> rows;
        if (IsSqliteDatabase())
        {
            rows = (await query.ToListAsync(ct))
                .OrderByDescending(a => a.PublishedAt)
                .ThenBy(a => a.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            rows = await query
                .OrderByDescending(a => a.PublishedAt)
                .ThenBy(a => a.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        return rows.Select(MapList).ToList();
    }

    public async Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var editorial = editorialArticles is null ? null : await editorialArticles.GetPublishedBySlugAsync(slug, ct);
        if (editorial is not null)
        {
            return MapEditorial(editorial);
        }

        return await SelectDetailProjection(PublishedSubmissions().Where(x => x.Slug == slug))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
        DateTimeOffset publishedAt, CancellationToken ct = default)
    {
        PublishedArticleListRow? previous;
        PublishedArticleListRow? next;
        if (IsSqliteDatabase())
        {
            previous = (await PublishedListQuery().ToListAsync(ct))
                .Where(a => a.PublishedAt < publishedAt)
                .OrderByDescending(a => a.PublishedAt)
                .ThenBy(a => a.Id)
                .FirstOrDefault();
            next = (await PublishedListQuery().ToListAsync(ct))
                .Where(a => a.PublishedAt > publishedAt)
                .OrderBy(a => a.PublishedAt)
                .ThenBy(a => a.Id)
                .FirstOrDefault();
        }
        else
        {
            previous = await PublishedListQuery()
                .Where(a => a.PublishedAt < publishedAt)
                .OrderByDescending(a => a.PublishedAt)
                .ThenBy(a => a.Id)
                .FirstOrDefaultAsync(ct);
            next = await PublishedListQuery()
                .Where(a => a.PublishedAt > publishedAt)
                .OrderBy(a => a.PublishedAt)
                .ThenBy(a => a.Id)
                .FirstOrDefaultAsync(ct);
        }

        return (previous is null ? null : MapList(previous), next is null ? null : MapList(next));
    }

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default)
    {
        var query = PublishedListQuery();
        var rows = IsSqliteDatabase()
            ? (await query.ToListAsync(ct)).OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id).ToList()
            : await query.OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id).ToListAsync(ct);
        return rows.Select(MapList).ToList();
    }

    internal IQueryable<PublishedArticleListRow> PublishedListQuery()
    {
        var submissions = PublishedSubmissions().Select(a => new PublishedArticleListRow
        {
            Id = a.Id,
            Title = a.Title,
            Slug = a.Slug,
            Excerpt = a.Excerpt,
            CoverImageBlobPath = a.CoverImageBlobPath,
            Tags = a.Tags,
            PublishedAt = a.PublishedAt!.Value,
            AuthorDisplayName = a.Author != null ? a.Author.DisplayName : null,
            WordCount = a.WordCount,
            Category = (string?)null,
            Source = (string?)null,
        });

        var editorials = dbContext.EditorialArticles
            .AsNoTracking()
            .Where(a => a.LegacyArticleId == null
                && a.Status != EditorialArticleStatus.Unpublished
                && a.LiveTitle != null
                && a.LivePublishedAt != null)
            .Select(a => new PublishedArticleListRow
            {
                Id = a.Id,
                Title = a.LiveTitle!,
                Slug = a.LiveSlug!,
                Excerpt = a.LiveExcerpt,
                CoverImageBlobPath = a.LiveImageBlobKey,
                Tags = a.LiveTags,
                PublishedAt = a.LivePublishedAt!.Value,
                AuthorDisplayName = a.LiveAuthorName,
                WordCount = a.LiveWordCount,
                Category = a.LiveCategory,
                Source = a.LiveSource,
            });

        return submissions.Concat(editorials);
    }

    private IQueryable<ArticleSubmissionEntity> PublishedSubmissions() =>
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a => a.Status == ArticleSubmissionStatus.Published && a.PublishedAt != null
                && !dbContext.EditorialArticles.Any(e => e.SourceSubmissionId == a.Id
                    && e.LiveTitle != null && e.Status != EditorialArticleStatus.Unpublished));

    private static IQueryable<PublishedArticleSubmission> SelectDetailProjection(
        IQueryable<ArticleSubmissionEntity> query) =>
        query.Select(a => new PublishedArticleSubmission(
            a.Id,
            a.Title,
            a.Slug,
            a.Excerpt,
            a.Body,
            a.CoverImageBlobPath,
            a.Tags,
            a.PublishedAt!.Value,
            a.Author != null ? a.Author.DisplayName : null,
            a.WordCount));

    private static IQueryable<PublishedArticleListRow> WithTag(
        IQueryable<PublishedArticleListRow> query,
        string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return query;
        }

        var pattern = "%," + tag.Trim() + ",%";
        return query.Where(a => a.Tags != null && EF.Functions.Like("," + a.Tags + ",", pattern));
    }

    private static PublishedArticleSubmission MapList(PublishedArticleListRow x) => new(
        x.Id,
        x.Title,
        x.Slug,
        x.Excerpt,
        string.Empty,
        x.CoverImageBlobPath,
        x.Tags,
        x.PublishedAt,
        string.IsNullOrWhiteSpace(x.AuthorDisplayName) ? null : x.AuthorDisplayName,
        x.WordCount,
        Category: x.Category,
        Source: x.Source);

    private static PublishedArticleSubmission MapEditorial(EditorialArticle x) => new(
        x.Id,
        x.Title,
        x.Slug,
        x.Excerpt,
        x.Body,
        x.ImageBlobKey,
        x.Tags,
        x.PublishedAt,
        x.AuthorName,
        x.WordCount,
        Category: x.Category,
        Source: x.Source);

    private bool IsSqliteDatabase() =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);
}

internal sealed class PublishedArticleListRow
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Excerpt { get; init; }

    public string? CoverImageBlobPath { get; init; }

    public string? Tags { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public string? AuthorDisplayName { get; init; }

    public int WordCount { get; init; }

    public string? Category { get; init; }

    public string? Source { get; init; }
}
