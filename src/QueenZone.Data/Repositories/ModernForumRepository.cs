using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Reads the public forum archive from the imported <c>ModernForum*</c> tables.
/// Stored procedures and supporting indexes are documented in <c>docs/sql/006-modern-forum-read-path.sql</c>.
/// Invoked through EF Core rather than Dapper.
/// </summary>
public sealed class ModernForumRepository(QueenZoneDbContext dbContext) : IForumRepository
{
    /// <summary>Interactive public forum pages — align with default request budget.</summary>
    private const int InteractiveCommandTimeoutSeconds = QueenZoneSqlServerOptions.DefaultCommandTimeoutSeconds;

    /// <summary>Sitemap/export pages may scan large offsets; keep a higher ceiling.</summary>
    private const int SitemapCommandTimeoutSeconds = 120;

    public async Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await EfSql.QueryProcAsync<ForumCategoryRow>(
            dbContext,
            "ModernForum_GetCategories",
            commandTimeoutSeconds: InteractiveCommandTimeoutSeconds,
            cancellationToken: cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await EfSql.QueryProcSingleOrDefaultAsync<ForumCategoryRow>(
            dbContext,
            "ModernForum_GetCategoryByLegacyForumId",
            command => command.Parameters.Add(EfSql.Input("@Q_FORUM_ID", id)),
            InteractiveCommandTimeoutSeconds,
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<ForumCategoryTopicsPage> GetCategoryTopicsPageAsync(
        int forumId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var totalRecords = EfSql.OutputInt("@TotalRecords");
        var rows = await EfSql.QueryProcAsync<ForumTopicRow>(
            dbContext,
            "ModernForum_GetCategoryThreadsPage",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@CurrentPage", page));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
                command.Parameters.Add(EfSql.Input("@Q_FORUM_ID", forumId));
                command.Parameters.Add(totalRecords);
            },
            InteractiveCommandTimeoutSeconds,
            cancellationToken);

        return new ForumCategoryTopicsPage(
            rows.Select(MapTopic).ToList(),
            EfSql.GetNullableInt(totalRecords) ?? 0,
            page,
            pageSize);
    }

    public async Task<ForumTopicPostsPage?> GetTopicPostsPageAsync(
        int topicId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var totalRecords = EfSql.OutputInt("@TotalRecords");
        var forumName = EfSql.OutputString("@forum_name", 100);
        var subject = EfSql.OutputString("@SUBJECT", 200);
        var forumIdParam = EfSql.OutputInt("@Q_FORUM_ID");
        var disco = EfSql.OutputByte("@DISCO");
        var hasPoll = EfSql.OutputBool("@HasPoll");

        var rows = await EfSql.QueryProcAsync<ForumPostRow>(
            dbContext,
            "ModernForum_GetTopicPostsPage",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@CurrentPage", page));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
                command.Parameters.Add(EfSql.Input("@Q_FORUM_TOPIC_ID", topicId));
                command.Parameters.Add(totalRecords);
                command.Parameters.Add(forumName);
                command.Parameters.Add(subject);
                command.Parameters.Add(forumIdParam);
                command.Parameters.Add(disco);
                command.Parameters.Add(hasPoll);
            },
            InteractiveCommandTimeoutSeconds,
            cancellationToken);

        var forumId = EfSql.GetNullableInt(forumIdParam) ?? 0;
        var title = EfSql.GetNullableString(subject)?.Trim();
        if (forumId <= 0 || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var header = new ForumTopicHeader(
            topicId,
            title,
            forumId,
            EfSql.GetNullableString(forumName)?.Trim() ?? string.Empty,
            EfSql.GetNullableBool(hasPoll));

        var posts = rows.Select(MapPost).ToList();
        posts = await ForumAttachmentMerge.MergeModernAsync(dbContext, posts, cancellationToken);

        return new ForumTopicPostsPage(
            header,
            posts,
            EfSql.GetNullableInt(totalRecords) ?? 0,
            page,
            pageSize);
    }

    public Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default) =>
        EfSql.ExecuteScalarProcAsync(
            dbContext,
            "ModernForum_GetTotalThreadCount",
            commandTimeoutSeconds: InteractiveCommandTimeoutSeconds,
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, 50);
        dbContext.Database.SetCommandTimeout(InteractiveCommandTimeoutSeconds);

        return await dbContext.ModernForumThreads
            .AsNoTracking()
            .Where(thread =>
                thread.IsLegacyTopicStarter
                && !thread.IsHidden
                && thread.StartedByUserValidated == true
                && thread.LastActivityAt != null
                && thread.Category != null
                && !thread.Category.IsSynthetic
                && thread.Category.LegacyForumId != ForumRecentThreadsPolicy.WebsiteDiscussionBoardId)
            .OrderByDescending(thread => thread.LastActivityAt)
            .ThenByDescending(thread => thread.LegacyTopicId)
            .Take(take)
            .Select(thread => new ForumRecentThreadItem(
                thread.LegacyTopicId,
                thread.Title,
                thread.Category!.LegacyForumId,
                thread.Category.Name,
                thread.ReplyCount,
                thread.LastActivityAt!.Value))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// John S Stuart's rare/discography analysis posts, flagged in the legacy import via
    /// <c>LegacyDiscography = 1</c> (the old <c>Q_DISCOGRAPHY_LIST_SP</c> filter). Returns the
    /// full set, ordered to match that legacy procedure's alphabetical listing.
    /// </summary>
    public async Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(InteractiveCommandTimeoutSeconds);

        return await dbContext.ModernForumThreads
            .AsNoTracking()
            .Where(thread =>
                thread.IsLegacyTopicStarter
                && thread.LegacyDiscography == 1
                && !thread.IsHidden
                && thread.StartedByUserValidated == true
                && thread.LastActivityAt != null
                && thread.Category != null
                && !thread.Category.IsSynthetic)
            .OrderBy(thread => thread.Title)
            .Select(thread => new ForumRecentThreadItem(
                thread.LegacyTopicId,
                thread.Title,
                thread.Category!.LegacyForumId,
                thread.Category.Name,
                thread.ReplyCount,
                thread.LastActivityAt!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default)
    {
        var categories = await GetCategoriesAsync(cancellationToken);
        var threadCount = await GetTotalThreadCountAsync(cancellationToken);
        return ForumArchiveStats.FromCategories(categories, threadCount);
    }

    public Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default) =>
        EfSql.ExecuteScalarProcAsync(
            dbContext,
            "ModernForum_GetTopicSitemapCount",
            commandTimeoutSeconds: SitemapCommandTimeoutSeconds,
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var rows = await EfSql.QueryProcAsync<ForumTopicSitemapRow>(
            dbContext,
            "ModernForum_GetTopicSitemapPage",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Offset", Math.Max(offset, 0)));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
            },
            SitemapCommandTimeoutSeconds,
            cancellationToken);

        return rows
            .Select(row => new ForumTopicSitemapItem(
                row.TopicId,
                row.Title?.Trim() ?? string.Empty,
                row.LastActivityAt))
            .ToList();
    }

    public async Task<ForumSearchPage> SearchForumAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ForumSearchPage([], 0, page, pageSize);
        }

        return await ExecuteSearchAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<ForumLegacyPostLocation?> FindLegacyPostAsync(
        int legacyPostId,
        CancellationToken cancellationToken = default)
    {
        // Topic IDs win over post IDs: old links overwhelmingly point at topics, and modern
        // threads allocate topic and post IDs from separate sequences that can overlap.
        // PostIndex mirrors ModernForum_GetTopicPostsPage ordering (visible posts by LegacyPostId).
        var rows = await EfSql.QuerySqlAsync<ForumLegacyPostRow>(
            dbContext,
            """
            SELECT TOP (1) TopicId, Title, PostId, PostIndex
            FROM
            (
                SELECT 0 AS Priority, t.LegacyTopicId AS TopicId, t.Title, t.LegacyTopicId AS PostId, 0 AS PostIndex
                FROM dbo.ModernForumThread t
                WHERE t.LegacyTopicId = @LegacyPostId
                  AND t.IsHidden = 0
                UNION ALL
                SELECT 1, t.LegacyTopicId, t.Title, p.LegacyPostId,
                    (SELECT COUNT(*)
                     FROM dbo.ModernForumPost earlier
                     WHERE earlier.ThreadId = p.ThreadId
                       AND earlier.IsHidden = 0
                       AND earlier.LegacyPostId < p.LegacyPostId)
                FROM dbo.ModernForumPost p
                INNER JOIN dbo.ModernForumThread t ON t.Id = p.ThreadId
                WHERE p.LegacyPostId = @LegacyPostId
                  AND p.IsHidden = 0
                  AND t.IsHidden = 0
            ) matches
            ORDER BY Priority;
            """,
            command => command.Parameters.Add(EfSql.Input("@LegacyPostId", legacyPostId)),
            InteractiveCommandTimeoutSeconds,
            cancellationToken);

        return rows.Select(ForumLegacyPostRow.Map).FirstOrDefault();
    }

    private async Task<ForumSearchPage> ExecuteSearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var offset = Math.Max(page - 1, 0) * pageSize;
        var totalRecords = EfSql.OutputInt("@TotalRecords");
        var rows = await EfSql.QueryProcAsync<ForumSearchRow>(
            dbContext,
            "ModernForum_SearchThreads",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Query", query));
                command.Parameters.Add(EfSql.Input("@Offset", offset));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
                command.Parameters.Add(totalRecords);
            },
            InteractiveCommandTimeoutSeconds,
            cancellationToken);

        return new ForumSearchPage(
            rows.Select(MapSearch).ToList(),
            EfSql.GetNullableInt(totalRecords) ?? 0,
            page,
            pageSize);
    }

    private static ForumCategoryItem Map(ForumCategoryRow row) =>
        new(
            row.Id,
            row.Name?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
            row.PostCount,
            row.LastActivityAt,
            string.IsNullOrWhiteSpace(row.LatestThreadTitle) ? null : row.LatestThreadTitle.Trim(),
            row.SortOrder);

    private static ForumPostItem MapPost(ForumPostRow row) =>
        new(
            row.Q_FORUM_TOPIC_ID,
            row.TOPIC_MESSAGE?.Trim() ?? string.Empty,
            row.TOPIC_DATE ?? DateTime.MinValue,
            row.USERNAME?.Trim() ?? "Unknown",
            string.IsNullOrWhiteSpace(row.SIGNATURE) ? null : row.SIGNATURE.Trim(),
            row.NUMBER_OF_POSTS ?? 0,
            row.DATE_CREATED,
            ForumPostAttachment.Parse(row.ATTACHMENT, row.FILESIZE, row.Q_FORUM_TOPIC_ID),
            row.AuthorMemberId,
            row.EditedAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.EditedAt.Value, DateTimeKind.Utc))
                : null,
            row.EditCount,
            row.USER_ID);

    private static ForumTopicItem MapTopic(ForumTopicRow row) =>
        new(
            row.Q_FORUM_TOPIC_ID,
            row.TOPIC_SUBJECT?.Trim() ?? string.Empty,
            row.TOPIC_LAST_POST ?? DateTime.MinValue,
            row.USERNAME?.Trim() ?? "Unknown",
            row.NUMBEROFREPLIES,
            string.IsNullOrWhiteSpace(row.LAST_POST_USERNAME) ? null : row.LAST_POST_USERNAME.Trim(),
            row.STICKY == 1);

    private static ForumSearchResult MapSearch(ForumSearchRow row) =>
        new(
            row.TopicId,
            row.Title?.Trim() ?? string.Empty,
            row.CategoryId,
            row.CategoryName?.Trim() ?? string.Empty,
            row.ReplyCount,
            row.LastActivityAt,
            string.IsNullOrWhiteSpace(row.StartedByDisplayName) ? null : row.StartedByDisplayName.Trim());

    internal sealed class ForumPostRow
    {
        public string? TOPIC_MESSAGE { get; set; }

        public DateTime? TOPIC_DATE { get; set; }

        public int? USER_ID { get; set; }

        public string? USERNAME { get; set; }

        public string? SIGNATURE { get; set; }

        public int? NUMBER_OF_POSTS { get; set; }

        public DateTime? DATE_CREATED { get; set; }

        public int Q_FORUM_TOPIC_ID { get; set; }

        public string? ATTACHMENT { get; set; }

        public string? FILESIZE { get; set; }

        public int ATTACH_COUNT { get; set; }

        public byte ONLINE { get; set; }

        public string? AVATAR { get; set; }

        public string? DISPLAY_MESSAGE { get; set; }

        public byte DISCO { get; set; }

        public Guid? AuthorMemberId { get; set; }

        public DateTime? EditedAt { get; set; }

        public int EditCount { get; set; }
    }

    internal sealed class ForumTopicRow
    {
        public int Id { get; set; }

        public int Q_FORUM_TOPIC_ID { get; set; }

        public string? TOPIC_SUBJECT { get; set; }

        public DateTime? TOPIC_LAST_POST { get; set; }

        public int? USER_ID { get; set; }

        public string? USERNAME { get; set; }

        public int NUMBEROFREPLIES { get; set; }

        public string? LAST_POST_USERNAME { get; set; }

        public byte STICKY { get; set; }
    }

    internal sealed class ForumCategoryRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public int PostCount { get; set; }

        public DateTime? LastActivityAt { get; set; }

        public string? LatestThreadTitle { get; set; }

        public int SortOrder { get; set; }
    }

    internal sealed class ForumSearchRow
    {
        public int TopicId { get; set; }

        public string? Title { get; set; }

        public int CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public int ReplyCount { get; set; }

        public DateTime? LastActivityAt { get; set; }

        public string? StartedByDisplayName { get; set; }
    }

    internal sealed class ForumTopicSitemapRow
    {
        public int TopicId { get; set; }

        public string? Title { get; set; }

        public DateTime? LastActivityAt { get; set; }
    }
}
