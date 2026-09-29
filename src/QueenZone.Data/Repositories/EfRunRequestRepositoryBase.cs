using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using QueenZone.Data.Entities;
using static QueenZone.Data.RunRequestText;

namespace QueenZone.Data;

/// <summary>
/// Shared claim/complete/fail/list plumbing for the run-request queues. Each queue keeps its own table,
/// entity, request record and enum; subclasses supply the enum values, the mapping and the queue rules.
/// Properties are addressed with <see cref="EF.Property{TProperty}"/> so the shared code stays generic.
/// </summary>
public abstract class EfRunRequestRepositoryBase<TEntity, TStatus, TRequest>(QueenZoneDbContext dbContext)
    where TEntity : class, IRunRequestEntity<TStatus>
    where TStatus : struct, Enum
{
    protected const string ActiveKey = "active";

    private static readonly TimeSpan StaleRunTimeout = TimeSpan.FromHours(3);

    private static readonly Expression<Func<TEntity, TStatus>> StatusProperty =
        Property<TStatus>(nameof(IRunRequestEntity<TStatus>.Status));

    private static readonly Expression<Func<TEntity, string?>> RunnerProperty =
        Property<string?>(nameof(IRunRequestEntity.RunnerId));

    private static readonly Expression<Func<TEntity, DateTime?>> StartedProperty =
        Property<DateTime?>(nameof(IRunRequestEntity<TStatus>.StartedAtUtc));

    private static readonly Expression<Func<TEntity, DateTime>> UpdatedProperty =
        Property<DateTime>(nameof(IRunRequestEntity.UpdatedAtUtc));

    protected abstract TStatus Pending { get; }

    protected abstract TStatus Running { get; }

    protected abstract TStatus Completed { get; }

    protected abstract TStatus Failed { get; }

    protected QueenZoneDbContext DbContext => dbContext;

    protected DbSet<TEntity> Requests => dbContext.Set<TEntity>();

    public async Task<TRequest?> ClaimNextAsync(
        string runnerId,
        CancellationToken cancellationToken = default)
    {
        runnerId = Normalize(runnerId, 100);
        await BeforeClaimAsync(runnerId, cancellationToken);

        var now = DateTime.UtcNow;
        var staleBefore = now.Subtract(StaleRunTimeout);
        await Requests
            .Where(StatusIs(Running))
            .Where(request => EF.Property<DateTime>(request, nameof(IRunRequestEntity.UpdatedAtUtc)) < staleBefore)
            .ExecuteUpdateAsync(setters => ResetToPending(setters).SetProperty(UpdatedProperty, now), cancellationToken);

        while (true)
        {
            var requestId = await Requests
                .AsNoTracking()
                .Where(StatusIs(Pending))
                .OrderBy(request => EF.Property<DateTime>(request, nameof(IRunRequestEntity.RequestedAtUtc)))
                .Select(request => (long?)EF.Property<long>(request, nameof(IRunRequestEntity.Id)))
                .FirstOrDefaultAsync(cancellationToken);
            if (requestId is null)
            {
                return default;
            }

            now = DateTime.UtcNow;
            var updated = await Requests
                .Where(IdIs(requestId.Value))
                .Where(StatusIs(Pending))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(StatusProperty, Running)
                    .SetProperty(RunnerProperty, runnerId)
                    .SetProperty(StartedProperty, now)
                    .SetProperty(UpdatedProperty, now),
                    cancellationToken);
            if (updated == 0)
            {
                continue;
            }

            await AfterClaimAsync(runnerId, cancellationToken);
            var claimed = await Requests
                .AsNoTracking()
                .SingleAsync(IdIs(requestId.Value), cancellationToken);
            return Map(claimed);
        }
    }

    public Task<bool> CompleteAsync(
        long requestId,
        string summary,
        CancellationToken cancellationToken = default) =>
        FinishAsync(requestId, Completed, Normalize(summary, 2000), null, cancellationToken);

    public Task<bool> FailAsync(
        long requestId,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        FinishAsync(requestId, Failed, null, Normalize(errorMessage, 2000), cancellationToken);

    public async Task<bool> ReturnToPendingAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var updated = await Requests
            .Where(IdIs(requestId))
            .Where(StatusIs(Running))
            .ExecuteUpdateAsync(
                setters => ResetToPending(setters).SetProperty(UpdatedProperty, DateTime.UtcNow),
                cancellationToken);
        return updated == 1;
    }

    public async Task<IReadOnlyList<TRequest>> ListRecentAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Map is an instance method, so EF cannot project through it; materialise the entities first.
        var entities = await Requests
            .AsNoTracking()
            .OrderByDescending(request => EF.Property<DateTime>(request, nameof(IRunRequestEntity.RequestedAtUtc)))
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return entities.ConvertAll(Map);
    }

    protected abstract TRequest Map(TEntity entity);

    protected virtual Task BeforeClaimAsync(string runnerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected virtual Task AfterClaimAsync(string runnerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// Adds <paramref name="entity"/>. When <paramref name="singleActive"/> is set, an existing active request
    /// (or one that wins the unique-index race) is returned instead of creating another.
    /// </summary>
    protected async Task<(TRequest Request, bool WasCreated)> QueueCoreAsync(
        TEntity entity,
        bool singleActive,
        CancellationToken cancellationToken)
    {
        if (singleActive)
        {
            var active = await GetActiveAsync(cancellationToken);
            if (active is not null)
            {
                return (Map(active), false);
            }
        }

        Requests.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return (Map(entity), true);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            if (!singleActive)
            {
                throw;
            }

            var raced = await GetActiveAsync(cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return (Map(raced), false);
        }
    }

    private static Expression<Func<TEntity, TProperty>> Property<TProperty>(string name) =>
        request => EF.Property<TProperty>(request, name);

    private static Expression<Func<TEntity, bool>> IdIs(long id) =>
        request => EF.Property<long>(request, nameof(IRunRequestEntity.Id)) == id;

    // Generic enums cannot use ==, so build the equality by hand.
    private static Expression<Func<TEntity, bool>> StatusIs(TStatus status)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "request");
        var body = Expression.Equal(
            Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                [typeof(TStatus)],
                parameter,
                Expression.Constant(nameof(IRunRequestEntity<TStatus>.Status))),
            Expression.Constant(status, typeof(TStatus)));
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private async Task<bool> FinishAsync(
        long requestId,
        TStatus status,
        string? summary,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await Requests
            .Where(IdIs(requestId))
            .Where(StatusIs(Running))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(StatusProperty, status)
                .SetProperty(Property<DateTime?>(nameof(IRunRequestEntity<TStatus>.CompletedAtUtc)), now)
                .SetProperty(Property<string?>(nameof(IRunRequestEntity.Summary)), summary)
                .SetProperty(Property<string?>(nameof(IRunRequestEntity.ErrorMessage)), errorMessage)
                .SetProperty(Property<string?>(nameof(IRunRequestEntity.ActiveKey)), (string?)null)
                .SetProperty(UpdatedProperty, now),
                cancellationToken);
        return updated == 1;
    }

    private Task<TEntity?> GetActiveAsync(CancellationToken cancellationToken) =>
        Requests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                request => EF.Property<string?>(request, nameof(IRunRequestEntity.ActiveKey)) == ActiveKey,
                cancellationToken);

    private UpdateSettersBuilder<TEntity> ResetToPending(UpdateSettersBuilder<TEntity> setters) =>
        setters
            .SetProperty(StatusProperty, Pending)
            .SetProperty(RunnerProperty, (string?)null)
            .SetProperty(StartedProperty, (DateTime?)null);
}
