using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfAdminQueenHistoryRepository(QueenZoneDbContext dbContext) : IAdminQueenHistoryRepository
{
    public async Task<AdminQueenHistoryPage> GetPageAsync(
        AdminQueenHistoryListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.QueenHistoryEvents.AsNoTracking().AsQueryable();
        if (filter.IsPublished is bool isPublished)
        {
            query = query.Where(item => item.IsPublished == isPublished);
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var needle = filter.Query.Trim();
            query = query.Where(item => item.Title.Contains(needle) || item.Summary.Contains(needle));
        }

        var rows = await query.ToListAsync(cancellationToken);
        var sorted = rows
            .OrderByDescending(item => item.EventDate)
            .ThenByDescending(item => item.Importance)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var items = sorted
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(Map)
            .ToList();

        return new AdminQueenHistoryPage(items, sorted.Count, safePage, safePageSize);
    }

    public async Task<QueenHistoryEvent?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.QueenHistoryEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<int> CreateAsync(AdminQueenHistoryDraft draft, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var row = new QueenHistoryEventEntity
        {
            Title = draft.Title,
            Summary = draft.Summary,
            EventDate = draft.EventDate,
            DatePrecision = draft.DatePrecision,
            Category = draft.Category,
            Importance = draft.Importance,
            SourceType = QueenHistoryEventSourceType.Curated,
            SourceKey = $"curated:{Guid.NewGuid():N}",
            SourceUrl = draft.SourceUrl,
            IsPublished = draft.IsPublished,
            CreatedAt = now,
            UpdatedAt = now,
        };
        AssignClientRowVersion(row);

        dbContext.QueenHistoryEvents.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task UpdateAsync(
        int id,
        AdminQueenHistoryDraft draft,
        byte[]? expectedRowVersion = null,
        CancellationToken cancellationToken = default)
    {
        var row = await GetTrackedAsync(id, cancellationToken);
        QueenZoneConcurrency.EnsureRowVersion(row.RowVersion, expectedRowVersion);
        row.Title = draft.Title;
        row.Summary = draft.Summary;
        row.EventDate = draft.EventDate;
        row.DatePrecision = draft.DatePrecision;
        row.Category = draft.Category;
        row.Importance = draft.Importance;
        row.SourceUrl = draft.SourceUrl;
        row.IsPublished = draft.IsPublished;
        row.UpdatedAt = DateTime.UtcNow;
        AssignClientRowVersion(row);
        await QueenZoneConcurrency.SaveChangesAsync(dbContext, cancellationToken);
    }

    public async Task DeleteAsync(int id, byte[]? expectedRowVersion = null, CancellationToken cancellationToken = default)
    {
        var row = await GetTrackedAsync(id, cancellationToken);
        QueenZoneConcurrency.EnsureRowVersion(row.RowVersion, expectedRowVersion);
        dbContext.QueenHistoryEvents.Remove(row);
        await QueenZoneConcurrency.SaveChangesAsync(dbContext, cancellationToken);
    }

    public async Task SetPublishedAsync(
        int id,
        bool isPublished,
        byte[]? expectedRowVersion = null,
        CancellationToken cancellationToken = default)
    {
        var row = await GetTrackedAsync(id, cancellationToken);
        QueenZoneConcurrency.EnsureRowVersion(row.RowVersion, expectedRowVersion);
        row.IsPublished = isPublished;
        row.UpdatedAt = DateTime.UtcNow;
        AssignClientRowVersion(row);
        await QueenZoneConcurrency.SaveChangesAsync(dbContext, cancellationToken);
    }

    private async Task<QueenHistoryEventEntity> GetTrackedAsync(int id, CancellationToken cancellationToken)
    {
        var row = await dbContext.QueenHistoryEvents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            throw new InvalidOperationException($"Queen history event {id} was not found.");
        }

        return row;
    }

    private void AssignClientRowVersion(QueenHistoryEventEntity entity)
    {
        if (!dbContext.Database.IsSqlServer())
        {
            entity.RowVersion = QueenZoneConcurrency.NewClientRowVersion();
        }
    }

    private static QueenHistoryEvent Map(QueenHistoryEventEntity row) =>
        new(
            row.Id,
            row.Title,
            row.Summary,
            row.EventDate,
            row.DatePrecision,
            row.Category,
            row.Importance,
            row.SourceType,
            row.SourceKey,
            row.SourceUrl,
            row.IsPublished,
            row.RowVersion);
}
