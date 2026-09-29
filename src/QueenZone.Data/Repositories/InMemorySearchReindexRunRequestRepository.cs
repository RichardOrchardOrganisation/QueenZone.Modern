using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class InMemorySearchReindexRunRequestRepository(SharedSearchReindexRunRequestStore store)
    : InMemoryRunRequestRepositoryBase<SearchReindexRunRequest>(store), ISearchReindexRunRequestRepository
{
    public Task<SearchReindexRunRequestQueueResult> QueueAsync(
        SearchReindexRunRequestCreate request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Queue(request with
        {
            RequestedBy = Normalize(request.RequestedBy, 256)
        }));
}
