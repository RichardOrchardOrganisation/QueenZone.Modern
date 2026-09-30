using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class InMemoryNewsAgentRunRequestRepository(SharedNewsAgentRunRequestStore store)
    : InMemoryRunRequestRepositoryBase<NewsAgentRunRequest>(store), INewsAgentRunRequestRepository
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

    public Task RecordHeartbeatAsync(
        string runnerId,
        CancellationToken cancellationToken = default)
    {
        store.RecordHeartbeat(Normalize(runnerId, 100));
        return Task.CompletedTask;
    }

    public Task<NewsAgentRunnerHeartbeat?> GetLatestHeartbeatAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetLatestHeartbeat());
}
