using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfHelpRequestRepository(QueenZoneDbContext dbContext) : IHelpRequestRepository
{
    public async Task<HelpRequest> CreateAsync(
        HelpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = HelpRequestRecords.NewEntity(request);

        dbContext.HelpRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return HelpRequestRecords.Map(entity);
    }

    public async Task<HelpRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HelpRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        return entity is null ? null : HelpRequestRecords.Map(entity);
    }

    public async Task<HelpRequestListPage> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var statusFilter = HelpRequestRecords.NormalizeStatusFilter(status);

        var query = dbContext.HelpRequests.AsNoTracking();
        if (statusFilter is not null)
        {
            query = query.Where(row => row.Status == statusFilter);
        }

        if (IsSqliteDatabase())
        {
            var allRows = await query
                .Select(row => new
                {
                    row.Id,
                    row.Topic,
                    row.Subject,
                    row.Name,
                    row.Email,
                    row.MemberId,
                    row.Status,
                    row.SubmittedAt,
                })
                .ToListAsync(cancellationToken);

            var ordered = allRows.OrderByDescending(row => row.SubmittedAt).ThenBy(row => row.Id).ToList();
            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row => new HelpRequestListItem(
                    row.Id,
                    row.Topic,
                    row.Subject,
                    row.Name,
                    row.Email,
                    row.MemberId,
                    row.Status,
                    row.SubmittedAt))
                .ToList();

            return new HelpRequestListPage(items, ordered.Count, statusFilter);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageItems = await query
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new HelpRequestListItem(
                row.Id,
                row.Topic,
                row.Subject,
                row.Name,
                row.Email,
                row.MemberId,
                row.Status,
                row.SubmittedAt))
            .ToListAsync(cancellationToken);

        return new HelpRequestListPage(pageItems, totalCount, statusFilter);
    }

    public async Task<HelpRequest?> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewerEmail,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HelpRequests
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        HelpRequestRecords.ApplyStatus(entity, status, reviewerEmail, notes);

        await dbContext.SaveChangesAsync(cancellationToken);
        return HelpRequestRecords.Map(entity);
    }

    public async Task<int> CountByEmailSinceAsync(
        string normalizedEmail,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var key = HelpRequestRecords.NormalizeEmail(normalizedEmail, normalizedEmail);
        var rows = await dbContext.HelpRequests
            .AsNoTracking()
            .Where(row => row.NormalizedEmail == key)
            .Select(row => row.SubmittedAt)
            .ToListAsync(cancellationToken);

        return rows.Count(submittedAt => submittedAt >= sinceUtc);
    }

    public async Task<int> CountByMemberSinceAsync(
        Guid memberId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.HelpRequests
            .AsNoTracking()
            .Where(row => row.MemberId == memberId)
            .Select(row => row.SubmittedAt)
            .ToListAsync(cancellationToken);

        return rows.Count(submittedAt => submittedAt >= sinceUtc);
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.HelpRequests
            .AsNoTracking()
            .CountAsync(
                row => row.Status == HelpRequestStatus.Open
                    || row.Status == HelpRequestStatus.InProgress,
                cancellationToken);
    }

    private bool IsSqliteDatabase() =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal);
}
