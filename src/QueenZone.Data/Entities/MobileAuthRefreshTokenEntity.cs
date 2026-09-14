using System.Diagnostics.CodeAnalysis;

namespace QueenZone.Data.Entities;

/// <summary>
/// Opaque refresh token issued to a mobile client. The raw token is never stored;
/// only <see cref="TokenHash"/> is persisted so the value can be revoked later.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MobileAuthRefreshTokenEntity
{
    public Guid Id { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public Guid MemberAccountId { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Hash of the token this grant was rotated into. Lets a replay of an
    /// already-rotated token be traced forward to the grant that is actually
    /// still active, so a client that lost its rotation response (killed mid
    /// launch, dropped connection, ...) can be told apart from a stolen token.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    public MemberAccount? MemberAccount { get; set; }
}
