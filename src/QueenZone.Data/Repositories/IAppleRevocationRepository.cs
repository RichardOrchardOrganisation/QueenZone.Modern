namespace QueenZone.Data;

/// <summary>
/// Protected-token persistence for Apple revocation. Account deletion owns the purge transaction;
/// revocation consumes retained logins only after personal data has been purged.
/// </summary>
public interface IAppleRevocationRepository
{
    Task SaveAppleRefreshTokenAsync(
        Guid memberAccountId,
        string providerKey,
        string protectedToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingAppleRevocation>> ListPendingAppleRevocationsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task CompleteAppleRevocationAsync(Guid externalLoginId, CancellationToken cancellationToken = default);
}
