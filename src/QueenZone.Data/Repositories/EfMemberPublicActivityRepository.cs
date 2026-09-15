using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Reads a member (or followed-member) public contribution feed across four sources.
/// </summary>
/// <remarks>
/// The three submission sources store <c>datetimeoffset</c> and share one UNION ALL query.
/// Forum posts store <c>datetime2</c> (<c>ModernForumPost.PostedAt</c>), and EF Core cannot
/// translate a <c>DateTime</c> to <c>DateTimeOffset</c> conversion inside a set operation, so
/// they stay a second query merged in memory. Folding all four into a single UNION ALL needs a
/// value converter on <c>PostedAt</c> — deliberately out of scope here because that column also
/// carries the perf-critical forum read path.
/// A profile with a linked legacy account adds a third source: archive posts keyed by
/// <c>AuthorLegacyUserId</c>, counted from the precomputed archive-author summary rather than
/// aggregated per request.
/// </remarks>
public sealed class EfMemberPublicActivityRepository(
    QueenZoneDbContext dbContext,
    IForumArchiveAuthorRepository archiveAuthorRepository)
    : IMemberPublicActivityRepository
{
    public Task<MemberPublicActivityPage> GetPageAsync(
        Guid memberId,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetPageCoreAsync([memberId], linkedLegacyUserId, page, pageSize, cancellationToken);

    public Task<MemberPublicActivityPage> GetFeedPageAsync(
        IReadOnlyCollection<Guid> memberIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetPageCoreAsync(memberIds, linkedLegacyUserId: null, page, pageSize, cancellationToken);

    private async Task<MemberPublicActivityPage> GetPageCoreAsync(
        IReadOnlyCollection<Guid> memberIds,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var authorIds = memberIds.Distinct().ToList();
        if (authorIds.Count == 0)
        {
            return new MemberPublicActivityPage([], 0, page, pageSize);
        }

        var take = (int)Math.Min((long)page * pageSize, int.MaxValue);
        var forumPosts = BuildForumQuery(authorIds);
        var submissionsQuery = BuildSubmissionsQuery(authorIds);

        // Five round trips (two counts, two pages, names) rather than the previous nine. Both
        // sources are ordered and truncated in the database — the SQLite branch this replaced
        // fetched every matching row with no Take at all.
        var totalCount = await forumPosts.CountAsync(cancellationToken)
            + await submissionsQuery.CountAsync(cancellationToken);

        var legacyRows = linkedLegacyUserId is int legacyUserId
            ? await LoadLinkedLegacyRowsAsync(legacyUserId, authorIds[0], take, cancellationToken)
            : null;
        totalCount += legacyRows?.TotalCount ?? 0;
        if (totalCount == 0)
        {
            return new MemberPublicActivityPage([], 0, page, pageSize);
        }

        // Ordered on the entity before projecting, so the sort and Take stay server-side.
        var forumRows = (await forumPosts
                .OrderByDescending(post => post.PostedAt)
                .Take(take)
                .Select(post => new
                {
                    post.LegacyPostId,
                    post.LegacyThreadTopicId,
                    ThreadTitle = post.Thread!.Title,
                    post.BodyHtml,
                    post.PostedAt,
                    AuthorId = post.AuthorMemberId!.Value,
                    post.AuthorDisplayName,
                })
                .ToListAsync(cancellationToken))
            .Select(row => new FeedRow
            {
                Type = MemberPublicActivityType.ForumPost,
                Title = row.ThreadTitle,
                Summary = row.BodyHtml,
                PublishedAt = ToOffset(row.PostedAt),
                ContentId = row.LegacyPostId,
                ParentId = row.LegacyThreadTopicId,
                AuthorId = row.AuthorId,
                AuthorDisplayName = row.AuthorDisplayName,
            })
            .ToList();

        // SQLite cannot ORDER BY a DateTimeOffset, so the test provider sorts and truncates the
        // union client-side. SQL Server — the production provider — does both in the database.
        var submissionRows = IsSqliteDatabase()
            ? (await submissionsQuery.ToListAsync(cancellationToken))
                .OrderByDescending(row => row.PublishedAt)
                .Take(take)
                .ToList()
            : await submissionsQuery
                .OrderByDescending(row => row.PublishedAt)
                .Take(take)
                .ToListAsync(cancellationToken);

        // Type and Title break ties so paging stays stable across requests. The previous sort
        // ordered on PublishedAt alone, leaving same-timestamp rows free to swap between pages.
        var pageRows = forumRows
            .Concat(submissionRows)
            .Concat(legacyRows?.Rows ?? [])
            .OrderByDescending(row => row.PublishedAt)
            .ThenByDescending(row => row.Type, StringComparer.Ordinal)
            .ThenBy(row => row.Title, StringComparer.Ordinal)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        await LoadLegacyBodiesAsync(pageRows, cancellationToken);
        var names = await LoadAuthorNamesAsync(pageRows, cancellationToken);

        var items = pageRows
            .Select(row => new MemberPublicActivityItem(
                row.Type,
                BuildTitle(row),
                row.Summary,
                row.PublishedAt,
                ContentId: row.ContentId,
                ParentId: row.ParentId,
                Slug: BuildSlug(row),
                Category: row.Category,
                AuthorId: row.AuthorId,
                AuthorDisplayName: names.TryGetValue(row.AuthorId, out var displayName)
                    ? displayName
                    : row.AuthorDisplayName))
            .ToList();

        return new MemberPublicActivityPage(items, totalCount, page, pageSize);
    }

    private bool IsSqliteDatabase() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private IQueryable<ModernForumPostEntity> BuildForumQuery(IReadOnlyList<Guid> authorIds) =>
        dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.AuthorMemberId != null
                && authorIds.Contains(post.AuthorMemberId.Value)
                && post.Thread != null
                && !post.IsHidden);

    /// <summary>
    /// Newest <paramref name="take"/> visible archive posts for a linked legacy account, without
    /// bodies: a prolific legacy author can have thousands of posts, so deep profile pages merge
    /// on lightweight rows and only the posts that land on the page load <c>BodyHtml</c>.
    /// Served by <c>IX_ModernForumPost_AuthorLegacyUserId_PostedAt</c>.
    /// </summary>
    private async Task<(int TotalCount, List<FeedRow> Rows)?> LoadLinkedLegacyRowsAsync(
        int legacyUserId,
        Guid memberId,
        int take,
        CancellationToken cancellationToken)
    {
        var summary = await archiveAuthorRepository.GetSummaryAsync(legacyUserId, cancellationToken);
        if (summary is null || summary.PostCount <= 0)
        {
            return null;
        }

        var rows = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.AuthorLegacyUserId == legacyUserId
                && post.Thread != null
                && !post.IsHidden)
            .OrderByDescending(post => post.PostedAt)
            .ThenByDescending(post => post.Id)
            .Take(take)
            .Select(post => new
            {
                post.Id,
                post.LegacyPostId,
                post.LegacyThreadTopicId,
                ThreadTitle = post.Thread!.Title,
                post.PostedAt,
                post.AuthorDisplayName,
            })
            .ToListAsync(cancellationToken);

        return (summary.PostCount, rows
            .Select(row => new FeedRow
            {
                Type = MemberPublicActivityType.ForumPost,
                Title = row.ThreadTitle,
                PublishedAt = ToOffset(row.PostedAt),
                ContentId = row.LegacyPostId,
                ParentId = row.LegacyThreadTopicId,
                AuthorId = memberId,
                AuthorDisplayName = row.AuthorDisplayName,
                LegacyForumPostRowId = row.Id,
            })
            .ToList());
    }

    private async Task LoadLegacyBodiesAsync(IReadOnlyList<FeedRow> rows, CancellationToken cancellationToken)
    {
        var legacyRows = rows.Where(row => row.LegacyForumPostRowId is not null).ToList();
        if (legacyRows.Count == 0)
        {
            return;
        }

        var ids = legacyRows.Select(row => row.LegacyForumPostRowId!.Value).ToList();
        var bodies = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => ids.Contains(post.Id))
            .Select(post => new { post.Id, post.BodyHtml })
            .ToDictionaryAsync(post => post.Id, post => post.BodyHtml, cancellationToken);
        foreach (var row in legacyRows)
        {
            row.Summary = bodies.GetValueOrDefault(row.LegacyForumPostRowId!.Value);
        }
    }

    /// <summary>
    /// <c>PostedAt</c> is stored without an offset, so it is pinned to UTC explicitly rather than
    /// via the implicit <see cref="DateTimeOffset"/> conversion, which would read an Unspecified
    /// kind as local time and shift the timestamp.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime? value) =>
        new(DateTime.SpecifyKind(value ?? DateTime.MinValue, DateTimeKind.Utc));

    /// <summary>
    /// Articles, promoted news and approved photos all store <c>datetimeoffset</c>, so EF Core
    /// translates this Concat chain to a single UNION ALL that both SQL Server and the SQLite
    /// test provider order and truncate server-side.
    /// </summary>
    private IQueryable<FeedRow> BuildSubmissionsQuery(IReadOnlyList<Guid> authorIds)
    {
        var articles = dbContext.ArticleSubmissions
            .AsNoTracking()
            .Where(article => authorIds.Contains(article.AuthorMemberId)
                && article.Status == ArticleSubmissionStatus.Published
                && article.PublishedAt != null)
            .Select(article => new FeedRow
            {
                Type = MemberPublicActivityType.Article,
                Title = article.Title,
                Summary = article.Excerpt,
                PublishedAt = article.PublishedAt!.Value,
                ContentId = null,
                ParentId = null,
                Slug = article.Slug,
                Category = null,
                AuthorId = article.AuthorMemberId,
                AuthorDisplayName = null,
            });

        var news = dbContext.NewsSuggestions
            .AsNoTracking()
            .Where(suggestion => authorIds.Contains(suggestion.SubmitterMemberId)
                && suggestion.Status == NewsSuggestionStatus.Promoted
                && suggestion.PromotedNewsId != null)
            .Select(suggestion => new FeedRow
            {
                Type = MemberPublicActivityType.News,
                Title = suggestion.Title,
                Summary = suggestion.Notes,
                PublishedAt = suggestion.ReviewedAt ?? suggestion.SubmittedAt,
                ContentId = suggestion.PromotedNewsId!.Value,
                ParentId = null,
                Slug = null,
                Category = null,
                AuthorId = suggestion.SubmitterMemberId,
                AuthorDisplayName = null,
            });

        var photos = dbContext.PhotoSubmissions
            .AsNoTracking()
            .Where(photo => authorIds.Contains(photo.SubmitterMemberId)
                && photo.Status == PhotoSubmissionStatus.Approved)
            .Select(photo => new FeedRow
            {
                Type = MemberPublicActivityType.Photo,
                Title = photo.Title,
                Summary = photo.Description,
                PublishedAt = photo.ReviewedAt ?? photo.SubmittedAt,
                ContentId = null,
                ParentId = null,
                Slug = null,
                Category = photo.ApprovedCategory,
                AuthorId = photo.SubmitterMemberId,
                AuthorDisplayName = null,
            });

        return articles.Concat(news).Concat(photos);
    }

    private async Task<Dictionary<Guid, string>> LoadAuthorNamesAsync(
        IReadOnlyList<FeedRow> rows,
        CancellationToken cancellationToken)
    {
        var authorIds = rows.Select(row => row.AuthorId).Distinct().ToList();
        if (authorIds.Count == 0)
        {
            return [];
        }

        return await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(member => authorIds.Contains(member.Id))
            .Select(member => new { member.Id, member.DisplayName })
            .ToDictionaryAsync(member => member.Id, member => member.DisplayName, cancellationToken);
    }

    /// <summary>Promoted news suggestions may carry no title; every other source has one.</summary>
    private static string BuildTitle(FeedRow row) =>
        row.Type == MemberPublicActivityType.News && string.IsNullOrWhiteSpace(row.Title)
            ? "News contribution"
            : row.Title ?? string.Empty;

    /// <summary>
    /// Article slugs are stored; forum and news slugs stay derived from the title as before.
    /// </summary>
    private static string? BuildSlug(FeedRow row) => row.Type switch
    {
        MemberPublicActivityType.Article => row.Slug,
        MemberPublicActivityType.ForumPost => NewsSlug.Slugify(row.Title ?? string.Empty),
        MemberPublicActivityType.News => NewsSlug.Slugify(row.Title ?? "news"),
        _ => null,
    };

    /// <summary>Common projection shape for both source queries.</summary>
    private sealed class FeedRow
    {
        public string Type { get; set; } = string.Empty;

        /// <summary>Nullable because promoted news suggestions may carry no title.</summary>
        public string? Title { get; set; }

        public string? Summary { get; set; }

        public DateTimeOffset PublishedAt { get; set; }

        public int? ContentId { get; set; }

        public int? ParentId { get; set; }

        public string? Slug { get; set; }

        public string? Category { get; set; }

        public Guid AuthorId { get; set; }

        public string? AuthorDisplayName { get; set; }

        /// <summary>Set on linked legacy archive rows whose body is loaded after paging.</summary>
        public long? LegacyForumPostRowId { get; set; }
    }
}
