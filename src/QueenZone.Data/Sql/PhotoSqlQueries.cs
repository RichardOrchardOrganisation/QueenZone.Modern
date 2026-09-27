namespace QueenZone.Data;

/// <summary>
/// SQL shapes for <see cref="EfPhotoRepository"/>. Production uses SQL Server;
/// tests substitute SQLite-compatible selects against fixture tables.
/// Placeholders follow EF <c>SqlQueryRaw</c> conventions: <c>{0}</c>, <c>{1}</c>, …
/// </summary>
public sealed class PhotoSqlQueries
{
    /// <summary>
    /// Prefer linked modern member via legacy <c>PIC_FILES_T.user_id</c>, then modern photo
    /// submission submitter via <c>PhotoSubmissions.PromotedPicId</c>, then legacy username.
    /// </summary>
    private const string SubmittedByDisplayNameSql = """
        COALESCE(
            (
                SELECT TOP (1) NULLIF(LTRIM(RTRIM(m.DisplayName)), N'')
                FROM dbo.MemberAccounts m
                WHERE m.LinkedLegacyUserId = p.user_id
                  AND m.DisplayName IS NOT NULL
                ORDER BY m.Id
            ),
            (
                SELECT TOP (1) NULLIF(LTRIM(RTRIM(m.DisplayName)), N'')
                FROM dbo.PhotoSubmissions ps
                INNER JOIN dbo.MemberAccounts m ON m.Id = ps.SubmitterMemberId
                WHERE ps.PromotedPicId = p.PIC_ID
                  AND m.DisplayName IS NOT NULL
                ORDER BY ps.ReviewedAt DESC, ps.Id
            ),
            NULLIF(LTRIM(RTRIM(u.USERNAME)), N'')
        )
        """;

    private const string SubmittedByPlaceholder = "__SUBMITTED_BY_DISPLAY_NAME__";

    private static string EmbedSubmittedBy(string sql) =>
        sql.Replace(SubmittedByPlaceholder, SubmittedByDisplayNameSql, StringComparison.Ordinal);

    /// <summary>
    /// Adds neighbor file path and dimensions in the same round-trip by looking
    /// up the already-resolved prev/next pic ids. Production uses SQL Server
    /// PIC_FILES_T; the SQLite fixture uses PhotoItems.
    /// </summary>
    private const string InnerNavSqlPlaceholder = "__INNER_NAV_SQL__";

    private static string WithNeighborMedia(string innerSql, bool sqlite)
    {
        // Regular raw strings keep EF `{0}` placeholders literal. Interpolated
        // raw strings (`$""" {{0}}`) fail CS9006 because `{` starts interpolation.
        var wrapper = sqlite
            ? """
                SELECT
                    nav.*,
                    (SELECT URL FROM PhotoItems t WHERE t.cat_id = {0} AND t.pic_id = nav.PreviousPicId) AS PreviousUrl,
                    (SELECT PIC_WIDTH FROM PhotoItems t WHERE t.cat_id = {0} AND t.pic_id = nav.PreviousPicId) AS PreviousWidth,
                    (SELECT PIC_HEIGHT FROM PhotoItems t WHERE t.cat_id = {0} AND t.pic_id = nav.PreviousPicId) AS PreviousHeight,
                    (SELECT URL FROM PhotoItems t WHERE t.cat_id = {0} AND t.pic_id = nav.NextPicId) AS NextUrl,
                    (SELECT PIC_WIDTH FROM PhotoItems t WHERE t.cat_id = {0} AND t.pic_id = nav.NextPicId) AS NextWidth,
                    (SELECT PIC_HEIGHT FROM PhotoItems t WHERE t.cat_id = {0} AND t.pic_id = nav.NextPicId) AS NextHeight
                FROM (
                __INNER_NAV_SQL__
                ) nav
                """
            : """
                SELECT
                    nav.*,
                    (
                        SELECT ISNULL(t.Url, N'')
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1 AND t.PIC_ID = nav.PreviousPicId
                    ) AS PreviousUrl,
                    (
                        SELECT CAST(ISNULL(t.PIC_WIDTH, 0) AS int)
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1 AND t.PIC_ID = nav.PreviousPicId
                    ) AS PreviousWidth,
                    (
                        SELECT CAST(ISNULL(t.PIC_HEIGHT, 0) AS int)
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1 AND t.PIC_ID = nav.PreviousPicId
                    ) AS PreviousHeight,
                    (
                        SELECT ISNULL(t.Url, N'')
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1 AND t.PIC_ID = nav.NextPicId
                    ) AS NextUrl,
                    (
                        SELECT CAST(ISNULL(t.PIC_WIDTH, 0) AS int)
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1 AND t.PIC_ID = nav.NextPicId
                    ) AS NextWidth,
                    (
                        SELECT CAST(ISNULL(t.PIC_HEIGHT, 0) AS int)
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1 AND t.PIC_ID = nav.NextPicId
                    ) AS NextHeight
                FROM (
                __INNER_NAV_SQL__
                ) nav
                """;
        return wrapper.Replace(InnerNavSqlPlaceholder, innerSql, StringComparison.Ordinal);
    }

    public required string CategoriesWithCountsSql { get; init; }

    /// <summary>Parameters: offset, pageSize, catId.</summary>
    public required string CategoryPageSql { get; init; }

    /// <summary>Parameter: catId. Returns scalar count as <c>Value</c>.</summary>
    public required string CategoryCountSql { get; init; }

    /// <summary>Parameter: catId. Returns <c>name</c>.</summary>
    public required string CategoryNameSql { get; init; }

    /// <summary>Parameters: catId, picId.</summary>
    public required string PhotoByIdSql { get; init; }

    /// <summary>
    /// Parameters: catId, picId. Single round-trip for detail navigation:
    /// photo fields + TotalCount + IndexBefore + PreviousPicId + NextPicId
    /// plus neighbor file path and original dimensions.
    /// </summary>
    public required string DetailNavigationSql { get; init; }

    /// <summary>Parameters: catId, dateTime, picId — neighbor toward newer (list index - 1).</summary>
    public required string PreviousPicIdSql { get; init; }

    /// <summary>Parameters: catId, dateTime, picId — neighbor toward older (list index + 1).</summary>
    public required string NextPicIdSql { get; init; }

    /// <summary>Parameters: catId, dateTime, picId — count of photos before current in DESC order.</summary>
    public required string IndexBeforeSql { get; init; }

    /// <summary>Light sitemap rows: cat_id, category_name, pic_id, date_time.</summary>
    public required string SitemapSql { get; init; }

    /// <summary>Parameter: catId. Full visible collection for tools (not detail pages).</summary>
    public required string CategoryAllSql { get; init; }

    /// <summary>Parameter: catId. Inclusive published pic-id bounds for an indexed seek.</summary>
    public required string RandomIdBoundsSql { get; init; }

    /// <summary>Parameters: catId, targetPicId. Next published pic id at or after the target.</summary>
    public required string RandomIdSeekAtOrAfterSql { get; init; }

    /// <summary>Parameters: catId, targetPicId. Published pic id before the target (range wrap).</summary>
    public required string RandomIdSeekBeforeSql { get; init; }

    /// <summary>
    /// Parameter: catId. <c>{ID_LIST}</c> is replaced with a comma-separated list of positive pic ids.
    /// </summary>
    public required string PublishedByIdsSql { get; init; }

    /// <summary>Parameters: catId, picId. Original width and height for one displayed photo.</summary>
    public required string PhotoDimensionsSql { get; init; }

    /// <summary>
    /// When true, <see cref="ApplyFilter"/> uses SQLite IFNULL expressions; otherwise SQL Server CAST/ISNULL.
    /// </summary>
    public bool UseSqliteFilterExpressions { get; init; }

    public string ApplyFilter(string sql, PhotoListFilter? filter) =>
        UseSqliteFilterExpressions
            ? PhotoSqlFilter.ApplySqlite(sql, filter)
            : PhotoSqlFilter.ApplyProduction(sql, filter);

    /// <summary>
    /// Substitutes <c>{ID_LIST}</c> with the given positive pic ids.
    /// Ids are integers produced by this repository, not caller text.
    /// </summary>
    public static string ApplyIdList(string sql, IReadOnlyList<int> picIds)
    {
        if (picIds.Count == 0)
        {
            throw new ArgumentException("At least one photo id is required.", nameof(picIds));
        }

        foreach (var picId in picIds)
        {
            if (picId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(picIds), picId, "Photo ids must be positive.");
            }
        }

        return sql.Replace("{ID_LIST}", string.Join(", ", picIds), StringComparison.Ordinal);
    }

    public static PhotoSqlQueries CreateProduction() =>
        new()
        {
            UseSqliteFilterExpressions = false,
            CategoriesWithCountsSql = """
                SELECT
                    c.cat_id,
                    c.name,
                    COUNT(p.PIC_ID) AS ImageCount,
                    (
                        SELECT TOP (1) p2.Thumb_URL
                        FROM dbo.PIC_FILES_T p2
                        WHERE p2.Cat_ID = c.cat_id AND p2.DISPLAY = 1
                        ORDER BY p2.Date_time DESC, p2.PIC_ID DESC
                    ) AS CoverThumbUrl
                FROM dbo.PIC_CAT_T c
                INNER JOIN dbo.PIC_FILES_T p ON p.Cat_ID = c.cat_id AND p.DISPLAY = 1
                GROUP BY c.cat_id, c.name
                HAVING COUNT(p.PIC_ID) > 0
                ORDER BY c.name
                """,
            CategoryPageSql = """
                SELECT
                    ISNULL(p.Name, N'') AS NAME,
                    p.Date_time AS DATE_TIME,
                    ISNULL(p.Url, N'') AS URL,
                    ISNULL(p.Thumb_URL, N'') AS THUMB_URL,
                    ISNULL(p.t_height, 0) AS T_HEIGHT,
                    ISNULL(p.t_width, 0) AS T_WIDTH,
                    CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH,
                    CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT,
                    p.PIC_ID AS pic_id,
                    ISNULL(c.name, N'') AS category_name
                FROM dbo.PIC_FILES_T p
                INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
                WHERE p.Cat_ID = {2} AND p.DISPLAY = 1{PHOTO_FILTER_P}
                ORDER BY p.Date_time DESC, p.PIC_ID DESC
                OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY
                """,
            CategoryCountSql = """
                SELECT COUNT(*) AS Value
                FROM dbo.PIC_FILES_T p
                WHERE p.Cat_ID = {0} AND p.DISPLAY = 1{PHOTO_FILTER_P}
                """,
            CategoryNameSql = """
                SELECT name
                FROM dbo.PIC_CAT_T
                WHERE cat_id = {0}
                """,
            PhotoByIdSql = EmbedSubmittedBy("""
                SELECT
                    ISNULL(p.Name, N'') AS NAME,
                    p.Date_time AS DATE_TIME,
                    ISNULL(p.Url, N'') AS URL,
                    ISNULL(p.Thumb_URL, N'') AS THUMB_URL,
                    ISNULL(p.t_height, 0) AS T_HEIGHT,
                    ISNULL(p.t_width, 0) AS T_WIDTH,
                    CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH,
                    CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT,
                    p.PIC_ID AS pic_id,
                    ISNULL(c.name, N'') AS category_name,
                    __SUBMITTED_BY_DISPLAY_NAME__ AS submitted_by_display_name
                FROM dbo.PIC_FILES_T p
                INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
                LEFT JOIN dbo.USERS_T u ON u.USER_ID = p.user_id
                WHERE p.Cat_ID = {0} AND p.PIC_ID = {1} AND p.DISPLAY = 1{PHOTO_FILTER_P}
                """),
            // One round-trip: photo + totals + neighbors. Neighbor subqueries mirror Previous/NextPicIdSql
            // (seek-friendly UNION ALL; relies on IX_PIC_FILES_T_Cat_Display_Date).
            // {PHOTO_FILTER_*} placeholders are filled by PhotoSqlFilter for size presets (#437).
            // Submitted-by: linked modern member, else promoted PhotoSubmissions submitter, else legacy username.
            DetailNavigationSql = WithNeighborMedia(EmbedSubmittedBy("""
                SELECT
                    ISNULL(p.Name, N'') AS NAME,
                    p.Date_time AS DATE_TIME,
                    ISNULL(p.Url, N'') AS URL,
                    ISNULL(p.Thumb_URL, N'') AS THUMB_URL,
                    ISNULL(p.t_height, 0) AS T_HEIGHT,
                    ISNULL(p.t_width, 0) AS T_WIDTH,
                    CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH,
                    CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT,
                    p.PIC_ID AS pic_id,
                    ISNULL(c.name, N'') AS category_name,
                    __SUBMITTED_BY_DISPLAY_NAME__ AS submitted_by_display_name,
                    (
                        SELECT COUNT(*)
                        FROM dbo.PIC_FILES_T t
                        WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T}
                    ) AS TotalCount,
                    (
                        (SELECT COUNT(*)
                         FROM dbo.PIC_FILES_T t
                         WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T} AND t.Date_time > p.Date_time)
                        +
                        (SELECT COUNT(*)
                         FROM dbo.PIC_FILES_T t
                         WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T} AND t.Date_time = p.Date_time AND t.PIC_ID > p.PIC_ID)
                    ) AS IndexBefore,
                    (
                        SELECT TOP (1) PIC_ID
                        FROM (
                            SELECT PIC_ID, Date_time
                            FROM (
                                SELECT TOP (1) PIC_ID, Date_time
                                FROM dbo.PIC_FILES_T t
                                WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T} AND t.Date_time = p.Date_time AND t.PIC_ID > p.PIC_ID
                                ORDER BY t.PIC_ID ASC
                            ) same_ts
                            UNION ALL
                            SELECT PIC_ID, Date_time
                            FROM (
                                SELECT TOP (1) PIC_ID, Date_time
                                FROM dbo.PIC_FILES_T t
                                WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T} AND t.Date_time > p.Date_time
                                ORDER BY t.Date_time ASC, t.PIC_ID ASC
                            ) newer
                        ) neighbors
                        ORDER BY Date_time ASC, PIC_ID ASC
                    ) AS PreviousPicId,
                    (
                        SELECT TOP (1) PIC_ID
                        FROM (
                            SELECT PIC_ID, Date_time
                            FROM (
                                SELECT TOP (1) PIC_ID, Date_time
                                FROM dbo.PIC_FILES_T t
                                WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T} AND t.Date_time = p.Date_time AND t.PIC_ID < p.PIC_ID
                                ORDER BY t.PIC_ID DESC
                            ) same_ts
                            UNION ALL
                            SELECT PIC_ID, Date_time
                            FROM (
                                SELECT TOP (1) PIC_ID, Date_time
                                FROM dbo.PIC_FILES_T t
                                WHERE t.Cat_ID = {0} AND t.DISPLAY = 1{PHOTO_FILTER_T} AND t.Date_time < p.Date_time
                                ORDER BY t.Date_time DESC, t.PIC_ID DESC
                            ) older
                        ) neighbors
                        ORDER BY Date_time DESC, PIC_ID DESC
                    ) AS NextPicId
                FROM dbo.PIC_FILES_T p
                INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
                LEFT JOIN dbo.USERS_T u ON u.USER_ID = p.user_id
                WHERE p.Cat_ID = {0} AND p.PIC_ID = {1} AND p.DISPLAY = 1{PHOTO_FILTER_P}
                """), sqlite: false),
            // Seek-friendly UNION ALL (avoids OR residual predicates that scan the whole category).
            // Relies on IX_PIC_FILES_T_Cat_Display_Date (Cat_ID, DISPLAY, Date_time DESC, PIC_ID DESC).
            PreviousPicIdSql = """
                SELECT TOP (1) PIC_ID AS Value
                FROM (
                    SELECT PIC_ID, Date_time
                    FROM (
                        SELECT TOP (1) PIC_ID, Date_time
                        FROM dbo.PIC_FILES_T
                        WHERE Cat_ID = {0} AND DISPLAY = 1 AND Date_time = {1} AND PIC_ID > {2}
                        ORDER BY PIC_ID ASC
                    ) same_ts
                    UNION ALL
                    SELECT PIC_ID, Date_time
                    FROM (
                        SELECT TOP (1) PIC_ID, Date_time
                        FROM dbo.PIC_FILES_T
                        WHERE Cat_ID = {0} AND DISPLAY = 1 AND Date_time > {1}
                        ORDER BY Date_time ASC, PIC_ID ASC
                    ) newer
                ) neighbors
                ORDER BY Date_time ASC, PIC_ID ASC
                """,
            NextPicIdSql = """
                SELECT TOP (1) PIC_ID AS Value
                FROM (
                    SELECT PIC_ID, Date_time
                    FROM (
                        SELECT TOP (1) PIC_ID, Date_time
                        FROM dbo.PIC_FILES_T
                        WHERE Cat_ID = {0} AND DISPLAY = 1 AND Date_time = {1} AND PIC_ID < {2}
                        ORDER BY PIC_ID DESC
                    ) same_ts
                    UNION ALL
                    SELECT PIC_ID, Date_time
                    FROM (
                        SELECT TOP (1) PIC_ID, Date_time
                        FROM dbo.PIC_FILES_T
                        WHERE Cat_ID = {0} AND DISPLAY = 1 AND Date_time < {1}
                        ORDER BY Date_time DESC, PIC_ID DESC
                    ) older
                ) neighbors
                ORDER BY Date_time DESC, PIC_ID DESC
                """,
            IndexBeforeSql = """
                SELECT
                    (SELECT COUNT(*)
                     FROM dbo.PIC_FILES_T
                     WHERE Cat_ID = {0} AND DISPLAY = 1 AND Date_time > {1})
                    +
                    (SELECT COUNT(*)
                     FROM dbo.PIC_FILES_T
                     WHERE Cat_ID = {0} AND DISPLAY = 1 AND Date_time = {1} AND PIC_ID > {2})
                    AS Value
                """,
            SitemapSql = """
                SELECT
                    c.cat_id,
                    c.name AS category_name,
                    p.PIC_ID AS pic_id,
                    p.Date_time AS date_time
                FROM dbo.PIC_FILES_T p
                INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
                WHERE p.DISPLAY = 1
                ORDER BY c.name, p.Date_time DESC, p.PIC_ID DESC
                """,
            CategoryAllSql = """
                SELECT
                    ISNULL(p.Name, N'') AS NAME,
                    p.Date_time AS DATE_TIME,
                    ISNULL(p.Url, N'') AS URL,
                    ISNULL(p.Thumb_URL, N'') AS THUMB_URL,
                    ISNULL(p.t_height, 0) AS T_HEIGHT,
                    ISNULL(p.t_width, 0) AS T_WIDTH,
                    CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH,
                    CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT,
                    p.PIC_ID AS pic_id,
                    ISNULL(c.name, N'') AS category_name
                FROM dbo.PIC_FILES_T p
                INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
                WHERE p.Cat_ID = {0} AND p.DISPLAY = 1{PHOTO_FILTER_P}
                ORDER BY p.Date_time DESC, p.PIC_ID DESC
                """,
            RandomIdBoundsSql = """
                SELECT MIN(p.PIC_ID) AS MinId, MAX(p.PIC_ID) AS MaxId
                FROM dbo.PIC_FILES_T p
                WHERE p.Cat_ID = {0} AND p.DISPLAY = 1
                """,
            RandomIdSeekAtOrAfterSql = """
                SELECT TOP (1) p.PIC_ID AS Value
                FROM dbo.PIC_FILES_T p
                WHERE p.Cat_ID = {0} AND p.DISPLAY = 1 AND p.PIC_ID >= {1}
                ORDER BY p.PIC_ID
                """,
            RandomIdSeekBeforeSql = """
                SELECT TOP (1) p.PIC_ID AS Value
                FROM dbo.PIC_FILES_T p
                WHERE p.Cat_ID = {0} AND p.DISPLAY = 1 AND p.PIC_ID < {1}
                ORDER BY p.PIC_ID DESC
                """,
            PublishedByIdsSql = """
                SELECT
                    ISNULL(p.Name, N'') AS NAME,
                    p.Date_time AS DATE_TIME,
                    ISNULL(p.Url, N'') AS URL,
                    ISNULL(p.Thumb_URL, N'') AS THUMB_URL,
                    ISNULL(p.t_height, 0) AS T_HEIGHT,
                    ISNULL(p.t_width, 0) AS T_WIDTH,
                    CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH,
                    CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT,
                    p.PIC_ID AS pic_id,
                    ISNULL(c.name, N'') AS category_name
                FROM dbo.PIC_FILES_T p
                INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
                WHERE p.Cat_ID = {0} AND p.DISPLAY = 1 AND p.PIC_ID IN ({ID_LIST})
                """,
            PhotoDimensionsSql = """
                SELECT
                    CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH,
                    CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT
                FROM dbo.PIC_FILES_T p
                WHERE p.Cat_ID = {0} AND p.PIC_ID = {1} AND p.DISPLAY = 1
                """,
        };

    /// <summary>
    /// SQLite fixture shapes used by <c>EfPublicReadRepositoryTests</c>
    /// (<c>PhotoCategories</c> / <c>PhotoItems</c> tables).
    /// </summary>
    public static PhotoSqlQueries CreateSqliteFixture() =>
        new()
        {
            UseSqliteFilterExpressions = true,
            CategoriesWithCountsSql = """
                SELECT
                    c.cat_id,
                    c.name,
                    COUNT(p.pic_id) AS ImageCount,
                    (
                        SELECT p2.THUMB_URL
                        FROM PhotoItems p2
                        WHERE p2.cat_id = c.cat_id
                        ORDER BY p2.DATE_TIME DESC, p2.pic_id DESC
                        LIMIT 1
                    ) AS CoverThumbUrl
                FROM PhotoCategories c
                INNER JOIN PhotoItems p ON p.cat_id = c.cat_id
                GROUP BY c.cat_id, c.name
                HAVING COUNT(p.pic_id) > 0
                ORDER BY c.name
                """,
            CategoryPageSql = """
                SELECT NAME, DATE_TIME, URL, THUMB_URL, T_HEIGHT, T_WIDTH, PIC_WIDTH, PIC_HEIGHT, pic_id, category_name
                FROM PhotoItems p
                WHERE p.cat_id = {2}{PHOTO_FILTER_P}
                ORDER BY DATE_TIME DESC, pic_id DESC
                LIMIT {1} OFFSET {0}
                """,
            CategoryCountSql = """
                SELECT COUNT(*) AS Value FROM PhotoItems p WHERE p.cat_id = {0}{PHOTO_FILTER_P}
                """,
            CategoryNameSql = """
                SELECT name FROM PhotoCategories WHERE cat_id = {0}
                """,
            PhotoByIdSql = """
                SELECT NAME, DATE_TIME, URL, THUMB_URL, T_HEIGHT, T_WIDTH, PIC_WIDTH, PIC_HEIGHT, pic_id, category_name, submitted_by_display_name
                FROM PhotoItems p
                WHERE p.cat_id = {0} AND p.pic_id = {1}{PHOTO_FILTER_P}
                """,
            DetailNavigationSql = WithNeighborMedia("""
                SELECT
                    p.NAME,
                    p.DATE_TIME,
                    p.URL,
                    p.THUMB_URL,
                    p.T_HEIGHT,
                    p.T_WIDTH,
                    p.PIC_WIDTH,
                    p.PIC_HEIGHT,
                    p.pic_id,
                    p.category_name,
                    p.submitted_by_display_name,
                    (SELECT COUNT(*) FROM PhotoItems t WHERE t.cat_id = {0}{PHOTO_FILTER_T}) AS TotalCount,
                    (
                        (SELECT COUNT(*) FROM PhotoItems t WHERE t.cat_id = {0}{PHOTO_FILTER_T} AND t.DATE_TIME > p.DATE_TIME)
                        +
                        (SELECT COUNT(*) FROM PhotoItems t WHERE t.cat_id = {0}{PHOTO_FILTER_T} AND t.DATE_TIME = p.DATE_TIME AND t.pic_id > p.pic_id)
                    ) AS IndexBefore,
                    (
                        SELECT pic_id
                        FROM (
                            SELECT pic_id, DATE_TIME
                            FROM (
                                SELECT pic_id, DATE_TIME
                                FROM PhotoItems t
                                WHERE t.cat_id = {0}{PHOTO_FILTER_T} AND t.DATE_TIME = p.DATE_TIME AND t.pic_id > p.pic_id
                                ORDER BY t.pic_id ASC
                                LIMIT 1
                            ) same_ts
                            UNION ALL
                            SELECT pic_id, DATE_TIME
                            FROM (
                                SELECT pic_id, DATE_TIME
                                FROM PhotoItems t
                                WHERE t.cat_id = {0}{PHOTO_FILTER_T} AND t.DATE_TIME > p.DATE_TIME
                                ORDER BY t.DATE_TIME ASC, t.pic_id ASC
                                LIMIT 1
                            ) newer
                        ) neighbors
                        ORDER BY DATE_TIME ASC, pic_id ASC
                        LIMIT 1
                    ) AS PreviousPicId,
                    (
                        SELECT pic_id
                        FROM (
                            SELECT pic_id, DATE_TIME
                            FROM (
                                SELECT pic_id, DATE_TIME
                                FROM PhotoItems t
                                WHERE t.cat_id = {0}{PHOTO_FILTER_T} AND t.DATE_TIME = p.DATE_TIME AND t.pic_id < p.pic_id
                                ORDER BY t.pic_id DESC
                                LIMIT 1
                            ) same_ts
                            UNION ALL
                            SELECT pic_id, DATE_TIME
                            FROM (
                                SELECT pic_id, DATE_TIME
                                FROM PhotoItems t
                                WHERE t.cat_id = {0}{PHOTO_FILTER_T} AND t.DATE_TIME < p.DATE_TIME
                                ORDER BY t.DATE_TIME DESC, t.pic_id DESC
                                LIMIT 1
                            ) older
                        ) neighbors
                        ORDER BY DATE_TIME DESC, pic_id DESC
                        LIMIT 1
                    ) AS NextPicId
                FROM PhotoItems p
                WHERE p.cat_id = {0} AND p.pic_id = {1}{PHOTO_FILTER_P}
                """, sqlite: true),
            PreviousPicIdSql = """
                SELECT pic_id AS Value
                FROM (
                    SELECT pic_id, DATE_TIME
                    FROM (
                        SELECT pic_id, DATE_TIME
                        FROM PhotoItems
                        WHERE cat_id = {0} AND DATE_TIME = {1} AND pic_id > {2}
                        ORDER BY pic_id ASC
                        LIMIT 1
                    ) same_ts
                    UNION ALL
                    SELECT pic_id, DATE_TIME
                    FROM (
                        SELECT pic_id, DATE_TIME
                        FROM PhotoItems
                        WHERE cat_id = {0} AND DATE_TIME > {1}
                        ORDER BY DATE_TIME ASC, pic_id ASC
                        LIMIT 1
                    ) newer
                ) neighbors
                ORDER BY DATE_TIME ASC, pic_id ASC
                LIMIT 1
                """,
            NextPicIdSql = """
                SELECT pic_id AS Value
                FROM (
                    SELECT pic_id, DATE_TIME
                    FROM (
                        SELECT pic_id, DATE_TIME
                        FROM PhotoItems
                        WHERE cat_id = {0} AND DATE_TIME = {1} AND pic_id < {2}
                        ORDER BY pic_id DESC
                        LIMIT 1
                    ) same_ts
                    UNION ALL
                    SELECT pic_id, DATE_TIME
                    FROM (
                        SELECT pic_id, DATE_TIME
                        FROM PhotoItems
                        WHERE cat_id = {0} AND DATE_TIME < {1}
                        ORDER BY DATE_TIME DESC, pic_id DESC
                        LIMIT 1
                    ) older
                ) neighbors
                ORDER BY DATE_TIME DESC, pic_id DESC
                LIMIT 1
                """,
            IndexBeforeSql = """
                SELECT
                    (SELECT COUNT(*) FROM PhotoItems WHERE cat_id = {0} AND DATE_TIME > {1})
                    +
                    (SELECT COUNT(*) FROM PhotoItems WHERE cat_id = {0} AND DATE_TIME = {1} AND pic_id > {2})
                    AS Value
                """,
            SitemapSql = """
                SELECT
                    c.cat_id,
                    c.name AS category_name,
                    p.pic_id,
                    p.DATE_TIME AS date_time
                FROM PhotoItems p
                INNER JOIN PhotoCategories c ON c.cat_id = p.cat_id
                ORDER BY c.name, p.DATE_TIME DESC, p.pic_id DESC
                """,
            CategoryAllSql = """
                SELECT NAME, DATE_TIME, URL, THUMB_URL, T_HEIGHT, T_WIDTH, PIC_WIDTH, PIC_HEIGHT, pic_id, category_name
                FROM PhotoItems p
                WHERE p.cat_id = {0}{PHOTO_FILTER_P}
                ORDER BY DATE_TIME DESC, pic_id DESC
                """,
            RandomIdBoundsSql = """
                SELECT MIN(p.pic_id) AS MinId, MAX(p.pic_id) AS MaxId
                FROM PhotoItems p
                WHERE p.cat_id = {0}
                """,
            RandomIdSeekAtOrAfterSql = """
                SELECT p.pic_id AS Value
                FROM PhotoItems p
                WHERE p.cat_id = {0} AND p.pic_id >= {1}
                ORDER BY p.pic_id
                LIMIT 1
                """,
            RandomIdSeekBeforeSql = """
                SELECT p.pic_id AS Value
                FROM PhotoItems p
                WHERE p.cat_id = {0} AND p.pic_id < {1}
                ORDER BY p.pic_id DESC
                LIMIT 1
                """,
            PublishedByIdsSql = """
                SELECT NAME, DATE_TIME, URL, THUMB_URL, T_HEIGHT, T_WIDTH, PIC_WIDTH, PIC_HEIGHT, pic_id, category_name
                FROM PhotoItems p
                WHERE p.cat_id = {0} AND p.pic_id IN ({ID_LIST})
                """,
            PhotoDimensionsSql = """
                SELECT PIC_WIDTH, PIC_HEIGHT
                FROM PhotoItems p
                WHERE p.cat_id = {0} AND p.pic_id = {1}
                """,
        };
}

