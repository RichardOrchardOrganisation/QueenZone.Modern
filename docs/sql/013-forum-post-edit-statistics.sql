-- Applied by 20261022130000_PrecreateForumPostUpdatedAtStatistics (SQL Server only).
-- Use deployment time for initial UpdatedAt histogram sampling, rather than the first edit.
-- SQL Server chooses the default sample size. MAXDOP 1 bounds CPU, not reads or duration.
-- Existing unfiltered leading UpdatedAt statistics are retained without resampling.
-- Downgrade intentionally retains statistics, including any created by this script.
-- An unrelated statistic using the reserved name fails creation rather than being replaced.
IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.ModernForumPost', N'UpdatedAt') IS NOT NULL
    AND NOT EXISTS
    (
        SELECT 1
        FROM sys.stats AS s
        INNER JOIN sys.stats_columns AS sc
            ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
        WHERE s.object_id = OBJECT_ID(N'dbo.ModernForumPost', N'U')
            AND sc.stats_column_id = 1
            AND sc.column_id = COLUMNPROPERTY(s.object_id, N'UpdatedAt', 'ColumnId')
            AND s.has_filter = 0
            AND s.is_temporary = 0
    )
BEGIN
    EXEC(N'CREATE STATISTICS [ST_ModernForumPost_UpdatedAt]
        ON [dbo].[ModernForumPost] ([UpdatedAt]) WITH MAXDOP = 1;');
END;
