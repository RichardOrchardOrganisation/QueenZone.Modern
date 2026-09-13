using System.Diagnostics.CodeAnalysis;

namespace QueenZone.Data;

/// <summary>
/// Production SQL Server query text for EF public-read repositories.
/// Excluded from coverage: exercised against real SQL Server in opt-in probes / production,
/// while deterministic tests inject SQLite-compatible SQL via repository test constructors.
/// Dynamic ints use EF <c>{0}</c> placeholders (passed to <c>SqlQueryRaw</c>), not string interpolation.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class EfProductionSql
{
    /// <summary>
    /// Characters of <c>ARTICLE_TEXT</c> pulled for list/archive excerpt derivation only.
    /// Full body remains detail-only so archive pages do not stream entire LOBs.
    /// </summary>
    internal const int ArticlesListBodyPreviewChars = 2000;

    public static (
        string Latest,
        string Count,
        string ArchivePage,
        string ById,
        string Sitemap)
        CreateArticlesQueries()
    {
        // List/archive: truncated ARTICLE_TEXT only (enough for GetExcerpt). Detail: full body.
        var listSelect = $"""
            SELECT
                CAST(a.Q_ARTICLE_ID AS int) AS Id,
                a.ARTICLE_NAME AS Title,
                LEFT(ISNULL(CAST(a.ARTICLE_TEXT AS nvarchar(max)), N''), {ArticlesListBodyPreviewChars}) AS Body,
                a.DATE_CREATED AS PublishedAt,
                NULLIF(LTRIM(RTRIM(a.SOURCE)), '') AS Source,
                NULLIF(LTRIM(RTRIM(c.ARTICLE_CATEGORY)), '') AS CategoryName,
                CAST(CASE WHEN a.DISPLAY = 1 THEN 1 ELSE 0 END AS bit) AS IsPublished
            FROM Q_ARTICLE_T a
            LEFT JOIN Q_ARTICLE_CATEGORY_T c
                ON c.Q_ARTICLE_CAT_ID = a.Q_ARTICLE_CATEGORY_ID
            WHERE a.DISPLAY = 1
            """;

        const string detailSelect = """
            SELECT
                CAST(a.Q_ARTICLE_ID AS int) AS Id,
                a.ARTICLE_NAME AS Title,
                ISNULL(CAST(a.ARTICLE_TEXT AS nvarchar(max)), N'') AS Body,
                a.DATE_CREATED AS PublishedAt,
                NULLIF(LTRIM(RTRIM(a.SOURCE)), '') AS Source,
                NULLIF(LTRIM(RTRIM(c.ARTICLE_CATEGORY)), '') AS CategoryName,
                CAST(CASE WHEN a.DISPLAY = 1 THEN 1 ELSE 0 END AS bit) AS IsPublished
            FROM Q_ARTICLE_T a
            LEFT JOIN Q_ARTICLE_CATEGORY_T c
                ON c.Q_ARTICLE_CAT_ID = a.Q_ARTICLE_CATEGORY_ID
            WHERE a.DISPLAY = 1
            """;

        return (
            listSelect + """

                ORDER BY a.DATE_CREATED DESC, a.Q_ARTICLE_ID DESC
                OFFSET 0 ROWS FETCH NEXT {0} ROWS ONLY
                """,
            """
            SELECT COUNT(*) AS Value
            FROM Q_ARTICLE_T
            WHERE DISPLAY = 1
            """,
            listSelect + """

                ORDER BY a.DATE_CREATED DESC, a.Q_ARTICLE_ID DESC
                OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY
                """,
            detailSelect + """

                  AND a.Q_ARTICLE_ID = {0}
                """,
            """
            SELECT
                CAST(a.Q_ARTICLE_ID AS int) AS Id,
                a.ARTICLE_NAME AS Title,
                a.DATE_CREATED AS PublishedAt,
                CAST(NULL AS nvarchar(200)) AS Slug
            FROM Q_ARTICLE_T a
            WHERE a.DISPLAY = 1
            ORDER BY a.DATE_CREATED DESC, a.Q_ARTICLE_ID DESC
            """);
    }

    /// <summary>
    /// Builds public news SQL. List/count/sitemap use a body-free CTE; detail uses a CTE with body.
    /// </summary>
    /// <param name="listCte">CTE without <c>Body</c>/<c>ARTICLE</c> (from <see cref="PublishedNewsQuery.BuildPublishedNewsCte"/> with <c>includeBody: false</c>).</param>
    /// <param name="detailCte">CTE with full body ( <c>includeBody: true</c> ).</param>
    public static (
        string Latest,
        string Count,
        string ArchivePage,
        string ById,
        string Sitemap,
        string ArchivePageByDecade,
        string CountByDecade,
        string YearRange)
        CreateNewsQueries(string listCte, string detailCte) =>
        (
            // $$ raw strings: {{expr}} interpolates; single {0} stays a literal EF parameter placeholder.
            listCte + $$"""

                SELECT TOP ({0})
                    Id,
                    Title,
                    Excerpt,
                    Body,
                    PublishedAt,
                    SourceUrl,
                    IsPublished,
                    Slug,
                    ImageBlobKey,
                    ImageGalleryPicId,
                    ForumTopicId
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                ORDER BY PublishedAt DESC, Id DESC
                """,
            listCte + $$"""

                SELECT COUNT(*) AS Value
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                """,
            listCte + $$"""

                SELECT
                    Id,
                    Title,
                    Excerpt,
                    Body,
                    PublishedAt,
                    SourceUrl,
                    IsPublished,
                    Slug,
                    ImageBlobKey,
                    ImageGalleryPicId,
                    ForumTopicId
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                ORDER BY PublishedAt DESC, Id DESC
                OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY
                """,
            detailCte + $$"""

                SELECT
                    Id,
                    Title,
                    Excerpt,
                    Body,
                    PublishedAt,
                    SourceUrl,
                    IsPublished,
                    Slug,
                    ImageBlobKey,
                    ImageGalleryPicId,
                    ForumTopicId
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                  AND Id = {0}
                """,
            listCte + $$"""

                SELECT
                    Id,
                    Title,
                    PublishedAt,
                    Slug
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                ORDER BY PublishedAt DESC, Id DESC
                """,
            // Decade filter: {0} = inclusive start (UTC datetime), {1} = exclusive end, {2} = OFFSET, {3} = FETCH.
            listCte + $$"""

                SELECT
                    Id,
                    Title,
                    Excerpt,
                    Body,
                    PublishedAt,
                    SourceUrl,
                    IsPublished,
                    Slug,
                    ImageBlobKey,
                    ImageGalleryPicId,
                    ForumTopicId
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                  AND PublishedAt >= {0} AND PublishedAt < {1}
                ORDER BY PublishedAt DESC, Id DESC
                OFFSET {2} ROWS FETCH NEXT {3} ROWS ONLY
                """,
            // Decade filter: {0} = inclusive start (UTC datetime), {1} = exclusive end.
            listCte + $$"""

                SELECT COUNT(*) AS Value
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                  AND PublishedAt >= {0} AND PublishedAt < {1}
                """,
            // Feeds the mobile year-rail scrubber's tick-mark range (issue #886).
            listCte + $$"""

                SELECT
                    MIN(PublishedAt) AS MinPublishedAt,
                    MAX(PublishedAt) AS MaxPublishedAt
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                """);

    /// <summary>
    /// Builds LIKE-based news search queries used as a SQLite fallback for deterministic tests.
    /// Production uses the <c>dbo.NEWS_T_SearchPublished</c> stored procedure (full-text search).
    /// <paramref name="searchCte"/> must project <c>Body</c> from <c>ARTICLE</c> so the LIKE clause can match it;
    /// the outer SELECT replaces Body with an empty constant so the LOB is not sent to the application.
    /// Parameters: {0} = LIKE pattern (e.g. <c>%freddie%</c>), {1} = OFFSET row count, {2} = FETCH row count.
    /// The count query uses only {0}.
    /// </summary>
    public static (string SearchSql, string SearchCountSql) CreateNewsSqliteLikeSearchQueries(string searchCte) =>
        (
            searchCte + $$"""

                SELECT
                    Id,
                    Title,
                    Excerpt,
                    CAST(N'' AS nvarchar(max)) AS Body,
                    PublishedAt,
                    SourceUrl,
                    IsPublished,
                    Slug,
                    ImageBlobKey,
                    ImageGalleryPicId,
                    ForumTopicId
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                  AND (Title LIKE {0} OR Excerpt LIKE {0} OR Body LIKE {0})
                ORDER BY PublishedAt DESC, Id DESC
                OFFSET {1} ROWS FETCH NEXT {2} ROWS ONLY
                """,
            searchCte + $$"""

                SELECT COUNT(*) AS Value
                FROM PublishedNews
                WHERE {{PublishedNewsQuery.LatestRowFilter}}
                  AND (Title LIKE {0} OR Excerpt LIKE {0} OR Body LIKE {0})
                """);

    public static (string ListSql, Func<int, FormattableString> DisplaySql, Func<int, FormattableString> SongsSql)
        CreateDiscographyQueries() =>
        (
            "EXEC Q_ALBUM_LIST_SP",
            static id => $"EXEC Q_ALBUM_T_DISPLAY_SP @Q_ALBUM_ID = {id}",
            static id => $"EXEC Q_ALBUM_SONG_T_LIST_SP @Q_ALBUM_ID = {id}");

    public static (bool UseProcs, string PageSelect, string CountSql, Func<int, FormattableString> ByIdSql)
        CreateFanPerformanceQueries() =>
        (
            true,
            string.Empty,
            "SELECT COUNT(*) AS Value FROM dbo.Q_STAGE_T WHERE DISPLAY = 1",
            static id => $"""
                SELECT Q_STAGE_ID, TITLE, PERFORMED_BY, DESCRIPTION, URL, thesize, DATE_ADDED
                FROM dbo.Q_STAGE_T
                WHERE Q_STAGE_ID = {id} AND DISPLAY = 1
                """);

    public static PhotoSqlQueries CreatePhotoQueries() => PhotoSqlQueries.CreateProduction();

    public static Func<string, FormattableString> CreateMemberLookupSql() =>
        email => $"""
            SELECT USER_ID, USERNAME
            FROM dbo.USERS_T
            WHERE EMAIL = {email}
            ORDER BY USERNAME, USER_ID
            """;

    public static Func<int, FormattableString> CreateMemberLookupByUserIdSql() =>
        userId => $"""
            SELECT TOP 1 USER_ID, USERNAME
            FROM dbo.USERS_T
            WHERE USER_ID = {userId}
            """;

    public static Func<int, FormattableString> CreateForumArchiveAuthorSummarySql() =>
        legacyUserId => $"""
            SELECT LegacyUserId, DisplayName, MemberSince, PostCount
            FROM dbo.ModernForumArchiveAuthorSummary
            WHERE LegacyUserId = {legacyUserId}
            """;

    public static (string ListSql, Func<short, FormattableString> DetailSql) CreateBiographyQueries() =>
        (
            "EXEC Q_BIO_LIST_SP",
            static id => $"EXEC Q_BIO_DISPLAY_SP @Q_BIO_ID = {id}");
}
