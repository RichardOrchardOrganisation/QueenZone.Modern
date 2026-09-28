using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryDeviceTokenRepository(SharedDeviceTokenStore store) : IDeviceTokenRepository
{
    public Task<DeviceTokenEntity> UpsertAsync(
        DeviceTokenEntity token,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var existing = store.Tokens.FirstOrDefault(row => SameDeviceId(row.DeviceId, token.DeviceId));
            if (existing is null)
            {
                var stored = Clone(token);
                store.Tokens.Add(stored);
                return Task.FromResult(Clone(stored));
            }

            existing.MemberAccountId = token.MemberAccountId;
            existing.Platform = token.Platform;
            existing.Token = token.Token;
            existing.UpdatedAt = token.UpdatedAt;
            return Task.FromResult(Clone(existing));
        }
    }

    public Task<bool> DeleteByDeviceIdAsync(
        Guid memberAccountId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var existing = store.Tokens.FirstOrDefault(
                row => row.DeviceId == deviceId && row.MemberAccountId == memberAccountId);
            if (existing is null)
            {
                return Task.FromResult(false);
            }

            store.Tokens.Remove(existing);
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteIfTokenMatchesAsync(
        Guid registrationId,
        DateTime updatedAt,
        Guid memberAccountId,
        DevicePushPlatform platform,
        string token,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var removed = store.Tokens.RemoveAll(row =>
                row.Id == registrationId
                && row.UpdatedAt == updatedAt
                && row.MemberAccountId == memberAccountId
                && row.Platform == platform
                && row.Token == token);
            return Task.FromResult(removed > 0);
        }
    }

    public Task<IReadOnlyList<DeviceTokenEntity>> ListByMemberIdsAsync(
        IReadOnlyCollection<Guid> memberAccountIds,
        CancellationToken cancellationToken = default)
    {
        if (memberAccountIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<DeviceTokenEntity>>([]);
        }

        lock (store.Gate)
        {
            var ids = memberAccountIds.ToHashSet();
            var matches = store.Tokens
                .Where(row => ids.Contains(row.MemberAccountId))
                .Select(Clone)
                .ToList();
            return Task.FromResult<IReadOnlyList<DeviceTokenEntity>>(matches);
        }
    }

    private static bool SameDeviceId(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static DeviceTokenEntity Clone(DeviceTokenEntity token) =>
        new()
        {
            Id = token.Id,
            DeviceId = token.DeviceId,
            MemberAccountId = token.MemberAccountId,
            Platform = token.Platform,
            Token = token.Token,
            CreatedAt = token.CreatedAt,
            UpdatedAt = token.UpdatedAt,
        };
}
