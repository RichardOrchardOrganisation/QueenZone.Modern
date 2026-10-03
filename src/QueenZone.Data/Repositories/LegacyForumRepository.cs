using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Reads forum categories from legacy <c>Q_FORUM_T</c> via EF Core SQL / stored procedures.
/// Column map and stored procedures are documented in <c>docs/legacy/db-schema.txt</c>
/// and <c>docs/legacy/table-map.md</c> (equivalent to <c>Q_LIST_FORUM_SP</c> / <c>Q_FORUM_MAIN_SP</c>).
/// </summary>
public sealed class LegacyForumRepository(QueenZoneDbContext dbContext) : IForumRepository
{
    private const int CommandTimeoutSeconds = 120;

    private const string CategoriesSelect = """
        SELECT
            CAST(f.Q_FORUM_ID AS int) AS Id,
            f.Q_FORUM_NAME AS Name,
            NULLIF(LTRIM(RTRIM(f.Q_FORUM_DESCRIPTION)), '') AS Description,
            ISNULL(f.Q_FORUM_POST_COUNT, 0) AS PostCount,
            f.Q_FORUM_LAST_POST AS LastActivityAt,
            CAST(NULL AS nvarchar(200)) AS LatestThreadTitle,
            ISNULL(CAST(f.FORUM_ORDER AS int), 0) AS SortOrder
        FROM Q_FORUM_T f
        ORDER BY f.FORUM_ORDER ASC, f.Q_FORUM_ID ASC
        """;

    private const string CategoryByIdSelect = """
        SELECT
            CAST(f.Q_FORUM_ID AS int) AS Id,
            f.Q_FORUM_NAME AS Name,
            NULLIF(LTRIM(RTRIM(f.Q_FORUM_DESCRIPTION)), '') AS Description,
            ISNULL(f.Q_FORUM_POST_COUNT, 0) AS PostCount,
            f.Q_FORUM_LAST_POST AS LastActivityAt,
            NULLIF(LTRIM(RTRIM(latest.TOPIC_SUBJECT)), '') AS LatestThreadTitle,
            ISNULL(CAST(f.FORUM_ORDER AS int), 0) AS SortOrder
        FROM Q_FORUM_T f
        OUTER APPLY (
            SELECT TOP 1 t.TOPIC_SUBJECT
            FROM Q_FORUM_TOPIC_T t
            WHERE t.Q_FORUM_ID = f.Q_FORUM_ID
              AND t.TOPIC_STARTER = 1
            ORDER BY t.TOPIC_LAST_POST DESC
        ) latest
        WHERE f.Q_FORUM_ID = {0}
        """;

    private const string TotalThreadCountSelect = """
        SELECT CAST(ISNULL(SUM(CAST(THREADCOUNT AS bigint)), 0) AS int) AS Value
        FROM dbUser.Q_FORUM_TOPIC_THREAD_COUNT_V
        """;

    private const string RecentThreadsSelect = """
        SELECT TOP ({0})
            CAST(t.Q_FORUM_TOPIC_ID AS int) AS TopicId,
            LTRIM(RTRIM(t.TOPIC_SUBJECT)) AS Title,
            CAST(t.Q_FORUM_ID AS int) AS CategoryId,
            f.Q_FORUM_NAME AS CategoryName,
            ISNULL(CAST(t.TOPIC_REPLIES AS int), 0) AS ReplyCount,
            t.TOPIC_LAST_POST AS LastActivityAt
        FROM Q_FORUM_TOPIC_T t
        INNER JOIN Q_FORUM_T f ON f.Q_FORUM_ID = t.Q_FORUM_ID
        WHERE t.TOPIC_STARTER = 1
          AND LTRIM(RTRIM(ISNULL(t.TOPIC_SUBJECT, ''))) <> ''
        ORDER BY t.TOPIC_LAST_POST DESC, t.Q_FORUM_TOPIC_ID DESC
        """;

    /// <summary>
    /// Equivalent to the legacy <c>Q_DISCOGRAPHY_LIST_SP</c> filter (<c>DISCOGRAPHY = 1</c>),
    /// enriched with the same board/reply/activity columns as <see cref="RecentThreadsSelect"/>
    /// so the public page can render it the same way.
    /// </summary>
    private const string LegacyDiscographyThreadsSelect = """
        SELECT
            CAST(t.Q_FORUM_TOPIC_ID AS int) AS TopicId,
            LTRIM(RTRIM(t.TOPIC_SUBJECT)) AS Title,
            CAST(t.Q_FORUM_ID AS int) AS CategoryId,
            f.Q_FORUM_NAME AS CategoryName,
            ISNULL(CAST(t.TOPIC_REPLIES AS int), 0) AS ReplyCount,
            t.TOPIC_LAST_POST AS LastActivityAt
        FROM Q_FORUM_TOPIC_T t
        INNER JOIN Q_FORUM_T f ON f.Q_FORUM_ID = t.Q_FORUM_ID
        WHERE t.TOPIC_STARTER = 1
          AND t.DISCOGRAPHY = 1
          AND LTRIM(RTRIM(ISNULL(t.TOPIC_SUBJECT, ''))) <> ''
        ORDER BY t.TOPIC_SUBJECT ASC
        """;

    private const string TopicSitemapCountSelect = """
        SELECT COUNT(*) AS Value
        FROM Q_FORUM_TOPIC_T
        WHERE Q_FORUM_TOPIC_PARENT_ID = 0
          AND LTRIM(RTRIM(ISNULL(TOPIC_SUBJECT, ''))) <> ''
        """;

    private const string TopicSitemapPageSelect = """
        SELECT
            t.Q_FORUM_TOPIC_ID AS TopicId,
            LTRIM(RTRIM(t.TOPIC_SUBJECT)) AS Title,
            t.TOPIC_LAST_POST AS LastActivityAt
        FROM Q_FORUM_TOPIC_T t
        WHERE t.Q_FORUM_TOPIC_PARENT_ID = 0
          AND LTRIM(RTRIM(ISNULL(t.TOPIC_SUBJECT, ''))) <> ''
        ORDER BY t.Q_FORUM_TOPIC_ID ASC
        OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY
        """;

    public async Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var rows = await dbContext.Database
            .SqlQueryRaw<ForumCategoryRow>(CategoriesSelect)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var rows = await dbContext.Database
            .SqlQueryRaw<ForumCategoryRow>(CategoryByIdSelect, id)
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
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
            "Q_FORUM_VIEW_PAGE_SP",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@CurrentPage", page));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
                command.Parameters.Add(EfSql.Input("@Q_FORUM_ID", forumId));
                command.Parameters.Add(totalRecords);
            },
            CommandTimeoutSeconds,
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
        var subscribed = EfSql.OutputInt("@SUBSCRIBED");
        var forumName = EfSql.OutputString("@forum_name", 30);
        var subject = EfSql.OutputString("@SUBJECT", 75);
        var forumIdParam = EfSql.OutputInt("@Q_FORUM_ID");
        var disco = EfSql.OutputByte("@DISCO");

        var rows = await EfSql.QueryProcAsync<ForumPostRow>(
            dbContext,
            "Q_FORUM_TOPIC_NEW_SP",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@CurrentPage", page));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
                command.Parameters.Add(EfSql.Input("@Q_FORUM_TOPIC_ID", topicId));
                command.Parameters.Add(EfSql.Input("@USER_ID", 0));
                command.Parameters.Add(totalRecords);
                command.Parameters.Add(subscribed);
                command.Parameters.Add(forumName);
                command.Parameters.Add(subject);
                command.Parameters.Add(forumIdParam);
                command.Parameters.Add(disco);
            },
            CommandTimeoutSeconds,
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
            EfSql.GetNullableString(forumName)?.Trim() ?? string.Empty);

        return new ForumTopicPostsPage(
            header,
            rows.Select(MapPost).ToList(),
            EfSql.GetNullableInt(totalRecords) ?? 0,
            page,
            pageSize);
    }

    public async Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        return await dbContext.Database
            .SqlQueryRaw<int>(TotalThreadCountSelect)
            .FirstAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, 50);
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var rows = await dbContext.Database
            .SqlQueryRaw<ForumRecentThreadRow>(RecentThreadsSelect, take)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ForumRecentThreadItem(
                row.TopicId,
                row.Title?.Trim() ?? string.Empty,
                row.CategoryId,
                row.CategoryName?.Trim() ?? string.Empty,
                row.ReplyCount,
                row.LastActivityAt ?? DateTime.MinValue))
            .ToList();
    }

    public async Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var rows = await dbContext.Database
            .SqlQueryRaw<ForumRecentThreadRow>(LegacyDiscographyThreadsSelect)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ForumRecentThreadItem(
                row.TopicId,
                row.Title?.Trim() ?? string.Empty,
                row.CategoryId,
                row.CategoryName?.Trim() ?? string.Empty,
                row.ReplyCount,
                row.LastActivityAt ?? DateTime.MinValue))
            .ToList();
    }

    public async Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default)
    {
        var categories = await GetCategoriesAsync(cancellationToken);
        var threadCount = await GetTotalThreadCountAsync(cancellationToken);
        return ForumArchiveStats.FromCategories(categories, threadCount);
    }

    public async Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        return await dbContext.Database
            .SqlQueryRaw<int>(TopicSitemapCountSelect)
            .FirstAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        dbContext.Database.SetCommandTimeout(CommandTimeoutSeconds);
        var rows = await dbContext.Database
            .SqlQueryRaw<ForumTopicSitemapRow>(TopicSitemapPageSelect, Math.Max(offset, 0), pageSize)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ForumTopicSitemapItem(
                row.TopicId,
                row.Title?.Trim() ?? string.Empty,
                row.LastActivityAt))
            .ToList();
    }

    public Task<ForumSearchPage> SearchForumAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Forum search is not supported on the legacy forum path.");

    public async Task<ForumLegacyPostLocation?> FindLegacyPostAsync(
        int legacyPostId,
        CancellationToken cancellationToken = default)
    {
        // Replies point at their starter through Q_FORUM_TOPIC_PARENT_ID; starters hold their own
        // ID, 0 or NULL there. PostIndex mirrors Q_FORUM_TOPIC_NEW_SP: validated authors,
        // DISCOGRAPHY <> 2, TOPIC_DATE ascending (ID breaks same-minute ties). The target post
        // itself skips the author check so starters by since-deleted members still resolve.
        var rows = await EfSql.QuerySqlAsync<ForumLegacyPostRow>(
            dbContext,
            """
            SELECT TOP (1)
                root.Q_FORUM_TOPIC_ID AS TopicId,
                root.TOPIC_SUBJECT AS Title,
                p.Q_FORUM_TOPIC_ID AS PostId,
                (SELECT COUNT(*)
                 FROM dbo.Q_FORUM_TOPIC_T earlier
                 INNER JOIN dbo.USERS_T earlierUser ON earlierUser.USER_ID = earlier.USER_ID
                 WHERE (earlier.Q_FORUM_TOPIC_ID = root.Q_FORUM_TOPIC_ID
                        OR earlier.Q_FORUM_TOPIC_PARENT_ID = root.Q_FORUM_TOPIC_ID)
                   AND earlier.DISCOGRAPHY <> 2
                   AND earlierUser.validated = 1
                   AND (earlier.TOPIC_DATE < p.TOPIC_DATE
                        OR (earlier.TOPIC_DATE = p.TOPIC_DATE AND earlier.Q_FORUM_TOPIC_ID < p.Q_FORUM_TOPIC_ID))) AS PostIndex
            FROM dbo.Q_FORUM_TOPIC_T p
            INNER JOIN dbo.Q_FORUM_TOPIC_T root ON root.Q_FORUM_TOPIC_ID =
                CASE WHEN ISNULL(p.Q_FORUM_TOPIC_PARENT_ID, 0) = 0 THEN p.Q_FORUM_TOPIC_ID ELSE p.Q_FORUM_TOPIC_PARENT_ID END
            WHERE p.Q_FORUM_TOPIC_ID = @LegacyPostId
              AND p.DISCOGRAPHY <> 2
              AND root.DISCOGRAPHY <> 2;
            """,
            command => command.Parameters.Add(EfSql.Input("@LegacyPostId", legacyPostId)),
            CommandTimeoutSeconds,
            cancellationToken);

        return rows.Select(ForumLegacyPostRow.Map).FirstOrDefault();
    }

    private static ForumCategoryItem Map(ForumCategoryRow row) =>
        new(
            row.Id,
            row.Name?.Trim() ?? string.Empty,
            row.Description,
            row.PostCount,
            row.LastActivityAt,
            row.LatestThreadTitle,
            row.SortOrder);

    private static ForumPostItem MapPost(ForumPostRow row) =>
        new(
            row.Q_FORUM_TOPIC_ID,
            row.TOPIC_MESSAGE?.Trim() ?? string.Empty,
            row.TOPIC_DATE,
            row.USERNAME?.Trim() ?? "Unknown",
            string.IsNullOrWhiteSpace(row.SIGNATURE) ? null : row.SIGNATURE.Trim(),
            row.NUMBER_OF_POSTS,
            row.DATE_CREATED,
            ForumPostAttachment.Parse(row.ATTACHMENT, row.FILESIZE, row.Q_FORUM_TOPIC_ID));

    private static ForumTopicItem MapTopic(ForumTopicRow row) =>
        new(
            row.Q_FORUM_TOPIC_ID,
            row.TOPIC_SUBJECT?.Trim() ?? string.Empty,
            row.TOPIC_LAST_POST,
            row.USERNAME?.Trim() ?? "Unknown",
            row.NUMBEROFREPLIES,
            string.IsNullOrWhiteSpace(row.LAST_POST_USERNAME) ? null : row.LAST_POST_USERNAME.Trim(),
            row.STICKY == 1);

    internal sealed class ForumPostRow
    {
        public string? TOPIC_MESSAGE { get; set; }

        public DateTime TOPIC_DATE { get; set; }

        public int USER_ID { get; set; }

        public string? USERNAME { get; set; }

        public string? SIGNATURE { get; set; }

        public short NUMBER_OF_POSTS { get; set; }

        public DateTime? DATE_CREATED { get; set; }

        public int Q_FORUM_TOPIC_ID { get; set; }

        public string? ATTACHMENT { get; set; }

        public string? FILESIZE { get; set; }

        public short ATTACH_COUNT { get; set; }

        public byte ONLINE { get; set; }

        public string? AVATAR { get; set; }

        public string? DISPLAY_MESSAGE { get; set; }

        public byte DISCO { get; set; }
    }

    internal sealed class ForumTopicRow
    {
        public int Id { get; set; }

        public int Q_FORUM_TOPIC_ID { get; set; }

        public string TOPIC_SUBJECT { get; set; } = string.Empty;

        public DateTime TOPIC_LAST_POST { get; set; }

        public int USER_ID { get; set; }

        public string USERNAME { get; set; } = string.Empty;

        public short NUMBEROFREPLIES { get; set; }

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

    internal sealed class ForumTopicSitemapRow
    {
        public int TopicId { get; set; }

        public string? Title { get; set; }

        public DateTime? LastActivityAt { get; set; }
    }

    internal sealed class ForumRecentThreadRow
    {
        public int TopicId { get; set; }

        public string? Title { get; set; }

        public int CategoryId { get; set; }

        public string? CategoryName { get; set; }

        public int ReplyCount { get; set; }

        public DateTime? LastActivityAt { get; set; }
    }
}
