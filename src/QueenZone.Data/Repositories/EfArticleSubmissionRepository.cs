using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfArticleSubmissionRepository(QueenZoneDbContext dbContext) : IArticleSubmissionRepository
{
    public const int MinBodyVisibleChars = 300;

    private static readonly NewestFirstOrder<ArticleSubmissionEntity> NewestFirst =
        new(a => a.SubmittedAt ?? DateTimeOffset.MinValue, a => a.Id);

    private static readonly Expression<Func<ArticleSubmissionEntity, ArticleSubmissionListItem>> ListItemProjection =
        a => new ArticleSubmissionListItem(
            a.Id,
            a.Title,
            a.Status,
            a.Author != null ? a.Author.DisplayName : "Unknown member",
            a.SubmittedAt,
            a.PublishedAt,
            a.WordCount);

    private static readonly Expression<Func<ArticleSubmissionEntity, ArticleSubmission>> SubmissionProjection =
        a => new ArticleSubmission(
            a.Id,
            a.AuthorMemberId,
            a.Title,
            a.Slug,
            a.Excerpt,
            a.Body,
            a.CoverImageBlobPath,
            a.Tags,
            a.Status,
            a.SubmittedAt,
            a.PublishedAt,
            a.ReviewerEmail,
            a.ReviewNotes,
            a.RejectionReason,
            a.Author != null ? a.Author.DisplayName : null,
            a.Author != null ? a.Author.Email : null);

    public async Task<ArticleSubmission> UpsertDraftAsync(ArticleSubmissionDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        ArticleSubmissionEntity entity;

        if (draft.Id is { } existingId && existingId != Guid.Empty)
        {
            entity = await dbContext.ArticleSubmissions
                .SingleOrDefaultAsync(a => a.Id == existingId && a.AuthorMemberId == draft.AuthorMemberId, ct)
                ?? throw new InvalidOperationException("Article submission not found or does not belong to this member.");

            if (entity.Status != ArticleSubmissionStatus.Draft
                && entity.Status != ArticleSubmissionStatus.RequiresRevision)
            {
                throw new InvalidOperationException("Only draft or requires-revision submissions can be updated.");
            }

            entity.Title = Normalize(draft.Title, 300);
            entity.Slug = GenerateSlug(draft.Title);
            entity.Excerpt = NormalizeOptional(draft.Excerpt, 500);
            entity.Body = draft.Body ?? string.Empty;
            entity.CoverImageBlobPath = NormalizeOptional(draft.CoverImageBlobPath, 512);
            entity.Tags = NormalizeOptional(draft.Tags, 500);
            entity.WordCount = EstimateWordCount(entity.Body);
        }
        else
        {
            entity = new ArticleSubmissionEntity
            {
                Id = Guid.NewGuid(),
                AuthorMemberId = draft.AuthorMemberId,
                Title = Normalize(draft.Title, 300),
                Slug = GenerateSlug(draft.Title),
                Excerpt = NormalizeOptional(draft.Excerpt, 500),
                Body = draft.Body ?? string.Empty,
                CoverImageBlobPath = NormalizeOptional(draft.CoverImageBlobPath, 512),
                Tags = NormalizeOptional(draft.Tags, 500),
                WordCount = EstimateWordCount(draft.Body),
                Status = ArticleSubmissionStatus.Draft,
            };
            dbContext.ArticleSubmissions.Add(entity);
        }

        await dbContext.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<ArticleSubmission?> SubmitForReviewAsync(Guid id, Guid authorMemberId, CancellationToken ct = default)
    {
        var entity = await dbContext.ArticleSubmissions
            .SingleOrDefaultAsync(a => a.Id == id && a.AuthorMemberId == authorMemberId, ct);

        if (entity is null)
        {
            return null;
        }

        if (entity.Status != ArticleSubmissionStatus.Draft
            && entity.Status != ArticleSubmissionStatus.RequiresRevision)
        {
            return null;
        }

        var visibleChars = CountVisibleChars(entity.Body);
        if (visibleChars < MinBodyVisibleChars)
        {
            throw new InvalidOperationException(
                $"Article body must contain at least {MinBodyVisibleChars} characters of visible text (currently {visibleChars}).");
        }

        entity.Status = ArticleSubmissionStatus.Submitted;
        entity.SubmittedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return Map(entity);
    }

    public Task<SubmissionListPage<ArticleSubmission>> GetDraftsForMemberAsync(
        Guid memberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        return SubmittedBy(memberId).ToNewestFirstListPageAsync(
            NewestFirst, SubmissionProjection, skip, take, pageInSql: !dbContext.Database.IsSqliteProvider(), ct);
    }

    public async Task<IReadOnlyList<ArticleSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var (skip, take) = SubmissionPaging.Normalize(page, pageSize);
        return await PendingQueue().ToNewestFirstPageAsync(
            NewestFirst, ListItemProjection, skip, take, pageInSql: !dbContext.Database.IsSqliteProvider(), ct);
    }

    public async Task<ArticleSubmission?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await dbContext.ArticleSubmissions
            .AsNoTracking()
            .Include(a => a.Author)
            .SingleOrDefaultAsync(a => a.Id == id, ct);

        return entity is null ? null : Map(entity);
    }

    public async Task<ArticleSubmission?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? notes,
        string? rejectionReason,
        string? slug = null,
        string? excerpt = null,
        string? tags = null,
        CancellationToken ct = default)
    {
        var entity = await dbContext.ArticleSubmissions
            .SingleOrDefaultAsync(a => a.Id == id, ct);

        if (entity is null)
        {
            return null;
        }

        entity.Status = status;
        entity.ReviewerEmail = NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = NormalizeOptional(notes, 1000);

        if (!string.IsNullOrWhiteSpace(rejectionReason))
        {
            entity.RejectionReason = NormalizeOptional(rejectionReason, 1000);
        }

        if (!string.IsNullOrWhiteSpace(slug))
        {
            entity.Slug = Normalize(slug, 300);
        }

        if (excerpt is not null)
        {
            entity.Excerpt = NormalizeOptional(excerpt, 500);
        }

        if (tags is not null)
        {
            entity.Tags = NormalizeOptional(tags, 500);
        }

        if (status == ArticleSubmissionStatus.Published)
        {
            entity.PublishedAt = DateTimeOffset.UtcNow;
            entity.WordCount = EstimateWordCount(entity.Body);
        }

        await dbContext.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedAsync(CancellationToken ct = default)
    {
        var rows = await dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a => a.Status == ArticleSubmissionStatus.Published && a.PublishedAt != null)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.Slug,
                a.Excerpt,
                a.Body,
                a.CoverImageBlobPath,
                a.Tags,
                a.PublishedAt,
                a.AuthorMemberId,
                a.WordCount,
                DisplayName = a.Author != null ? a.Author.DisplayName : string.Empty,
            })
            .ToListAsync(ct);

        return rows
            .OrderByDescending(a => a.PublishedAt)
            .Select(a => new PublishedArticleSubmission(
                a.Id,
                a.Title,
                a.Slug,
                a.Excerpt,
                a.Body,
                a.CoverImageBlobPath,
                a.Tags,
                a.PublishedAt!.Value,
                string.IsNullOrWhiteSpace(a.DisplayName) ? null : a.DisplayName,
                a.WordCount,
                a.AuthorMemberId))
            .ToList();
    }

    public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
        DateTimeOffset utcNow,
        CancellationToken ct = default) =>
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Select(row => new SubmissionCountRow
            {
                SubmittedAt = row.SubmittedAt,
                IsOpen = row.Status == ArticleSubmissionStatus.Submitted
                    || row.Status == ArticleSubmissionStatus.UnderReview
                    || row.Status == ArticleSubmissionStatus.ApprovedForPublishing,
                IsApproved = row.Status == ArticleSubmissionStatus.Published
                    || row.Status == ArticleSubmissionStatus.ApprovedForPublishing,
                IsRejected = row.Status == ArticleSubmissionStatus.Rejected
                    || row.Status == ArticleSubmissionStatus.RequiresRevision,
                IsStillPending = row.Status == ArticleSubmissionStatus.Submitted
                    || row.Status == ArticleSubmissionStatus.UnderReview,
            })
            .ToDashboardCountsAsync(utcNow, aggregateInSql: !dbContext.Database.IsSqliteProvider(), ct);

    public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
        DateTimeOffset monthStart,
        int maxCount,
        CancellationToken ct = default) =>
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Select(row => new SubmissionContributorRow
            {
                MemberId = row.AuthorMemberId,
                DisplayName = row.Author != null ? row.Author.DisplayName : null,
                SubmittedAt = row.SubmittedAt,
            })
            .ToTopContributorsAsync(monthStart, maxCount, aggregateInSql: !dbContext.Database.IsSqliteProvider(), ct);

    internal static int CountVisibleChars(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return 0;
        }

        // Strip HTML tags; collapse whitespace; count remaining chars.
        var stripped = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(stripped);
        var collapsed = string.Join(" ", decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length;
    }

    internal static int EstimateWordCount(string? body) =>
        string.IsNullOrWhiteSpace(body)
            ? 0
            : System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string GenerateSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        var slug = NewsSlug.Slugify(title.Trim());
        return slug.Length > 300 ? slug[..300] : slug;
    }

    private static string Normalize(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    internal IQueryable<ArticleSubmissionListItem> PendingQueueQuery(int skip, int take) =>
        PendingQueue().NewestFirstPage(NewestFirst, ListItemProjection, skip, take);

    private IQueryable<ArticleSubmissionEntity> PendingQueue() =>
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a =>
                a.Status == ArticleSubmissionStatus.Submitted
                || a.Status == ArticleSubmissionStatus.UnderReview
                || a.Status == ArticleSubmissionStatus.ApprovedForPublishing);

    internal IQueryable<ArticleSubmission> MemberDraftsSqlQuery(int skip, int take, Guid memberId) =>
        SubmittedBy(memberId).NewestFirstPage(NewestFirst, SubmissionProjection, skip, take);

    private IQueryable<ArticleSubmissionEntity> SubmittedBy(Guid memberId) =>
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a => a.AuthorMemberId == memberId);

    private static ArticleSubmission Map(ArticleSubmissionEntity entity) =>
        new(
            entity.Id,
            entity.AuthorMemberId,
            entity.Title,
            entity.Slug,
            entity.Excerpt,
            entity.Body,
            entity.CoverImageBlobPath,
            entity.Tags,
            entity.Status,
            entity.SubmittedAt,
            entity.PublishedAt,
            entity.ReviewerEmail,
            entity.ReviewNotes,
            entity.RejectionReason,
            entity.Author?.DisplayName,
            entity.Author?.Email);
}
