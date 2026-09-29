namespace QueenZone.SqlServerTests;

/// <summary>
/// Legacy <c>FREDDIE_T</c> as committed in <c>docs/db-schema.txt</c> (this environment cannot
/// reach <c>queenzone_legacy_sync</c>). Used by the Freddie tribute repository tests (#1672 / #1887).
/// </summary>
internal static class LegacyFreddieTributeSchema
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.FREDDIE_T
        (
            ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Name varchar(200) NULL,
            Thought varchar(3500) NULL,
            Email varchar(60) NULL,
            Freddie_Date varchar(50) NOT NULL CONSTRAINT DF_Freddie_Date DEFAULT (getdate()),
            Freddie_Time varchar(50) NULL,
            Country varchar(80) NULL,
            DISPLAY tinyint NULL CONSTRAINT DF_FREDDIE_T_DISPLAY DEFAULT ((0))
        );
        """;
}
