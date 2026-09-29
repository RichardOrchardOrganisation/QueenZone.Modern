using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

/// <summary>Shared claim/complete/fail/list wrappers over an in-memory run-request store.</summary>
public abstract class InMemoryRunRequestRepositoryBase<TRequest>(SharedRunRequestStoreBase<TRequest> store)
    where TRequest : class, IRunRequestRecord<TRequest>
{
    public Task<TRequest?> ClaimNextAsync(
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

    public Task<IReadOnlyList<TRequest>> ListRecentAsync(
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ListRecent(Math.Clamp(limit, 1, 100)));
}
