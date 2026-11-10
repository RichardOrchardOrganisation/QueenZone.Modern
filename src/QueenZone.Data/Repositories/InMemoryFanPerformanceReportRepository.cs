using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryFanPerformanceReportRepository : IFanPerformanceReportRepository
{
    private readonly object sync = new();
    private readonly List<FanPerformanceReportEntity> reports = [];
    private readonly Func<Guid, MemberAccount?>? resolveMember;

    public InMemoryFanPerformanceReportRepository(Func<Guid, MemberAccount?>? resolveMember = null)
    {
        this.resolveMember = resolveMember;
    }

    public Task<FanPerformanceReportCreateResult> CreateAsync(
        NewFanPerformanceReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (sync)
        {
            var existing = reports.FirstOrDefault(row =>
                row.ReporterMemberId == report.ReporterMemberId
                && row.StageId == report.StageId
                && FanPerformanceReportStatus.IsOpen(row.Status));
            if (existing is not null)
            {
                return Task.FromResult(FanPerformanceReportCreateResult.Duplicate(existing.Id));
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
            reports.Add(entity);
            return Task.FromResult(FanPerformanceReportCreateResult.Created(entity.Id));
        }
    }

    public Task<FanPerformanceReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            var entity = reports.SingleOrDefault(row => row.Id == id);
            return Task.FromResult(entity is null ? null : Map(entity));
        }
    }

    public Task<FanPerformanceReportListPage> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filter = string.IsNullOrWhiteSpace(status) ? FanPerformanceReportStatus.Open : status.Trim();
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        lock (sync)
        {
            var query = reports.AsEnumerable();
            if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(row =>
                    string.Equals(row.Status, filter, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query
                .OrderBy(row => FanPerformanceReportStatus.IsOpen(row.Status) ? 0 : 1)
                .ThenByDescending(row => row.CreatedAt)
                .ToList();
            var items = ordered
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .Select(MapListItem)
                .ToList();

            return Task.FromResult(new FanPerformanceReportListPage(items, ordered.Count, filter));
        }
    }

    public Task<FanPerformanceReport?> UpdateStatusAsync(
        Guid id,
        string status,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var next = FanPerformanceReportStatus.Normalize(status);
        lock (sync)
        {
            var entity = reports.SingleOrDefault(row => row.Id == id);
            if (entity is null)
            {
                return Task.FromResult<FanPerformanceReport?>(null);
            }

            entity.Status = next;
            entity.ReviewedBy = string.IsNullOrWhiteSpace(actorEmail) ? "unknown" : actorEmail.Trim();
            entity.ReviewedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<FanPerformanceReport?>(Map(entity));
        }
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(reports.Count(row => FanPerformanceReportStatus.IsOpen(row.Status)));
        }
    }

    private FanPerformanceReport Map(FanPerformanceReportEntity entity)
    {
        var member = resolveMember?.Invoke(entity.ReporterMemberId);
        return new FanPerformanceReport(
            entity.Id,
            entity.StageId,
            entity.ReporterMemberId,
            string.IsNullOrWhiteSpace(member?.DisplayName) ? "Unknown member" : member.DisplayName,
            entity.Reason,
            entity.CreatedAt,
            entity.Status,
            entity.TitleSnapshot,
            entity.PerformedBySnapshot,
            entity.ReviewedBy,
            entity.ReviewedAt);
    }

    private FanPerformanceReportListItem MapListItem(FanPerformanceReportEntity entity)
    {
        var member = resolveMember?.Invoke(entity.ReporterMemberId);
        return new FanPerformanceReportListItem(
            entity.Id,
            entity.StageId,
            entity.ReporterMemberId,
            string.IsNullOrWhiteSpace(member?.DisplayName) ? "Unknown member" : member.DisplayName,
            entity.Reason,
            entity.CreatedAt,
            entity.Status,
            entity.TitleSnapshot,
            entity.PerformedBySnapshot);
    }
}
