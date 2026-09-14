using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Adds <c>OPTION (RECOMPILE)</c> to both <c>FREETEXTTABLE</c> match inserts in
/// <c>dbo.SearchDocument_Search</c>. <c>@MatchLimit</c> and <c>@ContentType</c> are local
/// variables, not literals, so without <c>RECOMPILE</c> the optimizer can cache one plan
/// from whichever <c>@Query</c>/<c>@ContentType</c> happened to run first and reuse it for
/// every later call. Full-text selectivity swings enormously by search term — a query built
/// from individually common words (e.g. "Live Aid 1985" on a Queen fan archive, where "Live"
/// and "Aid" each hit a large fraction of the corpus on their own) needs a very different
/// plan than a rare single-word query, and the <c>top_n_by_rank</c> push-down into the
/// full-text engine only reliably kicks in when the limit is known at compile time. A stale
/// sniffed plan reused a full-corpus rank/sort strategy for "Live Aid 1985" and blew the
/// 30-second command timeout even though <c>@MatchLimit</c> capped the row count.
/// </summary>
/// <remarks>
/// Procedure body source of truth: <c>docs/sql/010-search-document-full-text-search.sql</c>.
/// No EF model change.
/// </remarks>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260914080000_RecompileSearchDocumentSearchMatches")]
public partial class RecompileSearchDocumentSearchMatches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
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
                    FROM   FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit) ft;
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
                    ORDER BY ft.[RANK] DESC, d.PublishedAt DESC, d.Id DESC;
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
