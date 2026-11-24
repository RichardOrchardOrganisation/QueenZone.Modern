namespace QueenZone.Application;

/// <summary>Revokes queued Apple tokens using the host-owned encryption keys.</summary>
public interface IAppleTokenRevoker
{
    Task RevokePendingAsync(CancellationToken cancellationToken = default);
}
