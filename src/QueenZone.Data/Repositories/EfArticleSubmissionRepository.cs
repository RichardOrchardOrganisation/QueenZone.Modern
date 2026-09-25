using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfArticleSubmissionRepository(QueenZoneDbContext dbContext) : IArticleSubmissionRepository
{
    public const int MinBodyVisibleChars = 300;

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

    public async Task<SubmissionListPage<ArticleSubmission>> GetDraftsForMemberAsync(
        Guid memberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a => a.AuthorMemberId == memberId);

        var totalCount = await query.CountAsync(ct);
        var skip = (page - 1) * pageSize;

        if (dbContext.Database.IsSqliteProvider())
        {
            var sqliteRows = await query
                .Select(a => new
                {
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
                    DisplayName = a.Author != null ? a.Author.DisplayName : null,
                    Email = a.Author != null ? a.Author.Email : null,
                })
                .ToListAsync(ct);
            var sqliteItems = sqliteRows
                .OrderByDescending(a => a.SubmittedAt ?? DateTimeOffset.MinValue)
                .ThenBy(a => a.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(a => new ArticleSubmission(
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
                    a.DisplayName,
                    a.Email))
                .ToList();
            return new SubmissionListPage<ArticleSubmission>(sqliteItems, totalCount);
        }

        var items = await MemberDraftsSqlQuery(skip, pageSize, memberId).ToListAsync(ct);
        return new SubmissionListPage<ArticleSubmission>(items, totalCount);
    }

    public async Task<IReadOnlyList<ArticleSubmissionListItem>> GetPendingAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var skip = (page - 1) * pageSize;
        var filtered = dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a =>
                a.Status == ArticleSubmissionStatus.Submitted
                || a.Status == ArticleSubmissionStatus.UnderReview
                || a.Status == ArticleSubmissionStatus.ApprovedForPublishing);

        if (dbContext.Database.IsSqliteProvider())
        {
            var sqliteRows = await filtered
                .Select(a => new
                {
                    a.Id,
                    a.Title,
                    a.Status,
                    a.SubmittedAt,
                    a.PublishedAt,
                    a.WordCount,
                    DisplayName = a.Author != null ? a.Author.DisplayName : string.Empty,
                })
                .ToListAsync(ct);

            return sqliteRows
                .OrderByDescending(a => a.SubmittedAt ?? DateTimeOffset.MinValue)
                .ThenBy(a => a.Id)
                .Skip(skip)
                .Take(pageSize)
                .Select(a => new ArticleSubmissionListItem(
                    a.Id,
                    a.Title,
                    a.Status,
                    string.IsNullOrWhiteSpace(a.DisplayName) ? "Unknown member" : a.DisplayName,
                    a.SubmittedAt,
                    a.PublishedAt,
                    a.WordCount))
                .ToList();
        }

        return await PendingQueueQuery(skip, pageSize).ToListAsync(ct);
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
        var stripped = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ", System.Text.RegularExpressions.RegexOptions.None, RegexDefaults.MatchTimeout);
        var decoded = System.Net.WebUtility.HtmlDecode(stripped);
        var collapsed = string.Join(" ", decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length;
    }

    internal static int EstimateWordCount(string? body) =>
        string.IsNullOrWhiteSpace(body)
            ? 0
            : System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ", System.Text.RegularExpressions.RegexOptions.None, RegexDefaults.MatchTimeout)
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
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a =>
                a.Status == ArticleSubmissionStatus.Submitted
                || a.Status == ArticleSubmissionStatus.UnderReview
                || a.Status == ArticleSubmissionStatus.ApprovedForPublishing)
            .OrderByDescending(a => a.SubmittedAt ?? DateTimeOffset.MinValue)
            .ThenBy(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new ArticleSubmissionListItem(
                a.Id,
                a.Title,
                a.Status,
                a.Author != null ? a.Author.DisplayName : "Unknown member",
                a.SubmittedAt,
                a.PublishedAt,
                a.WordCount));

    internal IQueryable<ArticleSubmission> MemberDraftsSqlQuery(int skip, int take, Guid memberId) =>
        dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(a => a.AuthorMemberId == memberId)
            .OrderByDescending(a => a.SubmittedAt ?? DateTimeOffset.MinValue)
            .ThenBy(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new ArticleSubmission(
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
                a.Author != null ? a.Author.Email : null));

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
