using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>Persists protected Apple tokens and consumes revocations only after the account purge commits.</summary>
internal sealed class EfAppleRevocationRepository(QueenZoneDbContext dbContext) : IAppleRevocationRepository
{
    public Task SaveAppleRefreshTokenAsync(
        Guid memberAccountId,
        string providerKey,
        string protectedToken,
        CancellationToken cancellationToken = default) =>
        dbContext.MemberExternalLogins
            .Where(login => login.MemberAccountId == memberAccountId
                && login.Provider == "Apple"
                && login.ProviderKey == providerKey)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(login => login.AppleRefreshTokenProtected, protectedToken), cancellationToken);

    public async Task<IReadOnlyList<PendingAppleRevocation>> ListPendingAppleRevocationsAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.MemberExternalLogins
            .AsNoTracking()
            .Where(login => login.Provider == "Apple"
                && login.AppleRefreshTokenProtected != null
                && dbContext.MemberAccounts.Any(account =>
                    account.Id == login.MemberAccountId && account.PersonalDataPurgedAt != null))
            .OrderBy(login => login.LinkedAt)
            .Take(limit)
            .Select(login => new PendingAppleRevocation(login.Id, login.AppleRefreshTokenProtected!))
            .ToListAsync(cancellationToken);

    public Task CompleteAppleRevocationAsync(Guid externalLoginId, CancellationToken cancellationToken = default) =>
        dbContext.MemberExternalLogins
            .Where(login => login.Id == externalLoginId
                && login.Provider == "Apple"
                && dbContext.MemberAccounts.Any(account =>
                    account.Id == login.MemberAccountId && account.PersonalDataPurgedAt != null))
            .ExecuteDeleteAsync(cancellationToken);
}
