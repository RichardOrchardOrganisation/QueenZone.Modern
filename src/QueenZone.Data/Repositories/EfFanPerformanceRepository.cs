using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Reads visible legacy fan-stage recordings through EF Core rather than Dapper.
/// </summary>
public sealed class EfFanPerformanceRepository : IFanPerformanceRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly bool useLegacyProcedures;
    private readonly string pageSelectSql;
    private readonly string countSql;
    private readonly Func<int, FormattableString> byIdSql;

    [ExcludeFromCodeCoverage]
    public EfFanPerformanceRepository(QueenZoneDbContext dbContext)
        : this(
            dbContext,
            useLegacyProcedures: EfProductionSql.CreateFanPerformanceQueries().UseProcs,
            pageSelectSql: EfProductionSql.CreateFanPerformanceQueries().PageSelect,
            countSql: EfProductionSql.CreateFanPerformanceQueries().CountSql,
            byIdSql: EfProductionSql.CreateFanPerformanceQueries().ByIdSql)
    {
    }

    internal EfFanPerformanceRepository(
        QueenZoneDbContext dbContext,
        bool useLegacyProcedures,
        string pageSelectSql,
        string countSql,
        Func<int, FormattableString> byIdSql)
    {
        this.dbContext = dbContext;
        this.useLegacyProcedures = useLegacyProcedures;
        this.pageSelectSql = pageSelectSql;
        this.countSql = countSql;
        this.byIdSql = byIdSql;
    }

    public async Task<IReadOnlyList<FanPerformance>> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (useLegacyProcedures)
        {
            return await GetPageViaSqlServerAsync(page, pageSize, cancellationToken);
        }

        var rowsFromSql = await dbContext.Database
            .SqlQueryRaw<StageRow>(pageSelectSql)
            .ToListAsync(cancellationToken);
        return rowsFromSql.Select(MapRow).ToList();
    }

    [ExcludeFromCodeCoverage] // SQL Server legacy table path.
    private async Task<IReadOnlyList<FanPerformance>> GetPageViaSqlServerAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var offset = (Math.Max(page, 1) - 1) * pageSize;
        var rows = await EfSql.QuerySqlAsync<StageRow>(
            dbContext,
            """
            SELECT CAST(Q_STAGE_ID AS int) AS Q_STAGE_ID, TITLE, PERFORMED_BY,
                   DESCRIPTION, URL, thesize, DATE_ADDED, DurationSeconds
            FROM dbo.Q_STAGE_T
            WHERE DISPLAY = 1
            ORDER BY DATE_ADDED DESC, Q_STAGE_ID DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Offset", offset));
                command.Parameters.Add(EfSql.Input("@PageSize", pageSize));
            },
            cancellationToken: cancellationToken);

        return rows.Select(MapRow).ToList();
    }

    public async Task<int> GetVisibleCountAsync(CancellationToken cancellationToken = default)
    {
        if (useLegacyProcedures &&
            string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal))
        {
            return await GetVisibleCountViaSqlServerAsync(cancellationToken);
        }

        var values = await dbContext.Database
            .SqlQueryRaw<int>(countSql)
            .ToListAsync(cancellationToken);
        return values.FirstOrDefault();
    }

    [ExcludeFromCodeCoverage]
    private Task<int> GetVisibleCountViaSqlServerAsync(CancellationToken cancellationToken) =>
        EfSql.ExecuteScalarSqlAsync(
            dbContext,
            "SELECT COUNT(*) FROM dbo.Q_STAGE_T WHERE DISPLAY = 1",
            cancellationToken: cancellationToken);

    public async Task<FanPerformance?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (useLegacyProcedures &&
            string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal))
        {
            return await GetByIdViaSqlServerAsync(id, cancellationToken);
        }

        var rows = await dbContext.Database
            .SqlQuery<StageRow>(byIdSql(id))
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : MapRow(row);
    }

    [ExcludeFromCodeCoverage] // SQL Server stored procedure path.
    private async Task<FanPerformance?> GetByIdViaSqlServerAsync(int id, CancellationToken cancellationToken)
    {
        var rows = await EfSql.QuerySqlAsync<StageRow>(
            dbContext,
            """
            SELECT CAST(Q_STAGE_ID AS int) AS Q_STAGE_ID, TITLE, PERFORMED_BY,
                   DESCRIPTION, URL, thesize, DATE_ADDED, DurationSeconds
            FROM dbo.Q_STAGE_T
            WHERE Q_STAGE_ID = @id AND DISPLAY = 1
            """,
            command => command.Parameters.Add(EfSql.Input("@id", id)),
            cancellationToken: cancellationToken);
        return rows.FirstOrDefault() is { } row ? MapRow(row) : null;
    }

    private static FanPerformance MapRow(StageRow row) => new(
        Id: row.Q_STAGE_ID,
        Title: row.TITLE,
        PerformedBy: row.PERFORMED_BY,
        Description: row.DESCRIPTION ?? string.Empty,
        AudioFileName: row.URL?.Trim() ?? string.Empty,
        FileSizeBytes: ParseFileSize(row.thesize),
        DateAdded: row.DATE_ADDED,
        DurationSeconds: row.DurationSeconds);

    private static long ParseFileSize(string? thesize) =>
        long.TryParse(thesize, out var parsed) ? parsed : 0;

    internal sealed class StageRow
    {
        public int Q_STAGE_ID { get; set; }

        public string TITLE { get; set; } = string.Empty;

        public string PERFORMED_BY { get; set; } = string.Empty;

        public string? DESCRIPTION { get; set; }

        public string URL { get; set; } = string.Empty;

        public string? thesize { get; set; }

        public DateTime DATE_ADDED { get; set; }

        public int? DurationSeconds { get; set; }
    }
}
