namespace QueenZone.Data;

public sealed class SharedSearchReindexRunRequestStore : SharedRunRequestStoreBase<SearchReindexRunRequest>
{
    internal SearchReindexRunRequestQueueResult Queue(SearchReindexRunRequestCreate create)
    {
        ArgumentNullException.ThrowIfNull(create);

        lock (Gate)
        {
            var active = LastActive();
            if (active is not null)
            {
                return new SearchReindexRunRequestQueueResult(active, WasCreated: false);
            }

            var request = new SearchReindexRunRequest(
                TakeNextId(),
                SearchReindexRunRequestStatus.Pending,
                create.RequestedBy,
                DateTime.UtcNow,
                RunnerId: null,
                StartedAtUtc: null,
                CompletedAtUtc: null,
                Summary: null,
                ErrorMessage: null);
            Add(request);
            return new SearchReindexRunRequestQueueResult(request, WasCreated: true);
        }
    }
}
