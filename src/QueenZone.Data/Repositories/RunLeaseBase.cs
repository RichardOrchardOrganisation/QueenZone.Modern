using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>Shared lease handle. Feature-specific leases derive so they can expose their own interface.</summary>
public abstract class RunLeaseBase(string leaseName, string holderId) : IAsyncDisposable
{
    public string LeaseName { get; } = leaseName;

    public string HolderId { get; } = holderId;

    public abstract ValueTask DisposeAsync();
}

public abstract class InMemoryRunLeaseServiceBase<TLease>(SharedLeaseStore store)
    where TLease : class, IAsyncDisposable
{
    public Task<TLease?> TryAcquireAsync(
        string leaseName,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var holderId = Guid.NewGuid().ToString("N");
        var expiresAtUtc = DateTime.UtcNow.Add(duration);
        return Task.FromResult(store.TryAcquire(leaseName, holderId, expiresAtUtc)
            ? CreateLease(store, leaseName, holderId)
            : null);
    }

    protected abstract TLease CreateLease(SharedLeaseStore store, string leaseName, string holderId);
}

public abstract class InMemoryRunLease(SharedLeaseStore store, string leaseName, string holderId)
    : RunLeaseBase(leaseName, holderId)
{
    public override ValueTask DisposeAsync()
    {
        store.Release(LeaseName, HolderId);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Shared SQL lease acquisition. Each feature keeps its own table.</summary>
#pragma warning disable EF1002 // tableName is a constant supplied by the derived service, never user input.
public abstract class EfRunLeaseServiceBase<TLease, TEntity>(QueenZoneDbContext dbContext, string tableName)
    where TLease : class, IAsyncDisposable
    where TEntity : class, ILeaseEntity, new()
{
    public async Task<TLease?> TryAcquireAsync(
        string leaseName,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var holderId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(duration);

        var updated = await dbContext.Database.ExecuteSqlRawAsync(
            $@"
UPDATE {tableName}
SET HolderId = {{0}}, AcquiredAtUtc = {{1}}, ExpiresAtUtc = {{2}}
WHERE LeaseName = {{3}}
  AND (ExpiresAtUtc <= {{1}} OR HolderId = {{0}})",
            [holderId, now, expiresAtUtc, leaseName],
            cancellationToken);

        if (updated > 0)
        {
            return CreateLease(leaseName, holderId);
        }

        var leases = dbContext.Set<TEntity>();
        var leaseExists = await leases.AnyAsync(lease => lease.LeaseName == leaseName, cancellationToken);
        if (leaseExists)
        {
            return null;
        }

        try
        {
            leases.Add(new TEntity
            {
                LeaseName = leaseName,
                HolderId = holderId,
                AcquiredAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return CreateLease(leaseName, holderId);
        }
        catch (DbUpdateException)
        {
            return null;
        }
    }

    internal async Task ReleaseAsync(string leaseName, string holderId, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            $"UPDATE {tableName} SET ExpiresAtUtc = {{0}} WHERE LeaseName = {{1}} AND HolderId = {{2}}",
            [DateTime.UtcNow, leaseName, holderId],
            cancellationToken);
    }

    protected abstract TLease CreateLease(string leaseName, string holderId);
}

#pragma warning restore EF1002

public abstract class EfRunLease<TLease, TEntity>(
    EfRunLeaseServiceBase<TLease, TEntity> service,
    string leaseName,
    string holderId) : RunLeaseBase(leaseName, holderId)
    where TLease : class, IAsyncDisposable
    where TEntity : class, ILeaseEntity, new()
{
    public override async ValueTask DisposeAsync() => await service.ReleaseAsync(LeaseName, HolderId);
}
