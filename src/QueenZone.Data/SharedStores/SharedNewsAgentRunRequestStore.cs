namespace QueenZone.Data;

public sealed class SharedNewsAgentRunRequestStore
{
    private readonly object gate = new();
    private readonly List<NewsAgentRunRequest> requests = [];
    private readonly Dictionary<string, NewsAgentRunnerHeartbeat> heartbeats =
        new(StringComparer.OrdinalIgnoreCase);
    private long nextId = 1;

    internal NewsAgentRunRequestQueueResult Queue(NewsAgentRunRequestCreate create)
    {
        ArgumentNullException.ThrowIfNull(create);

        lock (gate)
        {
            if (create.Kind == NewsAgentRunRequestKind.ScheduledGathering)
            {
                var activeGathering = requests.LastOrDefault(request =>
                    request.Kind == NewsAgentRunRequestKind.ScheduledGathering
                    && request.Status is NewsAgentRunRequestStatus.Pending or NewsAgentRunRequestStatus.Running);
                if (activeGathering is not null)
                {
                    return new NewsAgentRunRequestQueueResult(activeGathering, WasCreated: false);
                }
            }

            var request = new NewsAgentRunRequest(
                nextId++,
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
            requests.Add(request);
            return new NewsAgentRunRequestQueueResult(request, WasCreated: true);
        }
    }

    internal NewsAgentRunRequest? ClaimNext(string runnerId)
    {
        lock (gate)
        {
            RecordHeartbeatCore(runnerId, claimed: false);
            var staleBefore = DateTime.UtcNow.AddHours(-3);
            for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                var request = requests[requestIndex];
                if (request.Status == NewsAgentRunRequestStatus.Running
                    && request.StartedAtUtc < staleBefore)
                {
                    requests[requestIndex] = request with
                    {
                        Status = NewsAgentRunRequestStatus.Pending,
                        RunnerId = null,
                        StartedAtUtc = null
                    };
                }
            }

            var index = requests.FindIndex(request => request.Status == NewsAgentRunRequestStatus.Pending);
            if (index < 0)
            {
                return null;
            }

            var claimed = requests[index] with
            {
                Status = NewsAgentRunRequestStatus.Running,
                RunnerId = runnerId,
                StartedAtUtc = DateTime.UtcNow
            };
            requests[index] = claimed;
            RecordHeartbeatCore(runnerId, claimed: true);
            return claimed;
        }
    }

    internal bool Complete(long requestId, string summary) =>
        UpdateRunning(requestId, request => request with
        {
            Status = NewsAgentRunRequestStatus.Completed,
            CompletedAtUtc = DateTime.UtcNow,
            Summary = summary,
            ErrorMessage = null
        });

    internal bool Fail(long requestId, string errorMessage) =>
        UpdateRunning(requestId, request => request with
        {
            Status = NewsAgentRunRequestStatus.Failed,
            CompletedAtUtc = DateTime.UtcNow,
            ErrorMessage = errorMessage
        });

    internal bool ReturnToPending(long requestId) =>
        UpdateRunning(requestId, request => request with
        {
            Status = NewsAgentRunRequestStatus.Pending,
            RunnerId = null,
            StartedAtUtc = null
        });

    internal void RecordHeartbeat(string runnerId)
    {
        lock (gate)
        {
            RecordHeartbeatCore(runnerId, claimed: false);
        }
    }

    internal IReadOnlyList<NewsAgentRunRequest> ListRecent(int limit)
    {
        lock (gate)
        {
            return requests
                .OrderByDescending(request => request.RequestedAtUtc)
                .Take(limit)
                .ToList();
        }
    }

    internal NewsAgentRunnerHeartbeat? GetLatestHeartbeat()
    {
        lock (gate)
        {
            return heartbeats.Values.MaxBy(heartbeat => heartbeat.LastSeenAtUtc);
        }
    }

    private bool UpdateRunning(
        long requestId,
        Func<NewsAgentRunRequest, NewsAgentRunRequest> update)
    {
        lock (gate)
        {
            var index = requests.FindIndex(request =>
                request.Id == requestId && request.Status == NewsAgentRunRequestStatus.Running);
            if (index < 0)
            {
                return false;
            }

            requests[index] = update(requests[index]);
            return true;
        }
    }

    private void RecordHeartbeatCore(string runnerId, bool claimed)
    {
        var now = DateTime.UtcNow;
        heartbeats.TryGetValue(runnerId, out var existing);
        heartbeats[runnerId] = new NewsAgentRunnerHeartbeat(
            runnerId,
            now,
            claimed ? now : existing?.LastClaimedAtUtc);
    }

    internal void Clear()
    {
        lock (gate)
        {
            requests.Clear();
            heartbeats.Clear();
            nextId = 1;
        }
    }
}
