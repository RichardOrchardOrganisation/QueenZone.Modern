using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed class MobileAuthGrantAccountAccess(
    MemberAccountService memberAccountService,
    MobileAuthAccountRateLimiter accountRateLimiter)
{
    public sealed record AccountCheck(MemberAccount? Account, bool RateLimited);

    public async Task<MemberAccount?> FindForAuthorizationCodeAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var account = await memberAccountService.FindByIdAsync(memberId, cancellationToken);
        return account?.IsSuspended == true ? null : account;
    }

    public async Task<AccountCheck> CheckRefreshAsync(Guid memberId, CancellationToken cancellationToken)
    {
        if (!accountRateLimiter.IsAllowed(memberId))
        {
            return new AccountCheck(null, true);
        }

        var account = await memberAccountService.FindByIdAsync(memberId, cancellationToken);
        return new AccountCheck(account, false);
    }

    public async Task<MemberAccount?> FindForReplayAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var account = await memberAccountService.FindByIdAsync(memberId, cancellationToken);
        return account is null || account.IsSuspended || account.DeletionRequestedAt is not null ? null : account;
    }

    public async Task<AccountCheck> CheckPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        var signIn = await memberAccountService.SignInAsync(username, password, cancellationToken);
        if (!signIn.Succeeded || signIn.Account is null)
        {
            return new AccountCheck(null, false);
        }

        return new AccountCheck(signIn.Account, !accountRateLimiter.IsAllowed(signIn.Account.Id));
    }

    public bool IsAuthorizationCodeAllowed(Guid memberId) => accountRateLimiter.IsAllowed(memberId);
}
