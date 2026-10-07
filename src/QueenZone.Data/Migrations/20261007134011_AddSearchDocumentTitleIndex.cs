using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchDocumentTitleIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ONLINE must be assembled in dynamic SQL. SQL Express parses every
            // CREATE INDEX in the batch even when EngineEdition <> 5 (#1628).
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.SearchDocument', N'U') IS NOT NULL
                   AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE object_id = OBJECT_ID(N'dbo.SearchDocument', N'U')
                          AND name = N'IX_SearchDocument_Title')
                BEGIN
                    DECLARE @sql nvarchar(max);
                    IF SERVERPROPERTY('EngineEdition') = 5
                        SET @sql = N'CREATE INDEX IX_SearchDocument_Title ON dbo.SearchDocument (Title) INCLUDE (ContentType, Url) WITH (ONLINE = ON);';
                    ELSE
                        SET @sql = N'CREATE INDEX IX_SearchDocument_Title ON dbo.SearchDocument (Title) INCLUDE (ContentType, Url);';
                    EXEC sys.sp_executesql @sql;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.SearchDocument', N'U') IS NOT NULL
                   AND EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE object_id = OBJECT_ID(N'dbo.SearchDocument', N'U')
                          AND name = N'IX_SearchDocument_Title')
                BEGIN
                    DROP INDEX IX_SearchDocument_Title
                        ON dbo.SearchDocument;
                END
                """);
        }
    }
}
