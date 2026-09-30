namespace QueenZone.SqlServerTests;

/// <summary>
/// Legacy <c>NEWS_T</c> copied from the <c>queenzone_legacy_sync</c> read-only dump of
/// 2026-09-30 (#1672 / #1882). Public archive SQL is ad-hoc (latest-row CTE); search uses
/// <c>dbo.NEWS_T_SearchPublished</c> and is covered by <see cref="NewsSearchSqlServerTests"/>.
/// The clustered <c>PK_NEWS_T</c> is omitted so duplicate <c>NEWS_ID</c> rows can exercise the
/// date-desc <c>ROW_NUMBER</c> dedupe the production CTE still applies.
/// </summary>
internal static class LegacyNewsArchiveSchema
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.NEWS_T
        (
            NEWS_ID int IDENTITY(1,1) NOT NULL,
            TITLE varchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            EXCERPT varchar(800) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            ARTICLE varchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            [DATE] smalldatetime NOT NULL
                CONSTRAINT DF_NEWS_T_DATE DEFAULT (getdate()),
            USER_ID int NULL,
            DISPLAY tinyint NOT NULL
                CONSTRAINT [DF_2_5_2004_5_22_6_319_DISPLAY] DEFAULT ((0)),
            TYPE int NULL,
            QUEEN_ONLINE tinyint NULL
                CONSTRAINT DF_NEWS_T_QUEEN_ONLINE DEFAULT ((0)),
            SOURCE_URL varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            SLUG nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CREATED_AT datetime2 NULL,
            UPDATED_AT datetime2 NULL,
            EDITOR_EMAIL nvarchar(256) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            IMAGE_BLOB_KEY nvarchar(512) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            IMAGE_GALLERY_PIC_ID int NULL,
            FORUM_TOPIC_ID int NULL
        );
        """;

    /// <summary>
    /// Core legacy columns only. Used to prove <see cref="QueenZone.Data.LegacyNewsSchema"/>
    /// COL_LENGTH probes report the eight optional columns as missing.
    /// </summary>
    public const string CreateMinimalTableSql = """
        CREATE TABLE dbo.NEWS_T
        (
            NEWS_ID int NOT NULL,
            TITLE varchar(150) NULL,
            EXCERPT varchar(800) NULL,
            ARTICLE varchar(max) NULL,
            [DATE] smalldatetime NOT NULL,
            DISPLAY tinyint NOT NULL
        );
        """;
}
