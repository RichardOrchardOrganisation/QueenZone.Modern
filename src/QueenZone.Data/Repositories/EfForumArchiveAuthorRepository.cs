using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Backed by <c>IX_ModernForumPost_AuthorLegacyUserId_PostedAt</c> so paging through a prolific
/// legacy author's posts stays fast even though the underlying table holds the whole imported
/// archive (see <see cref="QueenZoneDbContext"/> and the matching migration).
/// </summary>
public sealed class EfForumArchiveAuthorRepository : IForumArchiveAuthorRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly Func<int, FormattableString> summarySql;

    [ExcludeFromCodeCoverage]
    public EfForumArchiveAuthorRepository(QueenZoneDbContext dbContext)
        : this(dbContext, EfProductionSql.CreateForumArchiveAuthorSummarySql())
    {
    }

    internal EfForumArchiveAuthorRepository(
        QueenZoneDbContext dbContext,
        Func<int, FormattableString> summarySql)
    {
        this.dbContext = dbContext;
        this.summarySql = summarySql;
    }

    public async Task<ForumArchiveAuthorSummary?> GetSummaryAsync(
        int legacyUserId,
        CancellationToken cancellationToken = default)
    {
        var summary = await dbContext.Database
            .SqlQuery<ArchiveAuthorSummaryRow>(summarySql(legacyUserId))
            .FirstOrDefaultAsync(cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return new ForumArchiveAuthorSummary(
            summary.LegacyUserId,
            summary.DisplayName,
            summary.MemberSince,
            summary.PostCount);
    }

    public async Task<MemberPublicActivityPage> GetPostsPageAsync(
        int legacyUserId,
        int page,
        int pageSize,
        int totalCount,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        totalCount = Math.Max(totalCount, 0);

        // Page over the narrow author index before loading BodyHtml and joining threads.
        // A prolific author's thousands of large posts must not be fetched just to sort
        // and discard all but the requested page.
        var pageIds = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => post.AuthorLegacyUserId == legacyUserId && !post.IsHidden)
            .OrderByDescending(post => post.PostedAt)
            .ThenByDescending(post => post.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(post => post.Id)
            .ToListAsync(cancellationToken);

        if (pageIds.Count == 0)
        {
            return new MemberPublicActivityPage([], totalCount, page, pageSize);
        }

        var rows = await dbContext.ModernForumPosts
            .AsNoTracking()
            .Where(post => pageIds.Contains(post.Id) && post.Thread != null)
            .Select(post => new
            {
                post.Id,
                post.LegacyPostId,
                post.LegacyThreadTopicId,
                ThreadTitle = post.Thread!.Title,
                post.BodyHtml,
                post.PostedAt,
                post.AuthorDisplayName,
            })
            .ToListAsync(cancellationToken);

        var rowsById = rows.ToDictionary(row => row.Id);
        var items = pageIds
            .Where(rowsById.ContainsKey)
            .Select(id => rowsById[id])
            .Select(row => new MemberPublicActivityItem(
                MemberPublicActivityType.ForumPost,
                row.ThreadTitle,
                row.BodyHtml,
                ToOffset(row.PostedAt),
                row.LegacyPostId,
                row.LegacyThreadTopicId,
                NewsSlug.Slugify(row.ThreadTitle),
                AuthorDisplayName: row.AuthorDisplayName))
            .ToList();

        return new MemberPublicActivityPage(items, totalCount, page, pageSize);
    }

    private static DateTimeOffset ToOffset(DateTime? value) =>
        new(DateTime.SpecifyKind(value ?? DateTime.MinValue, DateTimeKind.Utc));

    internal sealed class ArchiveAuthorSummaryRow
    {
        public int LegacyUserId { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public DateTime? MemberSince { get; set; }

        public int PostCount { get; set; }
    }
}
