using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

public sealed class EfFreddieTributeRepository : IFreddieTributeRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly string pageSql;
    private readonly string countSql;
    private readonly string idBoundsSql;
    private readonly string idSeekAtOrAfterSql;
    private readonly string idSeekBeforeSql;
    private readonly string byIdSql;

    [ExcludeFromCodeCoverage]
    public EfFreddieTributeRepository(QueenZoneDbContext dbContext)
        : this(
            dbContext,
            FreddieTributeSql.PageSql,
            FreddieTributeSql.CountSql,
            FreddieTributeSql.IdBoundsSql,
            FreddieTributeSql.IdSeekAtOrAfterSql,
            FreddieTributeSql.IdSeekBeforeSql,
            FreddieTributeSql.ByIdSql)
    {
    }

    internal EfFreddieTributeRepository(
        QueenZoneDbContext dbContext,
        string pageSql,
        string countSql,
        string idBoundsSql,
        string idSeekAtOrAfterSql,
        string idSeekBeforeSql,
        string byIdSql)
    {
        this.dbContext = dbContext;
        this.pageSql = pageSql;
        this.countSql = countSql;
        this.idBoundsSql = idBoundsSql;
        this.idSeekAtOrAfterSql = idSeekAtOrAfterSql;
        this.idSeekBeforeSql = idSeekBeforeSql;
        this.byIdSql = byIdSql;
    }

    public async Task<FreddieTributePage> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (safePage - 1) * safePageSize;

        var total = await dbContext.Database
            .SqlQueryRaw<IntValueRow>(countSql)
            .SingleAsync(cancellationToken);

        var rows = await dbContext.Database
            .SqlQueryRaw<FreddieTributeRow>(pageSql, offset, safePageSize)
            .ToListAsync(cancellationToken);

        return new FreddieTributePage(rows.Select(Map).ToList(), total.Value);
    }

    public async Task<FreddieTribute?> GetRandomAsync(CancellationToken cancellationToken = default)
    {
        var id = await PickRandomVisibleIdAsync(cancellationToken);
        return id is int value ? await GetVisibleByIdAsync(value, cancellationToken) : null;
    }

    public async Task<int?> PickRandomVisibleIdAsync(CancellationToken cancellationToken = default)
    {
        var bounds = await dbContext.Database
            .SqlQueryRaw<IdBoundsRow>(idBoundsSql)
            .ToListAsync(cancellationToken);
        var range = bounds.FirstOrDefault();
        if (range?.MinId is not int minId || range.MaxId is not int maxId)
        {
            return null;
        }

        var target = IndexedIdRange.NextTarget(minId, maxId);
        var atOrAfter = await SeekIdAsync(idSeekAtOrAfterSql, target, cancellationToken);
        if (atOrAfter is int id)
        {
            return id;
        }

        return await SeekIdAsync(idSeekBeforeSql, target, cancellationToken);
    }

    public async Task<FreddieTribute?> GetVisibleByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<FreddieTributeRow>(byIdSql, id)
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : Map(row);
    }

    private async Task<int?> SeekIdAsync(string seekSql, int targetId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<IntValueRow>(seekSql, targetId)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault() is { } row && row.Value > 0 ? row.Value : null;
    }

    private static FreddieTribute Map(FreddieTributeRow row) =>
        new(
            row.Id,
            string.IsNullOrWhiteSpace(row.Name) ? "Anonymous" : row.Name.Trim(),
            row.Thought.Trim(),
            string.IsNullOrWhiteSpace(row.Country) ? null : row.Country.Trim(),
            row.DateText.Trim(),
            string.IsNullOrWhiteSpace(row.TimeText) ? null : row.TimeText.Trim());

    private sealed class FreddieTributeRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Thought { get; set; } = string.Empty;

        public string? Country { get; set; }

        public string DateText { get; set; } = string.Empty;

        public string? TimeText { get; set; }
    }

    private sealed class IntValueRow
    {
        public int Value { get; set; }
    }

    private sealed class IdBoundsRow
    {
        public int? MinId { get; set; }

        public int? MaxId { get; set; }
    }
}

