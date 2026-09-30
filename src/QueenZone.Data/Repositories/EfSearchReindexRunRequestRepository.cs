using QueenZone.Data.Entities;
using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class EfSearchReindexRunRequestRepository(QueenZoneDbContext dbContext)
    : EfRunRequestRepositoryBase<SearchReindexRunRequestEntity, SearchReindexRunRequestStatus, SearchReindexRunRequest>(dbContext),
        ISearchReindexRunRequestRepository
{
    protected override SearchReindexRunRequestStatus Pending => SearchReindexRunRequestStatus.Pending;

    protected override SearchReindexRunRequestStatus Running => SearchReindexRunRequestStatus.Running;

    protected override SearchReindexRunRequestStatus Completed => SearchReindexRunRequestStatus.Completed;

    protected override SearchReindexRunRequestStatus Failed => SearchReindexRunRequestStatus.Failed;

    public async Task<SearchReindexRunRequestQueueResult> QueueAsync(
        SearchReindexRunRequestCreate request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var entity = new SearchReindexRunRequestEntity
        {
            Status = SearchReindexRunRequestStatus.Pending,
            RequestedBy = Normalize(request.RequestedBy, 256),
            RequestedAtUtc = now,
            ActiveKey = ActiveKey,
            UpdatedAtUtc = now
        };

        var (queued, wasCreated) = await QueueCoreAsync(entity, singleActive: true, cancellationToken);
        return new SearchReindexRunRequestQueueResult(queued, wasCreated);
    }

    protected override SearchReindexRunRequest Map(SearchReindexRunRequestEntity request) =>
        new(
            request.Id,
            request.Status,
            request.RequestedBy,
            request.RequestedAtUtc,
            request.RunnerId,
            request.StartedAtUtc,
            request.CompletedAtUtc,
            request.Summary,
            request.ErrorMessage);
}
