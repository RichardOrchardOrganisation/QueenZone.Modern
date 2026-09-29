namespace QueenZone.SqlServerTests;

/// <summary>
/// Legacy <c>FREDDIE_T</c> copied from the <c>queenzone_legacy_sync</c> read-only dump of
/// 2026-09-29 (#1672 / #1887). No stored procedures, check constraints, FKs, or secondary
/// indexes — the modern repositories use inline SQL. Shared by the Freddie tribute
/// repository tests.
/// </summary>
internal static class LegacyFreddieTributeSchema
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.FREDDIE_T (
          ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FREDDIE_T PRIMARY KEY CLUSTERED,
          Name varchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          Thought varchar(3500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          Email varchar(60) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          Freddie_Date varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL CONSTRAINT DF_Freddie_Date DEFAULT (getdate()),
          Freddie_Time varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          Country varchar(80) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          DISPLAY tinyint NULL CONSTRAINT DF_FREDDIE_T_DISPLAY DEFAULT ((0))
        );
        """;
}
