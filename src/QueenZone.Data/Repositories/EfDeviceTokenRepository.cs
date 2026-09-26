using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfDeviceTokenRepository(QueenZoneDbContext dbContext) : IDeviceTokenRepository
{
    private const int MaxUniqueConflictRetries = 2;

    public async Task<DeviceTokenEntity> UpsertAsync(
        DeviceTokenEntity token,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxUniqueConflictRetries; attempt++)
        {
            var existing = await FindByDeviceIdAsync(token.DeviceId, cancellationToken);
            if (existing is not null)
            {
                existing.MemberAccountId = token.MemberAccountId;
                existing.Platform = token.Platform;
                existing.Token = token.Token;
                existing.UpdatedAt = token.UpdatedAt;
                await dbContext.SaveChangesAsync(cancellationToken);
                return existing;
            }

            dbContext.DeviceTokens.Add(token);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return token;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation() && attempt < MaxUniqueConflictRetries)
            {
                // A concurrent register (or a case-insensitive unique hit the find missed)
                // inserted first. Detach the failed INSERT and update that row.
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException(
            $"Could not upsert device token for DeviceId '{token.DeviceId}'.");
    }

    public async Task<bool> DeleteByDeviceIdAsync(
        Guid memberAccountId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.DeviceTokens
            .Where(row => row.DeviceId == deviceId && row.MemberAccountId == memberAccountId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 1;
    }

    public async Task<IReadOnlyList<DeviceTokenEntity>> ListByMemberIdsAsync(
        IReadOnlyCollection<Guid> memberAccountIds,
        CancellationToken cancellationToken = default)
    {
        if (memberAccountIds.Count == 0)
        {
            return [];
        }

        var ids = memberAccountIds.Distinct().ToArray();
        return await dbContext.DeviceTokens
            .AsNoTracking()
            .Where(row => ids.Contains(row.MemberAccountId))
            .ToListAsync(cancellationToken);
    }

    private Task<DeviceTokenEntity?> FindByDeviceIdAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        // IX_DeviceTokens_DeviceId is case-insensitive on SQL Server. Using ordinal ==
        // here misses "E3C8…" vs "e3c8…" and the following INSERT fails with
        // "Cannot insert duplicate key … IX_DeviceTokens_DeviceId".
        var normalized = deviceId.ToLowerInvariant();
        return dbContext.DeviceTokens
            .SingleOrDefaultAsync(row => row.DeviceId.ToLower() == normalized, cancellationToken);
    }
}
