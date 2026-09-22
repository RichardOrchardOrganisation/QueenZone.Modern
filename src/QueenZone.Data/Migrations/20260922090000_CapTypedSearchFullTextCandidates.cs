using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Gives typed full-text search a finite candidate window before filtering by content type.
/// This preserves rare-type recall while preventing common terms from ranking the full corpus.
/// </summary>
/// <remarks>
/// Procedure body source of truth: <c>docs/sql/010-search-document-full-text-search.sql</c>.
/// No EF model change.
/// </remarks>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260922090000_CapTypedSearchFullTextCandidates")]
public partial class CapTypedSearchFullTextCandidates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.SearchDocument_Search
                @Query        NVARCHAR(500),
                @ContentType  NVARCHAR(50) = NULL,
                @Offset       INT,
                @PageSize     INT,
                @TotalRecords INT OUTPUT,
                @RankLimit    INT = 1000
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @MatchLimit INT = CASE
                    WHEN @RankLimit IS NULL OR @RankLimit < 1 THEN 1000
                    WHEN @RankLimit > 1000 THEN 1000
                    ELSE @RankLimit
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
                    FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query) ft
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
