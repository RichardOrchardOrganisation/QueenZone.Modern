namespace QueenZone.Data;

/// <summary>
/// In-memory run-request bookkeeping shared by the news-agent and search-reindex queues.
/// Each queue keeps its own store instance and record type; only the claim/finish rules are shared.
/// </summary>
public abstract class SharedRunRequestStoreBase<TRequest>
    where TRequest : class, IRunRequestRecord<TRequest>
{
    private static readonly TimeSpan StaleRunTimeout = TimeSpan.FromHours(3);

    private readonly List<TRequest> requests = [];
    private long nextId = 1;

    protected object Gate { get; } = new();

    protected long TakeNextId() => nextId++;

    protected TRequest? LastActive(Func<TRequest, bool>? filter = null) =>
        requests.LastOrDefault(request => request.IsActive && (filter?.Invoke(request) ?? true));

    protected void Add(TRequest request) => requests.Add(request);

    internal TRequest? ClaimNext(string runnerId)
    {
        lock (Gate)
        {
            OnClaimAttempt(runnerId);
            var staleBefore = DateTime.UtcNow.Subtract(StaleRunTimeout);
            for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                var request = requests[requestIndex];
                if (request.IsRunning && request.StartedAtUtc < staleBefore)
                {
                    requests[requestIndex] = request.AsPending();
                }
            }

            var index = requests.FindIndex(request => request.IsPending);
            if (index < 0)
            {
                return null;
            }

            var claimed = requests[index].AsRunning(runnerId, DateTime.UtcNow);
            requests[index] = claimed;
            OnClaimed(runnerId);
            return claimed;
        }
    }

    internal bool Complete(long requestId, string summary) =>
        UpdateRunning(requestId, request => request.AsCompleted(DateTime.UtcNow, summary));

    internal bool Fail(long requestId, string errorMessage) =>
        UpdateRunning(requestId, request => request.AsFailed(DateTime.UtcNow, errorMessage));

    internal bool ReturnToPending(long requestId) =>
        UpdateRunning(requestId, request => request.AsPending());

    internal IReadOnlyList<TRequest> ListRecent(int limit)
    {
        lock (Gate)
        {
            return requests
                .OrderByDescending(request => request.RequestedAtUtc)
                .Take(limit)
                .ToList();
        }
    }

    /// <summary>Called under the lock before any request is claimed.</summary>
    protected virtual void OnClaimAttempt(string runnerId)
    {
    }

    /// <summary>Called under the lock after a request was claimed.</summary>
    protected virtual void OnClaimed(string runnerId)
    {
    }

    protected void ClearRequests()
    {
        requests.Clear();
        nextId = 1;
    }

    private bool UpdateRunning(long requestId, Func<TRequest, TRequest> update)
    {
        lock (Gate)
        {
            var index = requests.FindIndex(request => request.Id == requestId && request.IsRunning);
            if (index < 0)
            {
                return false;
            }

            requests[index] = update(requests[index]);
            return true;
        }
    }
}
