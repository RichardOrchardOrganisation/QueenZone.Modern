using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Covers the SQL Server path of <see cref="EfSiteSearchService"/> excluding Freddie tributes
/// and paging through <c>#Page</c> (#1443). Full-text search is not installed in LocalDB or
/// the CI <c>mssql</c> container, so the test creates <c>dbo.SearchDocument_Search</c> from
/// <see cref="ExcludeTributesFromSiteSearch"/> with only the <c>FREETEXTTABLE</c> sources
/// swapped for a LIKE match. Rank caps, tribute filters, paging, and <c>@TotalRecords</c>
/// run as shipped.
/// </summary>
public sealed class SearchDocumentSearchSqlServerTests : IAsyncLifetime
{
    private const string FreeTextUntyped =
        "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit) ft";

    private const string FreeTextTyped =
        "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit) ft";

    private const string LikeSource = """
        (
            SELECT Id AS [KEY],
                   MAX(CASE WHEN Title LIKE N'%' + @Query + N'%' THEN 2 ELSE 1 END) AS [RANK]
            FROM dbo.SearchDocument
            WHERE Title LIKE N'%' + @Query + N'%' 
               OR Body LIKE N'%' + @Query + N'%' 
            GROUP BY Id
        ) ft
        """;

    private const string SearchDocumentTableSql = """
        CREATE TABLE dbo.SearchDocument
        (
            Id uniqueidentifier NOT NULL PRIMARY KEY,
            SourceKey nvarchar(200) NOT NULL,
            ContentType nvarchar(50) NOT NULL,
            Title nvarchar(300) NOT NULL,
            Body nvarchar(max) NOT NULL,
            Summary nvarchar(500) NULL,
            Url nvarchar(500) NOT NULL,
            PublishedAt datetimeoffset NULL,
            ImageUrl nvarchar(512) NULL,
            Category nvarchar(200) NULL,
            AuthorDisplayName nvarchar(256) NULL,
            IndexedAt datetimeoffset NOT NULL
        );
        """;
