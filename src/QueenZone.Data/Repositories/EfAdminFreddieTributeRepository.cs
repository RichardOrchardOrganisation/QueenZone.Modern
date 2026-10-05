using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

public sealed class EfAdminFreddieTributeRepository : IAdminFreddieTributeRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly string pageSql;
    private readonly string countSql;
    private readonly string byIdSql;

    public EfAdminFreddieTributeRepository(QueenZoneDbContext dbContext)
        : this(
            dbContext,
            """
            WITH Filtered AS
            (
                SELECT
                    ID AS Id,
                    LTRIM(RTRIM(ISNULL(Name, 'Anonymous'))) AS Name,
                    LTRIM(RTRIM(ISNULL(Thought, ''))) AS Thought,
                    NULLIF(LTRIM(RTRIM(ISNULL(Country, ''))), '') AS Country,
                    LTRIM(RTRIM(ISNULL(Freddie_Date, ''))) AS DateText,
                    NULLIF(LTRIM(RTRIM(ISNULL(Freddie_Time, ''))), '') AS TimeText,
                    CASE WHEN DISPLAY = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsVisible,
                    COUNT(*) OVER (
                        PARTITION BY
                            UPPER(LTRIM(RTRIM(ISNULL(Name, '')))),
                            UPPER(LTRIM(RTRIM(ISNULL(Thought, ''))))
                    ) AS DuplicateCount
                FROM dbo.FREDDIE_T
                WHERE ({2} IS NULL OR DISPLAY = {2})
                  AND (
                      {3} IS NULL
                      OR Name LIKE '%' + {3} + '%'
                      OR Thought LIKE '%' + {3} + '%'
                      OR Country LIKE '%' + {3} + '%'
                  )
            )
            SELECT Id, Name, Thought, Country, DateText, TimeText, IsVisible, DuplicateCount
            FROM Filtered
            WHERE ({4} = 0 OR DuplicateCount > 1)
            ORDER BY Id DESC
            OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY
            """,
            """
            WITH Filtered AS
            (
                SELECT
                    ID AS Id,
                    COUNT(*) OVER (
                        PARTITION BY
                            UPPER(LTRIM(RTRIM(ISNULL(Name, '')))),
                            UPPER(LTRIM(RTRIM(ISNULL(Thought, ''))))
                    ) AS DuplicateCount
                FROM dbo.FREDDIE_T
                WHERE ({0} IS NULL OR DISPLAY = {0})
                  AND (
                      {1} IS NULL
                      OR Name LIKE '%' + {1} + '%'
                      OR Thought LIKE '%' + {1} + '%'
                      OR Country LIKE '%' + {1} + '%'
                  )
            )
            SELECT COUNT(*) AS Value
            FROM Filtered
            WHERE ({2} = 0 OR DuplicateCount > 1)
            """,
            """
            SELECT
                ID AS Id,
                LTRIM(RTRIM(ISNULL(Name, 'Anonymous'))) AS Name,
                LTRIM(RTRIM(ISNULL(Thought, ''))) AS Thought,
                NULLIF(LTRIM(RTRIM(ISNULL(Country, ''))), '') AS Country,
                LTRIM(RTRIM(ISNULL(Freddie_Date, ''))) AS DateText,
                NULLIF(LTRIM(RTRIM(ISNULL(Freddie_Time, ''))), '') AS TimeText,
                CASE WHEN DISPLAY = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsVisible,
                COUNT(*) OVER (
                    PARTITION BY
                        UPPER(LTRIM(RTRIM(ISNULL(Name, '')))),
                        UPPER(LTRIM(RTRIM(ISNULL(Thought, ''))))
                ) AS DuplicateCount
            FROM dbo.FREDDIE_T
            WHERE ID = {0}
            """)
    {
    }

    internal EfAdminFreddieTributeRepository(
        QueenZoneDbContext dbContext,
        string pageSql,
        string countSql,
        string byIdSql)
    {
        this.dbContext = dbContext;
        this.pageSql = pageSql;
        this.countSql = countSql;
        this.byIdSql = byIdSql;
    }

    public async Task<AdminFreddieTributePage> GetPageAsync(
        AdminFreddieTributeListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (safePage - 1) * safePageSize;
        int? visibility = filter.IsVisible switch
        {
            true => 1,
            false => 0,
            null => null,
        };
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        var duplicatesOnly = filter.DuplicatesOnly ? 1 : 0;
        var visibilityParameter = (object?)visibility ?? DBNull.Value;
        var searchParameter = (object?)search ?? DBNull.Value;

        var totals = await dbContext.Database
            .SqlQueryRaw<IntValueRow>(countSql, visibilityParameter, searchParameter, duplicatesOnly)
            .ToListAsync(cancellationToken);
        var total = totals.Single();
        var rows = await dbContext.Database
            .SqlQueryRaw<AdminFreddieTributeRow>(
                pageSql,
                offset,
                safePageSize,
                visibilityParameter,
                searchParameter,
                duplicatesOnly)
            .ToListAsync(cancellationToken);

        return new AdminFreddieTributePage(rows.Select(Map).ToList(), total.Value, safePage, safePageSize);
    }

    public async Task<AdminFreddieTributeItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<AdminFreddieTributeRow>(byIdSql, id)
            .ToListAsync(cancellationToken);

        var row = rows.FirstOrDefault();
        return row is null ? null : Map(row);
    }

    public async Task SetVisibilityAsync(
        int id,
        bool isVisible,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default)
    {
        var sql = expectedIsVisible is bool expected
            ? "UPDATE dbo.FREDDIE_T SET DISPLAY = {0} WHERE ID = {1} AND DISPLAY = {2}"
            : "UPDATE dbo.FREDDIE_T SET DISPLAY = {0} WHERE ID = {1}";
        var displayValue = isVisible ? 1 : 0;
        object[] parameters;
        if (expectedIsVisible is bool expectedDisplay)
        {
            parameters = [displayValue, id, expectedDisplay ? 1 : 0];
        }
        else
        {
            parameters = [displayValue, id];
        }
        var affected = await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        await EnsureTributeWriteAsync(id, affected, cancellationToken);
    }

    public async Task DeleteAsync(
        int id,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default)
    {
        var sql = expectedIsVisible is bool
            ? "DELETE FROM dbo.FREDDIE_T WHERE ID = {0} AND DISPLAY = {1}"
            : "DELETE FROM dbo.FREDDIE_T WHERE ID = {0}";
        object[] parameters;
        if (expectedIsVisible is bool expectedDisplay)
        {
            parameters = [id, expectedDisplay ? 1 : 0];
        }
        else
        {
            parameters = [id];
        }
        var affected = await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        await EnsureTributeWriteAsync(id, affected, cancellationToken);
    }

    private async Task EnsureTributeWriteAsync(int id, int affected, CancellationToken cancellationToken)
    {
        if (affected > 0)
        {
            return;
        }

        var exists = await GetByIdAsync(id, cancellationToken) is not null;
        QueenZoneConcurrency.EnsureUpdated(affected, exists, $"Freddie tribute {id} was not found.");
    }

    private static AdminFreddieTributeItem Map(AdminFreddieTributeRow row) =>
        new(
            row.Id,
            string.IsNullOrWhiteSpace(row.Name) ? "Anonymous" : row.Name.Trim(),
            row.Thought.Trim(),
            string.IsNullOrWhiteSpace(row.Country) ? null : row.Country.Trim(),
            row.DateText.Trim(),
            string.IsNullOrWhiteSpace(row.TimeText) ? null : row.TimeText.Trim(),
            row.IsVisible,
            row.DuplicateCount);

    private sealed class AdminFreddieTributeRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Thought { get; set; } = string.Empty;

        public string? Country { get; set; }

        public string DateText { get; set; } = string.Empty;

        public string? TimeText { get; set; }

        public bool IsVisible { get; set; }

        public int DuplicateCount { get; set; }
    }

    private sealed class IntValueRow
    {
        public int Value { get; set; }
    }
}
