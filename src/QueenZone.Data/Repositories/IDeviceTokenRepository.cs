using QueenZone.Data.Entities;

namespace QueenZone.Data;

public interface IDeviceTokenRepository
{
    /// <summary>
    /// Inserts a new device token, or updates the existing row for
    /// <see cref="DeviceTokenEntity.DeviceId"/> (owner, platform, token, timestamp) when one
    /// already exists. Returns the stored entity.
    /// </summary>
    Task<DeviceTokenEntity> UpsertAsync(
        DeviceTokenEntity token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the device token for <paramref name="deviceId"/> when it is owned by
    /// <paramref name="memberAccountId"/>. Returns false when no such row exists (unknown
    /// device, or owned by a different member).
    /// </summary>
    Task<bool> DeleteByDeviceIdAsync(
        Guid memberAccountId,
        string deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a token only if the registration ID, version, member, platform, and
    /// token still match the provider-rejected registration. A concurrent refresh remains intact.
    /// </summary>
    Task<bool> DeleteIfTokenMatchesAsync(
        Guid registrationId,
        DateTime updatedAt,
        Guid memberAccountId,
        DevicePushPlatform platform,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every stored device token for the given members. A member with two
    /// devices yields two rows. Empty input returns an empty list.
    /// </summary>
    Task<IReadOnlyList<DeviceTokenEntity>> ListByMemberIdsAsync(
        IReadOnlyCollection<Guid> memberAccountIds,
        CancellationToken cancellationToken = default);
}
