namespace QueenZone.SqlServerTests;

/// <summary>
/// Modern <c>SearchDocument</c> copied from the <c>queenzone_legacy_sync</c> read-only dump of
/// 2026-09-30 (#1895). Column types, named PK, unique source-key index, and
/// <c>ContentType ASC, PublishedAt DESC</c> match that catalog. No defaults, checks, or FKs.
/// Full-text catalog <c>FT_SearchCatalog</c> (Title/Body, language 1033, AUTO, system stoplist)
/// is omitted because LocalDB and the CI container have no full-text engine.
/// </summary>
internal static class SearchDocumentSchema
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.SearchDocument
        (
            Id uniqueidentifier NOT NULL,
            SourceKey nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            ContentType nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Title nvarchar(300) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Body nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            Summary nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            Url nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            PublishedAt datetimeoffset NULL,
            ImageUrl nvarchar(512) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            Category nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            AuthorDisplayName nvarchar(256) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            IndexedAt datetimeoffset NOT NULL,
            CONSTRAINT PK_SearchDocument PRIMARY KEY CLUSTERED (Id)
        );

        CREATE UNIQUE NONCLUSTERED INDEX UQ_SearchDocument_SourceKey
            ON dbo.SearchDocument (SourceKey);

        CREATE NONCLUSTERED INDEX IX_SearchDocument_ContentType_PublishedAt
            ON dbo.SearchDocument (ContentType ASC, PublishedAt DESC);
        """;
}
