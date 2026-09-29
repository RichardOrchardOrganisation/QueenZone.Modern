using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;
using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class EfSearchReindexRunRequestRepository(QueenZoneDbContext dbContext)
    : ISearchReindexRunRequestRepository
{
    private const string ActiveKey = "active";
    private static readonly TimeSpan StaleRunTimeout = TimeSpan.FromHours(3);

    public async Task<SearchReindexRunRequestQueueResult> QueueAsync(
        SearchReindexRunRequestCreate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var active = await GetActiveAsync(cancellationToken);
        if (active is not null)
        {
            return new SearchReindexRunRequestQueueResult(Map(active), WasCreated: false);
        }

        var now = DateTime.UtcNow;
        var entity = new SearchReindexRunRequestEntity
        {
            Status = SearchReindexRunRequestStatus.Pending,
            RequestedBy = Normalize(request.RequestedBy, 256),
            RequestedAtUtc = now,
            ActiveKey = ActiveKey,
            UpdatedAtUtc = now
        };
        dbContext.SearchReindexRunRequests.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SearchReindexRunRequestQueueResult(Map(entity), WasCreated: true);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            var raced = await GetActiveAsync(cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return new SearchReindexRunRequestQueueResult(Map(raced), WasCreated: false);
        }
    }

    public async Task<SearchReindexRunRequest?> ClaimNextAsync(
        string runnerId,
        CancellationToken cancellationToken = default)
    {
        runnerId = Normalize(runnerId, 100);

        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(StaleRunTimeout);
        await dbContext.SearchReindexRunRequests
            .Where(request =>
                request.Status == SearchReindexRunRequestStatus.Running
                && request.UpdatedAtUtc < staleBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, SearchReindexRunRequestStatus.Pending)
                .SetProperty(request => request.RunnerId, (string?)null)
                .SetProperty(request => request.StartedAtUtc, (DateTime?)null)
                .SetProperty(request => request.UpdatedAtUtc, now),
                cancellationToken);

        while (true)
        {
            var requestId = await dbContext.SearchReindexRunRequests
                .AsNoTracking()
                .Where(request => request.Status == SearchReindexRunRequestStatus.Pending)
                .OrderBy(request => request.RequestedAtUtc)
                .Select(request => (long?)request.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (requestId is null)
            {
                return null;
            }

            now = DateTime.UtcNow;
            var updated = await dbContext.SearchReindexRunRequests
                .Where(request => request.Id == requestId && request.Status == SearchReindexRunRequestStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(request => request.Status, SearchReindexRunRequestStatus.Running)
                    .SetProperty(request => request.RunnerId, runnerId)
                    .SetProperty(request => request.StartedAtUtc, now)
                    .SetProperty(request => request.UpdatedAtUtc, now),
                    cancellationToken);
            if (updated == 0)
            {
                continue;
            }

            var claimed = await dbContext.SearchReindexRunRequests
                .AsNoTracking()
                .SingleAsync(request => request.Id == requestId, cancellationToken);
            return Map(claimed);
        }
    }

    public Task<bool> CompleteAsync(
        long requestId,
        string summary,
        CancellationToken cancellationToken = default) =>
        FinishAsync(requestId, SearchReindexRunRequestStatus.Completed, Normalize(summary, 2000), null, cancellationToken);

    public Task<bool> FailAsync(
        long requestId,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        FinishAsync(requestId, SearchReindexRunRequestStatus.Failed, null, Normalize(errorMessage, 2000), cancellationToken);

    public async Task<bool> ReturnToPendingAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.SearchReindexRunRequests
            .Where(request => request.Id == requestId && request.Status == SearchReindexRunRequestStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, SearchReindexRunRequestStatus.Pending)
                .SetProperty(request => request.RunnerId, (string?)null)
                .SetProperty(request => request.StartedAtUtc, (DateTime?)null)
                .SetProperty(request => request.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken);
        return updated == 1;
    }

    public async Task<IReadOnlyList<SearchReindexRunRequest>> ListRecentAsync(
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        await dbContext.SearchReindexRunRequests
            .AsNoTracking()
            .OrderByDescending(request => request.RequestedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(request => Map(request))
            .ToListAsync(cancellationToken);

    private async Task<bool> FinishAsync(
        long requestId,
        SearchReindexRunRequestStatus status,
        string? summary,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await dbContext.SearchReindexRunRequests
            .Where(request => request.Id == requestId && request.Status == SearchReindexRunRequestStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, status)
                .SetProperty(request => request.CompletedAtUtc, now)
                .SetProperty(request => request.Summary, summary)
                .SetProperty(request => request.ErrorMessage, errorMessage)
                .SetProperty(request => request.ActiveKey, (string?)null)
                .SetProperty(request => request.UpdatedAtUtc, now),
                cancellationToken);
        return updated == 1;
    }

    private Task<SearchReindexRunRequestEntity?> GetActiveAsync(CancellationToken cancellationToken) =>
        dbContext.SearchReindexRunRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(request => request.ActiveKey == ActiveKey, cancellationToken);

    private static SearchReindexRunRequest Map(SearchReindexRunRequestEntity request) =>
        new(
            request.Id,
            request.Status,
            request.RequestedBy,
            request.RequestedAtUtc,
            request.RunnerId,
            request.StartedAtUtc,
            request.CompletedAtUtc,
            request.Summary,
            request.ErrorMessage);
}
