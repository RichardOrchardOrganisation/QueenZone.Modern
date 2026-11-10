using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfFanPerformanceReportRepository(QueenZoneDbContext dbContext)
    : IFanPerformanceReportRepository
{
    public async Task<FanPerformanceReportCreateResult> CreateAsync(
        NewFanPerformanceReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var existing = await dbContext.FanPerformanceReports
            .AsNoTracking()
            .Where(row =>
                row.ReporterMemberId == report.ReporterMemberId
                && row.StageId == report.StageId
                && row.Status == FanPerformanceReportStatus.Open)
            .Select(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing != Guid.Empty)
        {
            return FanPerformanceReportCreateResult.Duplicate(existing);
        }

        var entity = new FanPerformanceReportEntity
        {
            Id = Guid.NewGuid(),
            StageId = report.StageId,
            ReporterMemberId = report.ReporterMemberId,
            Reason = report.Reason,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = FanPerformanceReportStatus.Open,
            TitleSnapshot = report.TitleSnapshot,
            PerformedBySnapshot = report.PerformedBySnapshot,
        };

        dbContext.FanPerformanceReports.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await dbContext.FanPerformanceReports
                .AsNoTracking()
                .Where(row =>
                    row.ReporterMemberId == report.ReporterMemberId
                    && row.StageId == report.StageId
                    && row.Status == FanPerformanceReportStatus.Open)
                .Select(row => row.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (raced != Guid.Empty)
            {
                return FanPerformanceReportCreateResult.Duplicate(raced);
            }

            throw;
        }

        return FanPerformanceReportCreateResult.Created(entity.Id);
    }

    public async Task<FanPerformanceReport?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.FanPerformanceReports
            .AsNoTracking()
            .Include(row => row.Reporter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<FanPerformanceReportListPage> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filter = string.IsNullOrWhiteSpace(status) ? FanPerformanceReportStatus.Open : status.Trim();
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        var query = dbContext.FanPerformanceReports.AsNoTracking();
        if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(row => row.Status == filter);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .Include(row => row.Reporter)
            .ToListAsync(cancellationToken);
        var pageRows = rows
            .OrderBy(row => row.Status == FanPerformanceReportStatus.Open ? 0 : 1)
            .ThenByDescending(row => row.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return new FanPerformanceReportListPage(
            pageRows.Select(MapListItem).ToList(),
            totalCount,
            filter);
    }

    public async Task<FanPerformanceReport?> UpdateStatusAsync(
        Guid id,
        string status,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var next = FanPerformanceReportStatus.Normalize(status);
        var entity = await dbContext.FanPerformanceReports
            .Include(row => row.Reporter)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Status = next;
        entity.ReviewedBy = string.IsNullOrWhiteSpace(actorEmail) ? "unknown" : actorEmail.Trim();
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default) =>
        dbContext.FanPerformanceReports.CountAsync(
            row => row.Status == FanPerformanceReportStatus.Open,
            cancellationToken);

    private static FanPerformanceReport Map(FanPerformanceReportEntity entity) =>
        new(
            entity.Id,
            entity.StageId,
            entity.ReporterMemberId,
            DisplayName(entity.Reporter),
            entity.Reason,
            entity.CreatedAt,
            entity.Status,
            entity.TitleSnapshot,
            entity.PerformedBySnapshot,
            entity.ReviewedBy,
            entity.ReviewedAt);

    private static FanPerformanceReportListItem MapListItem(FanPerformanceReportEntity entity) =>
        new(
            entity.Id,
            entity.StageId,
            entity.ReporterMemberId,
            DisplayName(entity.Reporter),
            entity.Reason,
            entity.CreatedAt,
            entity.Status,
            entity.TitleSnapshot,
            entity.PerformedBySnapshot);

    private static string DisplayName(MemberAccount? member) =>
        string.IsNullOrWhiteSpace(member?.DisplayName) ? "Unknown member" : member.DisplayName;
}
