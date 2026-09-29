namespace QueenZone.SqlServerTests;

/// <summary>
/// Legacy links tables copied from the <c>queenzone_legacy_sync</c> read-only dump of
/// 2026-09-29 (#1672 / #1889). <c>Q_LINK_CAT_T.Q_LINK_CAT_ID</c> is <c>smallint</c>
/// identity; <c>QUEEN_FEATURED_SITE_T.Q_LINK_CAT_ID</c> is <c>tinyint</c> with no FK.
/// Legacy strings are <c>varchar</c> <c>SQL_Latin1_General_CP1_CI_AS</c>;
/// <c>QueenLinkChecks</c> is the modern <c>nvarchar</c> table (present on the
/// mirror). Shared by <see cref="LinksRepositorySqlServerTests"/>.
/// </summary>
internal static class LegacyLinksSchema
{
    public const string CreateLegacyTablesSql = """
        CREATE TABLE dbo.Q_LINK_CAT_T
        (
            Q_LINK_CAT_ID smallint IDENTITY(1,1) NOT NULL,
            CAT_NAME varchar(25) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT PK_Q_LINK_CAT_T PRIMARY KEY CLUSTERED (Q_LINK_CAT_ID)
        );

        CREATE TABLE dbo.QUEEN_FEATURED_SITE_T
        (
            QUEEN_FEATURED_SITE_ID int IDENTITY(1,1) NOT NULL,
            QUEEN_FEATURED_SITE_TITLE varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            QUEEN_FEATURED_SITE_URL varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            SITE_COMMENT varchar(400) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            Q_LINK_CAT_ID tinyint NOT NULL
                CONSTRAINT DF_QUEEN_FEATURED_SITE_T_Q_LINK_CAT_ID DEFAULT ((1)),
            FEATURED_SITE tinyint NOT NULL
                CONSTRAINT DF_QUEEN_FEATURED_SITE_T_FEATURED_SITE DEFAULT ((1)),
            USER_ID int NULL,
            CREATE_DATE smalldatetime NOT NULL
                CONSTRAINT DF_QUEEN_FEATURED_SITE_T_CREATE_DATE DEFAULT (getdate()),
            DISPLAY tinyint NOT NULL
                CONSTRAINT DF_QUEEN_FEATURED_SITE_T_DISPLAY DEFAULT ((1)),
            CONSTRAINT PK_QUEEN_FEATURED_SITE_T PRIMARY KEY CLUSTERED (QUEEN_FEATURED_SITE_ID)
        );
        """;

    public const string CreateLinkChecksSql = """
        CREATE TABLE dbo.QueenLinkChecks
        (
            QueenFeaturedSiteId int NOT NULL,
            Url nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            LastCheckedAtUtc datetime2 NOT NULL,
            IsAvailable bit NOT NULL,
            IsConfirmedDead bit NOT NULL,
            ConsecutiveFailureCount int NOT NULL,
            LastStatusCode int NULL,
            LastError nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT PK_QueenLinkChecks PRIMARY KEY CLUSTERED (QueenFeaturedSiteId)
        );

        CREATE NONCLUSTERED INDEX IX_QueenLinkChecks_IsConfirmedDead
            ON dbo.QueenLinkChecks (IsConfirmedDead);

        CREATE NONCLUSTERED INDEX IX_QueenLinkChecks_LastCheckedAtUtc
            ON dbo.QueenLinkChecks (LastCheckedAtUtc);
        """;
}
