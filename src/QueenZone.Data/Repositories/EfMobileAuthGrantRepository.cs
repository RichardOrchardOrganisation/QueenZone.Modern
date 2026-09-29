using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfMobileAuthGrantRepository(QueenZoneDbContext dbContext) : IMobileAuthGrantRepository
{
    public async Task StoreAuthorizationCodeAsync(
        MobileAuthAuthorizationCodeEntity code,
        CancellationToken cancellationToken = default)
    {
        dbContext.MobileAuthAuthorizationCodes.Add(code);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MobileAuthAuthorizationCodeEntity?> RedeemAuthorizationCodeAsync(
        string codeHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.MobileAuthAuthorizationCodes
            .Where(code => code.CodeHash == codeHash && code.RedeemedAt == null && code.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(code => code.RedeemedAt, utcNow),
                cancellationToken);

        if (updated != 1)
        {
            return null;
        }

        return await dbContext.MobileAuthAuthorizationCodes
            .AsNoTracking()
            .SingleAsync(code => code.CodeHash == codeHash, cancellationToken);
    }

    public async Task StoreRefreshTokenAsync(
        MobileAuthRefreshTokenEntity token,
        CancellationToken cancellationToken = default)
    {
        dbContext.MobileAuthRefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MobileAuthRefreshTokenEntity?> FindRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        await dbContext.MobileAuthRefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> TryRevokeRefreshTokenAsync(
        string tokenHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.MobileAuthRefreshTokens
            .Where(token => token.TokenHash == tokenHash && token.RevokedAt == null && token.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, utcNow),
                cancellationToken);

        return updated == 1;
    }

    public Task<bool> TryRotateRefreshTokenAsync(
        string oldTokenHash,
        MobileAuthRefreshTokenEntity replacement,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        QueenZoneDbTransactions.ExecuteAsync(
            dbContext,
            async ct =>
            {
                // The conditional UPDATE takes the row lock, so a concurrent rotation
                // of the same token waits here and then matches zero rows.
                var revoked = await dbContext.MobileAuthRefreshTokens
                    .Where(token => token.TokenHash == oldTokenHash
                        && token.RevokedAt == null
                        && token.ExpiresAt > utcNow)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(token => token.RevokedAt, utcNow)
                            .SetProperty(token => token.ReplacedByTokenHash, replacement.TokenHash),
                        ct);
                if (revoked != 1)
                {
                    return false;
                }

                dbContext.MobileAuthRefreshTokens.Add(replacement);
                await dbContext.SaveChangesAsync(ct);
                dbContext.Entry(replacement).State = EntityState.Detached;
                return true;
            },
            cancellationToken);

    public async Task<int> RevokeAllRefreshTokensForMemberAsync(
        Guid memberAccountId,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        await dbContext.MobileAuthRefreshTokens
            .Where(token => token.MemberAccountId == memberAccountId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, utcNow),
                cancellationToken);
}
