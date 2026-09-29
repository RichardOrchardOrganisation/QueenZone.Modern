using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class InMemoryNewsAgentRunRequestRepository(SharedNewsAgentRunRequestStore store)
    : INewsAgentRunRequestRepository
{
    public Task<NewsAgentRunRequestQueueResult> QueueAsync(
        NewsAgentRunRequestCreate request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Queue(request with
        {
            RequestedBy = Normalize(request.RequestedBy, 256),
            ArticleUrl = string.IsNullOrWhiteSpace(request.ArticleUrl)
                ? null
                : Normalize(request.ArticleUrl, 2000)
        }));

    public Task<NewsAgentRunRequest?> ClaimNextAsync(
        string runnerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ClaimNext(Normalize(runnerId, 100)));

    public Task<bool> CompleteAsync(
        long requestId,
        string summary,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Complete(requestId, Normalize(summary, 2000)));

    public Task<bool> FailAsync(
        long requestId,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Fail(requestId, Normalize(errorMessage, 2000)));

    public Task<bool> ReturnToPendingAsync(
        long requestId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ReturnToPending(requestId));

    public Task RecordHeartbeatAsync(
        string runnerId,
        CancellationToken cancellationToken = default)
    {
        store.RecordHeartbeat(Normalize(runnerId, 100));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NewsAgentRunRequest>> ListRecentAsync(
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ListRecent(Math.Clamp(limit, 1, 100)));

    public Task<NewsAgentRunnerHeartbeat?> GetLatestHeartbeatAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetLatestHeartbeat());
}
