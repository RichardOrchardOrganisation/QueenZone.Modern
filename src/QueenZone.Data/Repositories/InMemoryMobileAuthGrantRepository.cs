using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryMobileAuthGrantRepository(SharedMobileAuthGrantStore store)
    : IMobileAuthGrantRepository
{
    public Task StoreAuthorizationCodeAsync(
        MobileAuthAuthorizationCodeEntity code,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            store.AuthorizationCodes.Add(CloneCode(code));
        }

        return Task.CompletedTask;
    }

    public Task<MobileAuthAuthorizationCodeEntity?> RedeemAuthorizationCodeAsync(
        string codeHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var code = store.AuthorizationCodes.FirstOrDefault(item => item.CodeHash == codeHash);
            if (code is null || code.RedeemedAt is not null || code.ExpiresAt <= utcNow)
            {
                return Task.FromResult<MobileAuthAuthorizationCodeEntity?>(null);
            }

            code.RedeemedAt = utcNow;
            return Task.FromResult<MobileAuthAuthorizationCodeEntity?>(CloneCode(code));
        }
    }

    public Task StoreRefreshTokenAsync(
        MobileAuthRefreshTokenEntity token,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            store.RefreshTokens.Add(CloneRefresh(token));
        }

        return Task.CompletedTask;
    }

    public Task<MobileAuthRefreshTokenEntity?> FindRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var token = store.RefreshTokens.FirstOrDefault(item => item.TokenHash == tokenHash);
            return Task.FromResult(token is null ? null : CloneRefresh(token));
        }
    }

    public Task<bool> TryRevokeRefreshTokenAsync(
        string tokenHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var token = store.RefreshTokens.FirstOrDefault(item => item.TokenHash == tokenHash);
            if (token is null || token.RevokedAt is not null || token.ExpiresAt <= utcNow)
            {
                return Task.FromResult(false);
            }

            token.RevokedAt = utcNow;
            return Task.FromResult(true);
        }
    }

    public Task<int> RevokeAllRefreshTokensForMemberAsync(
        Guid memberAccountId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var count = 0;
            foreach (var token in store.RefreshTokens)
            {
                if (token.MemberAccountId != memberAccountId || token.RevokedAt is not null)
                {
                    continue;
                }

                token.RevokedAt = utcNow;
                count++;
            }

            return Task.FromResult(count);
        }
    }

    public Task<bool> LinkRefreshTokenRotationAsync(
        string oldTokenHash,
        string newTokenHash,
        CancellationToken cancellationToken = default)
    {
        lock (store.Gate)
        {
            var token = store.RefreshTokens.FirstOrDefault(item => item.TokenHash == oldTokenHash);
            if (token is null || token.ReplacedByTokenHash is not null)
            {
                return Task.FromResult(false);
            }

            token.ReplacedByTokenHash = newTokenHash;
            return Task.FromResult(true);
        }
    }

    private static MobileAuthAuthorizationCodeEntity CloneCode(MobileAuthAuthorizationCodeEntity code) =>
        new()
        {
            Id = code.Id,
            CodeHash = code.CodeHash,
            MemberAccountId = code.MemberAccountId,
            ClientId = code.ClientId,
            RedirectUri = code.RedirectUri,
            CodeChallenge = code.CodeChallenge,
            ExpiresAt = code.ExpiresAt,
            CreatedAt = code.CreatedAt,
            RedeemedAt = code.RedeemedAt,
        };

    private static MobileAuthRefreshTokenEntity CloneRefresh(MobileAuthRefreshTokenEntity token) =>
        new()
        {
            Id = token.Id,
            TokenHash = token.TokenHash,
            MemberAccountId = token.MemberAccountId,
            ClientId = token.ClientId,
            ExpiresAt = token.ExpiresAt,
            CreatedAt = token.CreatedAt,
            RevokedAt = token.RevokedAt,
            ReplacedByTokenHash = token.ReplacedByTokenHash,
        };
}
