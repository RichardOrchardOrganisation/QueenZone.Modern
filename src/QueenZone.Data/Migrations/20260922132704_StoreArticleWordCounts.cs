using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreArticleWordCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LiveWordCount",
                table: "EditorialArticles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WordCount",
                table: "ArticleSubmissions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                CREATE TABLE #ArticleWordCounts
                (
                    Id uniqueidentifier NOT NULL,
                    IsEditorial bit NOT NULL,
                    Body nvarchar(max) NOT NULL,
                    WordCount int NOT NULL DEFAULT 0
                );

                INSERT INTO #ArticleWordCounts (Id, IsEditorial, Body)
                SELECT Id, 0, COALESCE(Body, N'')
                FROM ArticleSubmissions;

                INSERT INTO #ArticleWordCounts (Id, IsEditorial, Body)
                SELECT Id, 1, COALESCE(LiveBody, N'')
                FROM EditorialArticles
                WHERE LiveTitle IS NOT NULL;

                WHILE EXISTS
                (
                    SELECT 1
                    FROM #ArticleWordCounts
                    WHERE CHARINDEX(N'<', Body) > 0
                      AND CHARINDEX(N'>', Body, CHARINDEX(N'<', Body)) > 0
                )
                BEGIN
                    UPDATE #ArticleWordCounts
                    SET Body = STUFF(
                        Body,
                        CHARINDEX(N'<', Body),
                        CHARINDEX(N'>', Body, CHARINDEX(N'<', Body)) - CHARINDEX(N'<', Body) + 1,
                        N' ')
                    WHERE CHARINDEX(N'<', Body) > 0
                      AND CHARINDEX(N'>', Body, CHARINDEX(N'<', Body)) > 0;
                END;

                UPDATE #ArticleWordCounts
                SET Body = REPLACE(REPLACE(REPLACE(REPLACE(Body, CHAR(9), N' '), CHAR(10), N' '), CHAR(13), N' '), NCHAR(160), N' ');

                WHILE EXISTS (SELECT 1 FROM #ArticleWordCounts WHERE Body LIKE N'%  %')
                BEGIN
                    UPDATE #ArticleWordCounts SET Body = REPLACE(Body, N'  ', N' ') WHERE Body LIKE N'%  %';
                END;

                UPDATE #ArticleWordCounts
                SET WordCount = CASE
                    WHEN LTRIM(RTRIM(Body)) = N'' THEN 0
                    ELSE LEN(LTRIM(RTRIM(Body))) - LEN(REPLACE(LTRIM(RTRIM(Body)), N' ', N'')) + 1
                END;

                UPDATE submissions
                SET WordCount = counts.WordCount
                FROM ArticleSubmissions AS submissions
                INNER JOIN #ArticleWordCounts AS counts ON counts.Id = submissions.Id AND counts.IsEditorial = 0;

                UPDATE editorials
                SET LiveWordCount = counts.WordCount
                FROM EditorialArticles AS editorials
                INNER JOIN #ArticleWordCounts AS counts ON counts.Id = editorials.Id AND counts.IsEditorial = 1;

                DROP TABLE #ArticleWordCounts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiveWordCount",
                table: "EditorialArticles");

            migrationBuilder.DropColumn(
                name: "WordCount",
                table: "ArticleSubmissions");
        }
    }
}
