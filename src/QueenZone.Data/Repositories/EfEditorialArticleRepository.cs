using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfEditorialArticleRepository(QueenZoneDbContext dbContext, TimeProvider timeProvider)
    : IEditorialArticleRepository
{
    public async Task<IReadOnlyList<EditorialArticle>> GetAllAsync(CancellationToken ct = default) =>
        (await dbContext.EditorialArticles.AsNoTracking().OrderByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(Map).ToList();

    public async Task<EditorialArticle?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await dbContext.EditorialArticles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)) is { } row ? Map(row) : null;

    public async Task<EditorialArticle?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default) =>
        (await dbContext.EditorialArticles.AsNoTracking().SingleOrDefaultAsync(x => x.LegacyArticleId == null && x.Status != EditorialArticleStatus.Unpublished && x.LiveSlug == slug, ct)) is { } row ? MapLive(row) : null;

    public async Task<IReadOnlyList<EditorialArticle>> GetPublishedStandaloneAsync(CancellationToken ct = default) =>
        (await dbContext.EditorialArticles.AsNoTracking().Where(x => x.LegacyArticleId == null && x.Status != EditorialArticleStatus.Unpublished && x.LiveTitle != null).ToListAsync(ct)).Select(MapLive).ToList();

    public Task<int> GetUnpublishedLegacyOverlayCountAsync(CancellationToken ct = default) =>
        dbContext.EditorialArticles.AsNoTracking().CountAsync(
            x => x.LegacyArticleId != null
                && x.LiveTitle != null
                && x.Status == EditorialArticleStatus.Unpublished,
            ct);

    public async Task<IReadOnlyDictionary<int, EditorialArticle>> GetPublishedLegacyOverlaysAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var values = ids.Distinct().ToArray();
        if (values.Length == 0) return new Dictionary<int, EditorialArticle>();
        return (await dbContext.EditorialArticles.AsNoTracking()
            .Where(x => x.LegacyArticleId != null && x.LiveTitle != null && values.Contains(x.LegacyArticleId.Value))
            .ToListAsync(ct)).ToDictionary(x => x.LegacyArticleId!.Value, MapLive);
    }

    public async Task<EditorialArticle> SaveDraftAsync(EditorialArticleDraft draft, string editor, CancellationToken ct = default)
    {
        var row = draft.Id is Guid id ? await dbContext.EditorialArticles.SingleAsync(x => x.Id == id, ct) : new EditorialArticleEntity { Id = Guid.NewGuid() };
        var resolvedSlug = NewsSlug.Resolve(draft.Title, draft.Slug);
        if (await dbContext.EditorialArticles.AsNoTracking().AnyAsync(x => x.Id != row.Id && (x.Slug == resolvedSlug || x.LiveSlug == resolvedSlug), ct))
            throw new InvalidOperationException("That article slug is already in use.");
        if (await dbContext.ArticleSubmissions.AsNoTracking().AnyAsync(x => x.Id != draft.SourceSubmissionId && x.Status == ArticleSubmissionStatus.Published && x.Slug == resolvedSlug, ct))
            throw new InvalidOperationException("That article slug is already in use.");
        if (draft.Id is null) dbContext.EditorialArticles.Add(row);
        row.LegacyArticleId = draft.LegacyArticleId;
        row.SourceSubmissionId = draft.SourceSubmissionId;
        row.Title = draft.Title.Trim();
        row.Slug = resolvedSlug;
        row.Excerpt = draft.Excerpt.Trim();
        row.Body = draft.Body;
        row.AuthorName = draft.AuthorName.Trim();
        row.Category = draft.Category.Trim();
        row.Tags = string.IsNullOrWhiteSpace(draft.Tags) ? null : draft.Tags.Trim();
        row.Source = string.IsNullOrWhiteSpace(draft.Source) ? null : draft.Source.Trim();
        row.ImageBlobKey = draft.ImageBlobKey;
        row.PublishedAt = draft.PublishedAt;
        // Visibility SoT is Status. Unpublished stays sticky until explicit Publish.
        // Edit-while-live (Status != Unpublished) may return to Draft; Live* is not a flag and is not cleared.
        if (row.Status != EditorialArticleStatus.Unpublished)
        {
            row.Status = EditorialArticleStatus.Draft;
        }
        row.UpdatedAt = timeProvider.GetUtcNow();
        row.UpdatedBy = editor;
        await dbContext.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<EditorialArticle?> SetStatusAsync(Guid id, string status, string editor, CancellationToken ct = default)
    {
        var row = await dbContext.EditorialArticles.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return null;
        row.Status = status;
        if (status == EditorialArticleStatus.Published)
        {
            row.LiveTitle = row.Title; row.LiveSlug = row.Slug; row.LiveExcerpt = row.Excerpt;
            row.LiveBody = row.Body; row.LiveAuthorName = row.AuthorName; row.LiveCategory = row.Category;
            row.LiveTags = row.Tags; row.LiveSource = row.Source; row.LiveImageBlobKey = row.ImageBlobKey; row.LivePublishedAt = row.PublishedAt;
            row.LiveWordCount = EfArticleSubmissionRepository.EstimateWordCount(row.Body);
        }
        row.UpdatedAt = timeProvider.GetUtcNow();
        row.UpdatedBy = editor;
        await dbContext.SaveChangesAsync(ct);
        return Map(row);
    }

    private static EditorialArticle Map(EditorialArticleEntity x) => new(x.Id, x.LegacyArticleId, x.SourceSubmissionId, x.Title, x.Slug, x.Excerpt, x.Body, x.AuthorName, x.Category, x.Tags, x.Source, x.ImageBlobKey, x.Status, x.PublishedAt, x.UpdatedAt, x.UpdatedBy, x.LiveImageBlobKey, x.LiveTitle is not null);
    private static EditorialArticle MapLive(EditorialArticleEntity x) => new(x.Id, x.LegacyArticleId, x.SourceSubmissionId, x.LiveTitle!, x.LiveSlug!, x.LiveExcerpt!, x.LiveBody!, x.LiveAuthorName!, x.LiveCategory!, x.LiveTags, x.LiveSource, x.LiveImageBlobKey, x.Status, x.LivePublishedAt!.Value, x.UpdatedAt, x.UpdatedBy, x.LiveImageBlobKey, true, x.LiveWordCount);
}
