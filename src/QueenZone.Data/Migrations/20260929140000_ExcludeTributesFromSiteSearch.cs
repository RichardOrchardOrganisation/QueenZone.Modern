using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Removes Freddie tribute rows from <c>SearchDocument</c> and rewrites
/// <c>dbo.SearchDocument_Search</c>: tributes never enter <c>#Matches</c>, the temp
/// table carries order columns, count reads <c>#Matches</c> only, and the page
/// materializes through a small <c>#Page</c> before the display join. No index changes.
/// Rank caps and <c>OPTION (RECOMPILE)</c> are unchanged.
/// </summary>
/// <remarks>
/// Procedure body source of truth: <c>docs/sql/010-search-document-full-text-search.sql</c>.
/// No EF model change.
/// </remarks>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260929140000_ExcludeTributesFromSiteSearch")]
public partial class ExcludeTributesFromSiteSearch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            IF OBJECT_ID(N'dbo.SearchDocument', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.SearchDocument
                WHERE {SiteSearchExclusion.SqlIsExcluded("ContentType", "SourceKey")};
            END
            """);

        migrationBuilder.Sql($"""
            CREATE OR ALTER PROCEDURE dbo.SearchDocument_Search
                @Query          NVARCHAR(500),
                @ContentType    NVARCHAR(50) = NULL,
                @Offset         INT,
                @PageSize       INT,
                @TotalRecords   INT OUTPUT,
                @RankLimit      INT = 1000,
                @TypedRankLimit INT = 5000
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @MatchLimit INT = CASE
                    WHEN @RankLimit IS NULL OR @RankLimit < 1 THEN 1000
                    WHEN @RankLimit > 1000 THEN 1000
                    ELSE @RankLimit
                END;

                DECLARE @TypedMatchLimit INT = CASE
                    WHEN @TypedRankLimit IS NULL OR @TypedRankLimit < 1 THEN 5000
                    WHEN @TypedRankLimit > 5000 THEN 5000
                    ELSE @TypedRankLimit
                END;

                CREATE TABLE #Matches
                (
                    DocumentId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    SearchRank INT NOT NULL,
                    ContentType NVARCHAR(50) NOT NULL,
                    PublishedAt DATETIMEOFFSET NULL
                );

                IF @ContentType IS NULL
                BEGIN
                    INSERT INTO #Matches (DocumentId, SearchRank, ContentType, PublishedAt)
                    SELECT ft.[KEY], ft.[RANK], d.ContentType, d.PublishedAt
                    FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit) ft
                    INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]
                    WHERE  {SiteSearchExclusion.SqlIsSearchable("d")}
                    OPTION (RECOMPILE);
            END
            ELSE
            BEGIN
                INSERT INTO #Matches (DocumentId, SearchRank, ContentType, PublishedAt)
                SELECT TOP (@MatchLimit)
                       d.Id,
                       ft.[RANK],
                       d.ContentType,
                       d.PublishedAt
                FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit) ft
                INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]
                WHERE  d.ContentType = @ContentType
                  AND  {SiteSearchExclusion.SqlIsSearchable("d")}
                ORDER BY ft.[RANK] DESC, d.PublishedAt DESC, d.Id DESC
                OPTION (RECOMPILE);
            END

                CREATE TABLE #Page
                (
                    DocumentId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    SearchRank INT NOT NULL,
                    PublishedAt DATETIMEOFFSET NULL
                );

                INSERT INTO #Page (DocumentId, SearchRank, PublishedAt)
                SELECT DocumentId, SearchRank, PublishedAt
                FROM   #Matches
                ORDER BY SearchRank DESC, PublishedAt DESC, DocumentId DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT
                    d.ContentType,
                    d.SourceKey,
                    d.Title,
                    d.Summary,
                    d.Url,
                    d.PublishedAt,
                    d.ImageUrl,
                    d.Category,
                    d.AuthorDisplayName
                FROM   #Page p
                INNER JOIN dbo.SearchDocument d ON d.Id = p.DocumentId
                ORDER BY p.SearchRank DESC, p.PublishedAt DESC, p.DocumentId DESC;

                SELECT @TotalRecords = COUNT(*)
                FROM   #Matches;
            END;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.SearchDocument_Search
                @Query          NVARCHAR(500),
                @ContentType    NVARCHAR(50) = NULL,
                @Offset         INT,
                @PageSize       INT,
                @TotalRecords   INT OUTPUT,
                @RankLimit      INT = 1000,
                @TypedRankLimit INT = 5000
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @MatchLimit INT = CASE
                    WHEN @RankLimit IS NULL OR @RankLimit < 1 THEN 1000
                    WHEN @RankLimit > 1000 THEN 1000
                    ELSE @RankLimit
                END;

                DECLARE @TypedMatchLimit INT = CASE
                    WHEN @TypedRankLimit IS NULL OR @TypedRankLimit < 1 THEN 5000
                    WHEN @TypedRankLimit > 5000 THEN 5000
                    ELSE @TypedRankLimit
                END;

                CREATE TABLE #Matches
                (
                    DocumentId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    SearchRank INT NOT NULL
                );

                IF @ContentType IS NULL
                BEGIN
                    INSERT INTO #Matches (DocumentId, SearchRank)
                    SELECT ft.[KEY], ft.[RANK]
                    FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit) ft
                    OPTION (RECOMPILE);
                END
                ELSE
                BEGIN
                    INSERT INTO #Matches (DocumentId, SearchRank)
                    SELECT TOP (@MatchLimit)
                           d.Id,
                           ft.[RANK]
                    FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit) ft
                    INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]
                    WHERE  d.ContentType = @ContentType
                    ORDER BY ft.[RANK] DESC, d.PublishedAt DESC, d.Id DESC
                    OPTION (RECOMPILE);
                END

                SELECT
                    d.ContentType,
                    d.SourceKey,
                    d.Title,
                    d.Summary,
                    d.Url,
                    d.PublishedAt,
                    d.ImageUrl,
                    d.Category,
                    d.AuthorDisplayName
                FROM   dbo.SearchDocument d
                INNER JOIN #Matches fm ON fm.DocumentId = d.Id
                WHERE  @ContentType IS NULL OR d.ContentType = @ContentType
                ORDER BY fm.SearchRank DESC, d.PublishedAt DESC, d.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT @TotalRecords = COUNT(*)
                FROM   dbo.SearchDocument d
                INNER JOIN #Matches fm ON fm.DocumentId = d.Id
                WHERE  @ContentType IS NULL OR d.ContentType = @ContentType;
            END;
            """);
    }
}
