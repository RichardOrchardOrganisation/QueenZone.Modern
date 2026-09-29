using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class InMemorySearchReindexRunRequestRepository(SharedSearchReindexRunRequestStore store)
    : ISearchReindexRunRequestRepository
{
    public Task<SearchReindexRunRequestQueueResult> QueueAsync(
        SearchReindexRunRequestCreate request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Queue(request with
        {
            RequestedBy = Normalize(request.RequestedBy, 256)
        }));

    public Task<SearchReindexRunRequest?> ClaimNextAsync(
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

    public Task<IReadOnlyList<SearchReindexRunRequest>> ListRecentAsync(
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ListRecent(Math.Clamp(limit, 1, 100)));
}
