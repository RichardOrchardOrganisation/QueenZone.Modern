using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>Uses the account repository's state and lock so purge and revocation observe the same lifecycle.</summary>
internal sealed class InMemoryAppleRevocationRepository(
    IReadOnlyCollection<MemberAccount> accounts,
    List<MemberExternalLogin> externalLogins,
    Lock gate) : IAppleRevocationRepository
{
    public Task SaveAppleRefreshTokenAsync(
        Guid memberAccountId,
        string providerKey,
        string protectedToken,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var login = externalLogins.FirstOrDefault(login => login.MemberAccountId == memberAccountId
                && login.Provider == "Apple" && login.ProviderKey == providerKey);
            if (login is not null)
            {
                login.AppleRefreshTokenProtected = protectedToken;
            }
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<PendingAppleRevocation>> ListPendingAppleRevocationsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<PendingAppleRevocation> items = externalLogins
                .Where(login => login.Provider == "Apple"
                    && login.AppleRefreshTokenProtected is not null
                    && accounts.Any(account => account.Id == login.MemberAccountId
                        && account.PersonalDataPurgedAt is not null))
                .OrderBy(login => login.LinkedAt)
                .Take(limit)
                .Select(login => new PendingAppleRevocation(login.Id, login.AppleRefreshTokenProtected!))
                .ToList();
            return Task.FromResult(items);
        }
    }

    public Task CompleteAppleRevocationAsync(Guid externalLoginId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            externalLogins.RemoveAll(login => login.Id == externalLoginId
                && login.Provider == "Apple"
                && accounts.Any(account => account.Id == login.MemberAccountId
                    && account.PersonalDataPurgedAt is not null));
            return Task.CompletedTask;
        }
    }
}
