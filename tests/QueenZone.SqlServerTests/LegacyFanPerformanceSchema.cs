namespace QueenZone.SqlServerTests;

/// <summary>
/// Legacy <c>Q_STAGE_T</c> copied from the <c>queenzone_legacy_sync</c> read-only dump of
/// 2026-09-29 (#1672 / #1888). No stored procedures, check constraints, FKs, or secondary
/// indexes — the modern repositories use ad-hoc SQL only. Shared by the public and admin
/// fan-performance repository tests.
/// </summary>
internal static class LegacyFanPerformanceSchema
{
    public const string CreateTableSql = """
        CREATE TABLE dbo.Q_STAGE_T (
          Q_STAGE_ID smallint IDENTITY(1,1) NOT NULL,
          TITLE varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          PERFORMED_BY varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          DESCRIPTION varchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          URL varchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          THESIZE varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          DATE_ADDED smalldatetime NOT NULL
            CONSTRAINT DF_Q_STAGE_T_DATE_ADDED DEFAULT (getdate()),
          DISPLAY tinyint NULL
            CONSTRAINT DF_Q_STAGE_T_DISPLAY DEFAULT ((0)),
          CONTACT varchar(300) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
          USER_ID int NOT NULL
            CONSTRAINT DF_Q_STAGE_T_USER_ID DEFAULT ((0)),
          ALLOW_RATING tinyint NOT NULL
            CONSTRAINT DF_Q_STAGE_T_ALLOW_RATING DEFAULT ((0)),
          DurationSeconds int NULL,
          CONSTRAINT PK_Q_STAGE_T PRIMARY KEY CLUSTERED (Q_STAGE_ID) WITH (FILLFACTOR = 90)
        );
        """;
}
