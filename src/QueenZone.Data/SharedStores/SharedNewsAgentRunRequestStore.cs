namespace QueenZone.Data;

public sealed class SharedNewsAgentRunRequestStore : SharedRunRequestStoreBase<NewsAgentRunRequest>
{
    private readonly Dictionary<string, NewsAgentRunnerHeartbeat> heartbeats =
        new(StringComparer.OrdinalIgnoreCase);

    internal NewsAgentRunRequestQueueResult Queue(NewsAgentRunRequestCreate create)
    {
        ArgumentNullException.ThrowIfNull(create);

        lock (Gate)
        {
            if (create.Kind == NewsAgentRunRequestKind.ScheduledGathering)
            {
                var activeGathering = LastActive(request => request.Kind == NewsAgentRunRequestKind.ScheduledGathering);
                if (activeGathering is not null)
                {
                    return new NewsAgentRunRequestQueueResult(activeGathering, WasCreated: false);
                }
            }

            var request = new NewsAgentRunRequest(
                TakeNextId(),
                NewsAgentRunRequestStatus.Pending,
                create.Kind,
                create.RequestedBy,
                DateTime.UtcNow,
                create.ArticleUrl,
                create.GenerateDraft,
                RunnerId: null,
                StartedAtUtc: null,
                CompletedAtUtc: null,
                Summary: null,
                ErrorMessage: null);
            Add(request);
            return new NewsAgentRunRequestQueueResult(request, WasCreated: true);
        }
    }

    internal void RecordHeartbeat(string runnerId)
    {
        lock (Gate)
        {
            RecordHeartbeatCore(runnerId, claimed: false);
        }
    }

    internal NewsAgentRunnerHeartbeat? GetLatestHeartbeat()
    {
        lock (Gate)
        {
            return heartbeats.Values.MaxBy(heartbeat => heartbeat.LastSeenAtUtc);
        }
    }

    internal void Clear()
    {
        lock (Gate)
        {
            ClearRequests();
            heartbeats.Clear();
        }
    }

    protected override void OnClaimAttempt(string runnerId) => RecordHeartbeatCore(runnerId, claimed: false);

    protected override void OnClaimed(string runnerId) => RecordHeartbeatCore(runnerId, claimed: true);

    private void RecordHeartbeatCore(string runnerId, bool claimed)
    {
        var now = DateTime.UtcNow;
        heartbeats.TryGetValue(runnerId, out var existing);
        heartbeats[runnerId] = new NewsAgentRunnerHeartbeat(
            runnerId,
            now,
            claimed ? now : existing?.LastClaimedAtUtc);
    }
}
