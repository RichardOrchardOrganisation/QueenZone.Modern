using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;
using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class EfNewsAgentRunRequestRepository(QueenZoneDbContext dbContext)
    : EfRunRequestRepositoryBase<NewsAgentRunRequestEntity, NewsAgentRunRequestStatus, NewsAgentRunRequest>(dbContext),
        INewsAgentRunRequestRepository
{
    protected override NewsAgentRunRequestStatus Pending => NewsAgentRunRequestStatus.Pending;

    protected override NewsAgentRunRequestStatus Running => NewsAgentRunRequestStatus.Running;

    protected override NewsAgentRunRequestStatus Completed => NewsAgentRunRequestStatus.Completed;

    protected override NewsAgentRunRequestStatus Failed => NewsAgentRunRequestStatus.Failed;

    public async Task<NewsAgentRunRequestQueueResult> QueueAsync(
        NewsAgentRunRequestCreate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isGathering = request.Kind == NewsAgentRunRequestKind.ScheduledGathering;
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
            ActiveKey = isGathering ? ActiveKey : null,
            UpdatedAtUtc = now
        };

        var (queued, wasCreated) = await QueueCoreAsync(entity, singleActive: isGathering, cancellationToken);
        return new NewsAgentRunRequestQueueResult(queued, wasCreated);
    }

    public Task RecordHeartbeatAsync(
        string runnerId,
        CancellationToken cancellationToken = default) =>
        UpsertHeartbeatAsync(Normalize(runnerId, 100), claimed: false, cancellationToken);

    public async Task<NewsAgentRunnerHeartbeat?> GetLatestHeartbeatAsync(
        CancellationToken cancellationToken = default) =>
        await DbContext.NewsAgentRunnerHeartbeats
            .AsNoTracking()
            .OrderByDescending(heartbeat => heartbeat.LastSeenAtUtc)
            .Select(heartbeat => new NewsAgentRunnerHeartbeat(
                heartbeat.RunnerId,
                heartbeat.LastSeenAtUtc,
                heartbeat.LastClaimedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    protected override Task BeforeClaimAsync(string runnerId, CancellationToken cancellationToken) =>
        UpsertHeartbeatAsync(runnerId, claimed: false, cancellationToken);

    protected override Task AfterClaimAsync(string runnerId, CancellationToken cancellationToken) =>
        UpsertHeartbeatAsync(runnerId, claimed: true, cancellationToken);

    private async Task UpsertHeartbeatAsync(
        string runnerId,
        bool claimed,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await DbContext.NewsAgentRunnerHeartbeats
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

        DbContext.NewsAgentRunnerHeartbeats.Add(new NewsAgentRunnerHeartbeatEntity
        {
            RunnerId = runnerId,
            LastSeenAtUtc = now,
            LastClaimedAtUtc = claimed ? now : null
        });
        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            DbContext.ChangeTracker.Clear();
            await DbContext.NewsAgentRunnerHeartbeats
                .Where(heartbeat => heartbeat.RunnerId == runnerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(heartbeat => heartbeat.LastSeenAtUtc, now)
                    .SetProperty(
                        heartbeat => heartbeat.LastClaimedAtUtc,
                        heartbeat => claimed ? now : heartbeat.LastClaimedAtUtc),
                    cancellationToken);
        }
    }

    protected override NewsAgentRunRequest Map(NewsAgentRunRequestEntity request) =>
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
