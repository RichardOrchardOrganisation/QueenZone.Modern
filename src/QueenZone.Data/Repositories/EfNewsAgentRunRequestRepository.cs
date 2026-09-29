using static QueenZone.Data.RunRequestText;

using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfNewsAgentRunRequestRepository(QueenZoneDbContext dbContext)
    : INewsAgentRunRequestRepository
{
    private const string ActiveKey = "active";
    private static readonly TimeSpan StaleRunTimeout = TimeSpan.FromHours(3);

    public async Task<NewsAgentRunRequestQueueResult> QueueAsync(
        NewsAgentRunRequestCreate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Kind == NewsAgentRunRequestKind.ScheduledGathering)
        {
            var active = await GetActiveGatheringAsync(cancellationToken);
            if (active is not null)
            {
                return new NewsAgentRunRequestQueueResult(Map(active), WasCreated: false);
            }
        }

        var now = DateTime.UtcNow;
        var entity = new NewsAgentRunRequestEntity
        {
            Status = NewsAgentRunRequestStatus.Pending,
            Kind = request.Kind,
            RequestedBy = Normalize(request.RequestedBy, 256),
            RequestedAtUtc = now,
            ArticleUrl = string.IsNullOrWhiteSpace(request.ArticleUrl)
                ? null
                : Normalize(request.ArticleUrl, 2000),
            GenerateDraft = request.GenerateDraft,
            ActiveKey = request.Kind == NewsAgentRunRequestKind.ScheduledGathering ? ActiveKey : null,
            UpdatedAtUtc = now
        };
        dbContext.NewsAgentRunRequests.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new NewsAgentRunRequestQueueResult(Map(entity), WasCreated: true);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            if (request.Kind != NewsAgentRunRequestKind.ScheduledGathering)
            {
                throw;
            }

            var active = await GetActiveGatheringAsync(cancellationToken);
            if (active is null)
            {
                throw;
            }

            return new NewsAgentRunRequestQueueResult(Map(active), WasCreated: false);
        }
    }

    public async Task<NewsAgentRunRequest?> ClaimNextAsync(
        string runnerId,
        CancellationToken cancellationToken = default)
    {
        runnerId = Normalize(runnerId, 100);
        await UpsertHeartbeatAsync(runnerId, claimed: false, cancellationToken);

        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(StaleRunTimeout);
        await dbContext.NewsAgentRunRequests
            .Where(request =>
                request.Status == NewsAgentRunRequestStatus.Running
                && request.UpdatedAtUtc < staleBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, NewsAgentRunRequestStatus.Pending)
                .SetProperty(request => request.RunnerId, (string?)null)
                .SetProperty(request => request.StartedAtUtc, (DateTime?)null)
                .SetProperty(request => request.UpdatedAtUtc, now),
                cancellationToken);

        while (true)
        {
            var requestId = await dbContext.NewsAgentRunRequests
                .AsNoTracking()
                .Where(request => request.Status == NewsAgentRunRequestStatus.Pending)
                .OrderBy(request => request.RequestedAtUtc)
                .Select(request => (long?)request.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (requestId is null)
            {
                return null;
            }

            now = DateTime.UtcNow;
            var updated = await dbContext.NewsAgentRunRequests
                .Where(request => request.Id == requestId && request.Status == NewsAgentRunRequestStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(request => request.Status, NewsAgentRunRequestStatus.Running)
                    .SetProperty(request => request.RunnerId, runnerId)
                    .SetProperty(request => request.StartedAtUtc, now)
                    .SetProperty(request => request.UpdatedAtUtc, now),
                    cancellationToken);
            if (updated == 0)
            {
                continue;
            }

            await UpsertHeartbeatAsync(runnerId, claimed: true, cancellationToken);
            var claimed = await dbContext.NewsAgentRunRequests
                .AsNoTracking()
                .SingleAsync(request => request.Id == requestId, cancellationToken);
            return Map(claimed);
        }
    }

    public Task<bool> CompleteAsync(
        long requestId,
        string summary,
        CancellationToken cancellationToken = default) =>
        FinishAsync(requestId, NewsAgentRunRequestStatus.Completed, Normalize(summary, 2000), null, cancellationToken);

    public Task<bool> FailAsync(
        long requestId,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        FinishAsync(requestId, NewsAgentRunRequestStatus.Failed, null, Normalize(errorMessage, 2000), cancellationToken);

    public async Task<bool> ReturnToPendingAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.NewsAgentRunRequests
            .Where(request => request.Id == requestId && request.Status == NewsAgentRunRequestStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, NewsAgentRunRequestStatus.Pending)
                .SetProperty(request => request.RunnerId, (string?)null)
                .SetProperty(request => request.StartedAtUtc, (DateTime?)null)
                .SetProperty(request => request.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken);
        return updated == 1;
    }

    public Task RecordHeartbeatAsync(
        string runnerId,
        CancellationToken cancellationToken = default) =>
        UpsertHeartbeatAsync(Normalize(runnerId, 100), claimed: false, cancellationToken);

    public async Task<IReadOnlyList<NewsAgentRunRequest>> ListRecentAsync(
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        await dbContext.NewsAgentRunRequests
            .AsNoTracking()
            .OrderByDescending(request => request.RequestedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(request => Map(request))
            .ToListAsync(cancellationToken);

    public async Task<NewsAgentRunnerHeartbeat?> GetLatestHeartbeatAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.NewsAgentRunnerHeartbeats
            .AsNoTracking()
            .OrderByDescending(heartbeat => heartbeat.LastSeenAtUtc)
            .Select(heartbeat => new NewsAgentRunnerHeartbeat(
                heartbeat.RunnerId,
                heartbeat.LastSeenAtUtc,
                heartbeat.LastClaimedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> FinishAsync(
        long requestId,
        NewsAgentRunRequestStatus status,
        string? summary,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await dbContext.NewsAgentRunRequests
            .Where(request => request.Id == requestId && request.Status == NewsAgentRunRequestStatus.Running)
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

    private Task<NewsAgentRunRequestEntity?> GetActiveGatheringAsync(CancellationToken cancellationToken) =>
        dbContext.NewsAgentRunRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(request => request.ActiveKey == ActiveKey, cancellationToken);

    private async Task UpsertHeartbeatAsync(
        string runnerId,
        bool claimed,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await dbContext.NewsAgentRunnerHeartbeats
            .Where(heartbeat => heartbeat.RunnerId == runnerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(heartbeat => heartbeat.LastSeenAtUtc, now)
                .SetProperty(
                    heartbeat => heartbeat.LastClaimedAtUtc,
                    heartbeat => claimed ? now : heartbeat.LastClaimedAtUtc),
                cancellationToken);
        if (updated == 1)
        {
            return;
        }

        dbContext.NewsAgentRunnerHeartbeats.Add(new NewsAgentRunnerHeartbeatEntity
        {
            RunnerId = runnerId,
            LastSeenAtUtc = now,
            LastClaimedAtUtc = claimed ? now : null
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            await dbContext.NewsAgentRunnerHeartbeats
                .Where(heartbeat => heartbeat.RunnerId == runnerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(heartbeat => heartbeat.LastSeenAtUtc, now)
                    .SetProperty(
                        heartbeat => heartbeat.LastClaimedAtUtc,
                        heartbeat => claimed ? now : heartbeat.LastClaimedAtUtc),
                    cancellationToken);
        }
    }

    private static NewsAgentRunRequest Map(NewsAgentRunRequestEntity request) =>
        new(
            request.Id,
            request.Status,
            request.Kind,
            request.RequestedBy,
            request.RequestedAtUtc,
            request.ArticleUrl,
            request.GenerateDraft,
            request.RunnerId,
            request.StartedAtUtc,
            request.CompletedAtUtc,
            request.Summary,
            request.ErrorMessage);
}
