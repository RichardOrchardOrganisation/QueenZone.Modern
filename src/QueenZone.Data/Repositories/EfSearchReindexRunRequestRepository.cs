using QueenZone.Data.Entities;
using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

public sealed class EfSearchReindexRunRequestRepository(QueenZoneDbContext dbContext)
    : EfRunRequestRepositoryBase<SearchReindexRunRequestEntity, SearchReindexRunRequestStatus>(dbContext),
        ISearchReindexRunRequestRepository
{
    public async Task<SearchReindexRunRequest?> ClaimNextAsync(string runnerId, CancellationToken cancellationToken = default)
    {
        var entity = await ClaimNextEntityAsync(runnerId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<SearchReindexRunRequest>> ListRecentAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        var entities = await ListRecentEntitiesAsync(limit, cancellationToken);
        return entities.ConvertAll(Map);
    }

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
        return new SearchReindexRunRequestQueueResult(Map(queued), wasCreated);
    }

    private static SearchReindexRunRequest Map(SearchReindexRunRequestEntity request) =>
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
