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
            // ONLINE is Azure SQL only (EngineEdition = 5). SQL Express (#1628) rejects it.
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.SearchDocument', N'U') IS NOT NULL
                   AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE object_id = OBJECT_ID(N'dbo.SearchDocument', N'U')
                          AND name = N'IX_SearchDocument_Title')
                BEGIN
                    IF SERVERPROPERTY('EngineEdition') = 5
                        CREATE INDEX IX_SearchDocument_Title
                            ON dbo.SearchDocument (Title)
                            INCLUDE (ContentType, Url)
                            WITH (ONLINE = ON);
                    ELSE
                        CREATE INDEX IX_SearchDocument_Title
                            ON dbo.SearchDocument (Title)
                            INCLUDE (ContentType, Url);
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
