using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfMemberAccountRepository(QueenZoneDbContext dbContext) : IMemberAccountRepository
{
    public async Task<MemberAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await dbContext.MemberAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.NormalizedEmail == Normalize(email), cancellationToken);

    public async Task<MemberAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.MemberAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> ListDisplayNamesAsync(
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken = default)
    {
        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(account => ids.Contains(account.Id))
            .Select(account => new { account.Id, account.DisplayName })
            .ToDictionaryAsync(account => account.Id, account => account.DisplayName, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ListActiveMemberIdsAsync(
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken = default)
    {
        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var active = await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(account => ids.Contains(account.Id) && account.DeletionRequestedAt == null)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);
        return active.ToHashSet();
    }

    public async Task<MemberAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken = default)
    {
        var login = await dbContext.MemberExternalLogins
            .AsNoTracking()
            .SingleOrDefaultAsync(l => l.Provider == provider && l.ProviderKey == providerKey, cancellationToken);

        return login is null ? null : await FindByIdAsync(login.MemberAccountId, cancellationToken);
    }

    public async Task<MemberAccount> CreateAsync(MemberAccount account, CancellationToken cancellationToken = default)
    {
        account.NormalizedEmail = Normalize(account.Email);
        dbContext.MemberAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task AddExternalLoginAsync(Guid memberAccountId, string provider, string providerKey, string email, CancellationToken cancellationToken = default)
    {
        dbContext.MemberExternalLogins.Add(new MemberExternalLogin
        {
            Id = Guid.NewGuid(),
            MemberAccountId = memberAccountId,
            Provider = provider,
            ProviderKey = providerKey,
            Email = email,
            LinkedAt = DateTime.UtcNow,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyList<string>> ListExternalProvidersAsync(Guid memberAccountId, CancellationToken cancellationToken = default) =>
        await dbContext.MemberExternalLogins
            .AsNoTracking()
            .Where(login => login.MemberAccountId == memberAccountId)
            .Select(login => login.Provider)
            .Distinct()
            .OrderBy(provider => provider)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberSocialLink>> ListSocialLinksAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.MemberSocialLinks
            .AsNoTracking()
            .Where(row => row.MemberId == memberId)
            .ToListAsync(cancellationToken);

        return MemberSocialChannels.All
            .Select(channel =>
            {
                var key = MemberSocialChannels.ToKey(channel);
                var row = rows.FirstOrDefault(candidate =>
                    string.Equals(candidate.Channel, key, StringComparison.Ordinal));
                return row is null ? null : new MemberSocialLink(channel, row.Url);
            })
            .OfType<MemberSocialLink>()
            .ToList();
    }

    public async Task ReplaceSocialLinksAsync(
        Guid memberId,
        IReadOnlyList<MemberSocialLink> links,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.MemberSocialLinks
            .Where(row => row.MemberId == memberId)
            .ToListAsync(cancellationToken);
        dbContext.MemberSocialLinks.RemoveRange(existing);

        foreach (var link in links)
        {
            dbContext.MemberSocialLinks.Add(new MemberSocialLinkEntity
            {
                MemberId = memberId,
                Channel = MemberSocialChannels.ToKey(link.Channel),
                Url = link.Url,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MemberAccount?> UpdateDisplayNameAsync(Guid memberId, string displayName, CancellationToken cancellationToken = default)
    {
        // Load tracked so change detection persists the new name.
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.DisplayName = displayName;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MemberAccount?> UpdateAvatarUrlAsync(Guid memberId, string? avatarBlobPath, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.AvatarUrl = avatarBlobPath;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MemberAccount?> UpdateMessagePrivacyAsync(
        Guid memberId,
        MemberMessagePrivacy messagePrivacy,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.MessagePrivacy = messagePrivacy;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MemberAccount?> FindByLinkedLegacyUserIdAsync(
        int legacyUserId,
        CancellationToken cancellationToken = default) =>
        await dbContext.MemberAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.LinkedLegacyUserId == legacyUserId, cancellationToken);

    public async Task<MemberAccount?> LinkLegacyUserIdAsync(
        Guid memberId,
        int legacyUserId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (account.LinkedLegacyUserId is not null)
        {
            return account;
        }

        account.LinkedLegacyUserId = legacyUserId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MemberAccount?> UnlinkLegacyUserIdAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (account.LinkedLegacyUserId is not null)
        {
            account.LinkedLegacyUserId = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return account;
    }

    public async Task RecordLoginAsync(Guid memberId, DateTime loginAt, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is not null)
        {
            account.LastLoginAt = loginAt;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RecordPasswordFailureAsync(
        Guid memberId,
        int failureCount,
        DateTime windowStartedAt,
        CancellationToken cancellationToken = default)
    {
        await dbContext.MemberAccounts
            .Where(account => account.Id == memberId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(account => account.PasswordFailureCount, failureCount)
                    .SetProperty(account => account.PasswordFailureWindowStartedAt, windowStartedAt),
                cancellationToken);
    }

    public async Task RecordPasswordSignInAsync(
        Guid memberId,
        DateTime loginAt,
        string? rehashedPassword,
        CancellationToken cancellationToken = default)
    {
        var accounts = dbContext.MemberAccounts.Where(account => account.Id == memberId);
        if (rehashedPassword is null)
        {
            await accounts.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(account => account.LastLoginAt, loginAt)
                    .SetProperty(account => account.PasswordFailureCount, 0)
                    .SetProperty(account => account.PasswordFailureWindowStartedAt, (DateTime?)null),
                cancellationToken);
            return;
        }

        await accounts.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(account => account.LastLoginAt, loginAt)
                .SetProperty(account => account.PasswordFailureCount, 0)
                .SetProperty(account => account.PasswordFailureWindowStartedAt, (DateTime?)null)
                .SetProperty(account => account.PasswordHash, rehashedPassword),
            cancellationToken);
    }

    public async Task<MemberStats> GetStatsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var today = utcNow.Date;
        var sevenDaysAgo = today.AddDays(-7);
        var thirtyDaysAgo = today.AddDays(-30);

        var total = await dbContext.MemberAccounts.CountAsync(cancellationToken);
        var newToday = await dbContext.MemberAccounts.CountAsync(a => a.CreatedAt >= today, cancellationToken);
        var newLast7 = await dbContext.MemberAccounts.CountAsync(a => a.CreatedAt >= sevenDaysAgo, cancellationToken);
        var newLast30 = await dbContext.MemberAccounts.CountAsync(a => a.CreatedAt >= thirtyDaysAgo, cancellationToken);

        return new MemberStats(total, newToday, newLast7, newLast30);
    }

    public async Task<IReadOnlyList<RecentLogin>> GetRecentLoginsAsync(int count, CancellationToken cancellationToken = default) =>
        await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(a => a.LastLoginAt != null)
            .OrderByDescending(a => a.LastLoginAt)
            .Take(count)
            .Select(a => new RecentLogin(a.Id, a.DisplayName, a.AvatarUrl != null, a.LastLoginAt!.Value))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DailyRegistration>> GetDailyRegistrationsAsync(DateOnly fromDate, CancellationToken cancellationToken = default)
    {
        var from = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rawDates = await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(a => a.CreatedAt >= from)
            .Select(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return rawDates
            .GroupBy(d => DateOnly.FromDateTime(d))
            .Select(g => new DailyRegistration(g.Key, g.Count()))
            .OrderBy(r => r.Date)
            .ToList();
    }

    public async Task<IReadOnlyList<MemberRecipientMatch>> SearchByDisplayNameAsync(
        string query,
        Guid? excludeMemberId = null,
        int maxResults = PrivateMessageLimits.MaxRecipientSearchResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        maxResults = Math.Clamp(maxResults, 1, PrivateMessageLimits.MaxRecipientSearchResults);
        var term = query.Trim();

        var rows = await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(account =>
                account.DisplayName.Contains(term)
                && account.DeletionRequestedAt == null
                && (excludeMemberId == null || account.Id != excludeMemberId))
            .OrderBy(account => account.DisplayName)
            .Take(maxResults)
            .Select(account => new MemberRecipientMatch(account.Id, account.DisplayName))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<MemberSearchResult> SearchMembersAsync(
        string? query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var term = query?.Trim();

        var matches = dbContext.MemberAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(term))
        {
            matches = matches.Where(account =>
                account.DisplayName.Contains(term) || account.Email.Contains(term));
        }

        var totalCount = await matches.CountAsync(cancellationToken);
        var members = await matches
            .OrderByDescending(account => account.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new MemberSearchResult(members, totalCount);
    }

    public async Task<IReadOnlyList<LocalPasswordAccountSummary>> ListLocalPasswordAccountsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.MemberAccounts
            .AsNoTracking()
            .Where(account => account.PasswordHash != null)
            .OrderBy(account => account.Email)
            .Select(account => new LocalPasswordAccountSummary(
                account.Id,
                account.Email,
                account.DisplayName,
                account.CreatedAt,
                account.LastLoginAt,
                account.IsSuspended))
            .ToListAsync(cancellationToken);

    public async Task<MemberAccount?> UpdateLocalPasswordAccountAsync(
        Guid memberId,
        string email,
        string displayName,
        string? passwordHash,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(candidate => candidate.Id == memberId, cancellationToken);
        if (account?.PasswordHash is null)
        {
            return null;
        }

        account.Email = email;
        account.NormalizedEmail = Normalize(email);
        account.DisplayName = displayName;
        if (passwordHash is not null)
        {
            account.PasswordHash = passwordHash;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<bool> RemoveLocalPasswordAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(candidate => candidate.Id == memberId, cancellationToken);
        if (account?.PasswordHash is null)
        {
            return false;
        }

        account.PasswordHash = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MemberAccount?> SuspendAsync(
        Guid memberId,
        string reason,
        string suspendedByAdminEmail,
        DateTime suspendedAt,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.IsSuspended = true;
        account.SuspendedAt = suspendedAt;
        account.SuspendedReason = reason;
        account.SuspendedByAdminEmail = suspendedByAdminEmail;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MemberAccount?> ReinstateAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.IsSuspended = false;
        account.SuspendedAt = null;
        account.SuspendedReason = null;
        account.SuspendedByAdminEmail = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public Task<MemberAccountDeletionRequestResult?> RequestDeletionAsync(
        Guid memberId,
        DateTime requestedAt,
        CancellationToken cancellationToken = default,
        bool immediate = false)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync<MemberAccountDeletionRequestResult?>(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var account = await dbContext.MemberAccounts
                .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
            if (account is null)
            {
                return null;
            }

            if (account.DeletionRequestedAt is not null && !immediate)
            {
                return new MemberAccountDeletionRequestResult(account, AlreadyRequested: true);
            }

            account.DeletionRecoveryDisplayName ??= account.DisplayName;
            account.DeletionRecoveryAvatarUrl ??= account.AvatarUrl;
            account.DisplayName = MemberAccountDeletionPolicy.DeletedDisplayName;
            account.AvatarUrl = null;
            account.DeletionRequestedAt = immediate
                ? requestedAt.AddDays(-MemberAccountDeletionPolicy.RetentionDays)
                : requestedAt;
            account.IsSuspended = immediate;

            await DeleteSocialLinksAsync(memberId, cancellationToken);
            await AnonymiseRetainedAttributionAsync(memberId, requestedAt, clearMemberLink: false, cancellationToken);

            dbContext.MemberAccountDeletionAuditLogs.Add(new MemberAccountDeletionAuditLogEntity
            {
                MemberAccountId = memberId,
                Action = immediate
                    ? MemberAccountDeletionPolicy.ImmediateRequestedAuditAction
                    : MemberAccountDeletionPolicy.RequestedAuditAction,
                OccurredAt = requestedAt,
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MemberAccountDeletionRequestResult(account, AlreadyRequested: false);
        });
    }

    public Task<MemberAccount?> CancelDeletionAsync(
        Guid memberId,
        DateTime cancelledAt,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var account = await dbContext.MemberAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == memberId, cancellationToken);
            if (account is null || account.DeletionRequestedAt is null || account.PersonalDataPurgedAt is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return account;
            }

            if (cancelledAt >= account.DeletionRequestedAt.Value.AddDays(MemberAccountDeletionPolicy.RetentionDays))
            {
                await transaction.CommitAsync(cancellationToken);
                return account;
            }

            var recoveryDisplayName = account.DeletionRecoveryDisplayName;
            var updated = await dbContext.MemberAccounts
                .Where(candidate =>
                    candidate.Id == memberId
                    && candidate.DeletionRequestedAt == account.DeletionRequestedAt
                    && candidate.PersonalDataPurgedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.DisplayName, recoveryDisplayName ?? MemberAccountDeletionPolicy.DeletedDisplayName)
                        .SetProperty(candidate => candidate.AvatarUrl, account.DeletionRecoveryAvatarUrl)
                        .SetProperty(candidate => candidate.DeletionRequestedAt, (DateTime?)null)
                        .SetProperty(candidate => candidate.DeletionRecoveryDisplayName, (string?)null)
                        .SetProperty(candidate => candidate.DeletionRecoveryAvatarUrl, (string?)null),
                    cancellationToken);
            if (updated == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return await dbContext.MemberAccounts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == memberId, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(recoveryDisplayName))
            {
                await RestoreRetainedAttributionAsync(memberId, recoveryDisplayName, cancelledAt, cancellationToken);
            }

            var trackedAccount = dbContext.ChangeTracker.Entries<MemberAccount>()
                .FirstOrDefault(entry => entry.Entity.Id == memberId);
            if (trackedAccount is not null)
            {
                trackedAccount.State = EntityState.Detached;
            }

            dbContext.MemberAccountDeletionAuditLogs.Add(new MemberAccountDeletionAuditLogEntity
            {
                MemberAccountId = memberId,
                Action = MemberAccountDeletionPolicy.CancelledAuditAction,
                OccurredAt = cancelledAt,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await dbContext.MemberAccounts
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == memberId, cancellationToken);
        });
    }

    public Task<MemberAccountDeletionPurgeResult> PurgeDeletedAccountsAsync(
        DateTime purgeBefore,
        DateTime purgedAt,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var accounts = await dbContext.MemberAccounts
                .Where(account =>
                    account.DeletionRequestedAt != null
                    && account.DeletionRequestedAt <= purgeBefore
                    && account.PersonalDataPurgedAt == null)
                .ToListAsync(cancellationToken);
            if (accounts.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return new MemberAccountDeletionPurgeResult(0, []);
            }

            var memberIds = accounts.Select(account => account.Id).ToList();
            await dbContext.MemberSocialLinks
                .Where(row => memberIds.Contains(row.MemberId))
                .ExecuteDeleteAsync(cancellationToken);
            var avatarBlobPaths = accounts
                .Select(account => account.DeletionRecoveryAvatarUrl)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToList();
            var contentBlobs = new List<MemberDeletionBlob>();

            foreach (var account in accounts)
            {
                await RemoveMemberContributionsAsync(account.Id, contentBlobs, purgedAt, cancellationToken);
                await AnonymiseRetainedAttributionAsync(account.Id, purgedAt, clearMemberLink: true, cancellationToken);
            }
            var allBlobs = contentBlobs
                .Concat(accounts.Where(account => !string.IsNullOrWhiteSpace(account.DeletionRecoveryAvatarUrl)).SelectMany(account => new[]
                {
                    new MemberDeletionBlob(account.Id, "ugc-avatars", account.DeletionRecoveryAvatarUrl!),
                    new MemberDeletionBlob(account.Id, "ugc-avatars", MemberAccountDeletionPolicy.ToAvatarThumbnailPath(account.DeletionRecoveryAvatarUrl!)),
                }))
                .Where(blob => !string.IsNullOrWhiteSpace(blob.Path))
                .Distinct()
                .ToList();
            dbContext.MemberDeletionBlobs.AddRange(allBlobs.Select(blob => new MemberDeletionBlobEntity
            {
                Id = Guid.NewGuid(),
                MemberAccountId = blob.MemberAccountId,
                Container = blob.Container,
                BlobPath = blob.Path,
                CreatedAt = purgedAt,
            }));

            await dbContext.MemberExternalLogins
                .Where(login => memberIds.Contains(login.MemberAccountId)
                    && (login.Provider != "Apple" || login.AppleRefreshTokenProtected == null))
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.MemberExternalLogins
                .Where(login => memberIds.Contains(login.MemberAccountId)
                    && login.Provider == "Apple"
                    && login.AppleRefreshTokenProtected != null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(login => login.Email, "deleted@deleted.invalid"), cancellationToken);

            foreach (var account in accounts)
            {
                var deletedEmail = MemberAccountDeletionPolicy.CreateDeletedEmail(account.Id);
                account.Email = deletedEmail;
                account.NormalizedEmail = Normalize(deletedEmail);
                account.DisplayName = MemberAccountDeletionPolicy.DeletedDisplayName;
                account.AvatarUrl = null;
                account.DeletionRecoveryDisplayName = null;
                account.DeletionRecoveryAvatarUrl = null;
                account.PasswordHash = null;
                account.PasswordFailureCount = 0;
                account.PasswordFailureWindowStartedAt = null;
                account.LastLoginAt = null;
                account.LinkedLegacyUserId = null;
                account.IsSuspended = true;
                account.SuspendedAt = purgedAt;
                account.SuspendedReason = null;
                account.SuspendedByAdminEmail = null;
                account.PersonalDataPurgedAt = purgedAt;

                dbContext.MemberAccountDeletionAuditLogs.Add(new MemberAccountDeletionAuditLogEntity
                {
                    MemberAccountId = account.Id,
                    Action = MemberAccountDeletionPolicy.PurgedAuditAction,
                    OccurredAt = purgedAt,
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MemberAccountDeletionPurgeResult(accounts.Count, avatarBlobPaths, contentBlobs);
        });
    }

    public async Task<IReadOnlyList<PendingMemberDeletionBlob>> ListPendingDeletionBlobsAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.MemberDeletionBlobs
            .AsNoTracking()
            .OrderBy(blob => blob.CreatedAt)
            .Take(limit)
            .Select(blob => new PendingMemberDeletionBlob(blob.Id, blob.MemberAccountId, blob.Container, blob.BlobPath))
            .ToListAsync(cancellationToken);

    public Task CompleteDeletionBlobAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemberDeletionBlobs
            .Where(blob => blob.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<MemberDeletionProgress?> GetDeletionProgressAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MemberAccounts.AsNoTracking()
            .Where(candidate => candidate.Id == memberId && candidate.DeletionRequestedAt != null)
            .Select(candidate => new { candidate.PersonalDataPurgedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var pendingBlobs = await dbContext.MemberDeletionBlobs
            .AnyAsync(blob => blob.MemberAccountId == memberId, cancellationToken);
        var pendingApple = await dbContext.MemberExternalLogins
            .AnyAsync(login => login.MemberAccountId == memberId
                && login.Provider == "Apple"
                && login.AppleRefreshTokenProtected != null, cancellationToken);
        return new MemberDeletionProgress(account.PersonalDataPurgedAt is not null
            && !pendingBlobs && !pendingApple);
    }

    private async Task RemoveMemberContributionsAsync(
        Guid memberId,
        List<MemberDeletionBlob> blobs,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        await dbContext.DeviceTokens.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationPreferences.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MobileAuthAuthorizationCodes.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MobileAuthRefreshTokens.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.IdempotencyReceipts.Where(row => row.MemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MemberTopicWatches.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MemberFollows.Where(row => row.FollowerMemberId == memberId || row.FollowedMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MemberMessageBlocks.Where(row => row.BlockerMemberId == memberId || row.BlockedMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.HomePollVotes.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ForumPollVotes.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.QuizAttempts.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.QuizSprintRuns.Where(row => row.MemberAccountId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.HelpRequests.Where(row => row.MemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);

        var homePollIds = await dbContext.HomePolls
            .Where(poll => poll.CreatedByMemberId == memberId)
            .Select(poll => poll.Id)
            .ToListAsync(cancellationToken);
        await dbContext.HomePollOptions.Where(option => homePollIds.Contains(option.PollId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(option => option.OptionText, "Deleted option"), cancellationToken);
        await dbContext.HomePolls.Where(poll => homePollIds.Contains(poll.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(poll => poll.Question, "Deleted poll")
                .SetProperty(poll => poll.IsCurrent, false), cancellationToken);

        var authoredQuizIds = await dbContext.Quizzes
            .Where(quiz => quiz.CreatedByMemberId == memberId)
            .Select(quiz => quiz.Id)
            .ToListAsync(cancellationToken);
        var authoredQuestionIds = await dbContext.QuizQuestions
            .Where(question => authoredQuizIds.Contains(question.QuizId))
            .Select(question => question.Id)
            .ToListAsync(cancellationToken);
        await dbContext.QuizOptions.Where(option => authoredQuestionIds.Contains(option.QuestionId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(option => option.OptionText, "Deleted option"), cancellationToken);
        await dbContext.QuizQuestions.Where(question => authoredQuestionIds.Contains(question.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(question => question.QuestionText, "Deleted question"), cancellationToken);
        await dbContext.Quizzes.Where(quiz => authoredQuizIds.Contains(quiz.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(quiz => quiz.Title, "Deleted quiz")
                .SetProperty(quiz => quiz.Description, (string?)null)
                .SetProperty(quiz => quiz.IsPublished, false), cancellationToken);

        var attachments = await dbContext.ForumPostAttachments
            .Where(attachment => attachment.Post!.AuthorMemberId == memberId)
            .Select(attachment => new { attachment.ContainerName, attachment.BlobPath })
            .ToListAsync(cancellationToken);
        blobs.AddRange(attachments.Select(attachment =>
            new MemberDeletionBlob(memberId, attachment.ContainerName, attachment.BlobPath)));
        await dbContext.ForumPostAttachments
            .Where(attachment => attachment.Post!.AuthorMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ModernForumPosts
            .Where(post => post.AuthorMemberId == memberId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(post => post.BodyHtml, "<p>Post deleted by member.</p>")
                .SetProperty(post => post.SignatureHtml, (string?)null)
                .SetProperty(post => post.Attachment, (string?)null)
                .SetProperty(post => post.FileSize, (string?)null)
                .SetProperty(post => post.AttachCount, 0)
                .SetProperty(post => post.UpdatedAt, occurredAt), cancellationToken);

        await dbContext.PrivateMessages
            .Where(message => message.SenderMemberId == memberId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Body, "Message deleted by member."), cancellationToken);
        await dbContext.PrivateConversations
            .Where(conversation => conversation.LastMessageSenderId == memberId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.LastMessagePreview, "Message deleted by member."),
                cancellationToken);

        var articles = await dbContext.ArticleSubmissions
            .Where(article => article.AuthorMemberId == memberId)
            .ToListAsync(cancellationToken);
        var articleKeys = articles.Select(article => "article:" + article.Slug).ToList();
        await dbContext.SearchDocuments
            .Where(document => articleKeys.Contains(document.SourceKey))
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var article in articles)
        {
            if (!string.IsNullOrWhiteSpace(article.CoverImageBlobPath))
            {
                blobs.Add(new MemberDeletionBlob(memberId, "ugc-articles", article.CoverImageBlobPath));
            }
            article.Title = "Deleted article";
            article.Slug = $"deleted-{article.Id:N}";
            article.Excerpt = null;
            article.Body = string.Empty;
            article.WordCount = 0;
            article.CoverImageBlobPath = null;
            article.Tags = null;
            article.Status = "Deleted";
        }

        var photos = await dbContext.PhotoSubmissions
            .Where(photo => photo.SubmitterMemberId == memberId)
            .ToListAsync(cancellationToken);
        await OutboxAndRemovePromotedGalleryAsync(memberId, photos, blobs, cancellationToken);
        foreach (var photo in photos)
        {
            blobs.Add(new MemberDeletionBlob(memberId, "ugc-photos", photo.BlobPath));
            blobs.Add(new MemberDeletionBlob(memberId, "ugc-photos", photo.WebOptimizedBlobPath));
            blobs.Add(new MemberDeletionBlob(memberId, "ugc-photos", photo.ThumbnailBlobPath));
            photo.Title = "Deleted photo";
            photo.Description = null;
            photo.BlobPath = string.Empty;
            photo.WebOptimizedBlobPath = string.Empty;
            photo.ThumbnailBlobPath = string.Empty;
            photo.OriginalFileName = string.Empty;
            photo.Status = "Deleted";
            photo.PromotedPicId = null;
        }

        var performances = await dbContext.FanPerformanceSubmissions
            .Where(performance => performance.SubmitterMemberId == memberId)
            .ToListAsync(cancellationToken);
        await OutboxAndRemovePromotedStagesAsync(memberId, performances, blobs, cancellationToken);
        foreach (var performance in performances)
        {
            if (!string.IsNullOrWhiteSpace(performance.BlobPath))
            {
                blobs.Add(new MemberDeletionBlob(memberId, "ugc-fan-performances", performance.BlobPath));
            }
            performance.Title = "Deleted performance";
            performance.Description = null;
            performance.PerformedBy = "Deleted member";
            performance.BlobPath = string.Empty;
            performance.OriginalFileName = string.Empty;
            performance.Status = "Deleted";
            performance.PromotedStageId = null;
        }

        await dbContext.NewsSuggestions
            .Where(suggestion => suggestion.SubmitterMemberId == memberId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(suggestion => suggestion.Url, string.Empty)
                .SetProperty(suggestion => suggestion.UrlHash, string.Empty)
                .SetProperty(suggestion => suggestion.Title, (string?)null)
                .SetProperty(suggestion => suggestion.Notes, (string?)null)
                .SetProperty(suggestion => suggestion.Status, "Deleted"), cancellationToken);

        var trivia = await dbContext.TriviaFactSubmissions
            .Where(submission => submission.SubmitterMemberId == memberId)
            .ToListAsync(cancellationToken);
        var promotedTriviaIds = trivia
            .Where(submission => submission.PromotedTriviaId is not null)
            .Select(submission => submission.PromotedTriviaId!.Value)
            .ToList();
        await dbContext.TriviaFacts
            .Where(fact => promotedTriviaIds.Contains(fact.Id))
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var submission in trivia)
        {
            submission.Text = "Deleted suggestion";
            submission.SourceNote = null;
            submission.Status = "Deleted";
            submission.PromotedTriviaId = null;
        }

        var quizSubmissions = await dbContext.QuizQuestionSubmissions
            .Where(submission => submission.SubmitterMemberId == memberId)
            .ToListAsync(cancellationToken);
        foreach (var submission in quizSubmissions)
        {
            if (submission.AddedToQuizId is Guid quizId)
            {
                var questionIds = await dbContext.QuizQuestions
                    .Where(question => question.QuizId == quizId
                        && question.QuestionText == submission.QuestionText)
                    .Select(question => question.Id)
                    .ToListAsync(cancellationToken);
                await dbContext.QuizOptions
                    .Where(option => questionIds.Contains(option.QuestionId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(option => option.OptionText, "Deleted option"), cancellationToken);
                await dbContext.QuizQuestions
                    .Where(question => questionIds.Contains(question.Id))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(question => question.QuestionText, "Deleted question"), cancellationToken);
            }

            submission.QuestionText = "Deleted question";
            submission.SourceNote = null;
            submission.Status = "Deleted";
        }
        var submissionIds = quizSubmissions.Select(submission => submission.Id).ToList();
        await dbContext.QuizQuestionSubmissionOptions
            .Where(option => submissionIds.Contains(option.QuizQuestionSubmissionId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(option => option.OptionText, "Deleted option"), cancellationToken);

        await dbContext.ForumPolls
            .Where(poll => poll.CreatedByMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);

        var conversationIds = await dbContext.PrivateConversations
            .Where(conversation => conversation.MemberLowId == memberId || conversation.MemberHighId == memberId)
            .Select(conversation => conversation.Id)
            .ToListAsync(cancellationToken);
        await dbContext.PrivateMessageReports
            .Where(report => report.ReporterMemberId == memberId || report.ReportedMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PrivateMessageReports
            .Where(report => conversationIds.Contains(report.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(report => report.PrecedingContextJson, (string?)null), cancellationToken);
        await dbContext.ForumPostReports
            .Where(report => report.ReporterMemberId == memberId || report.ReportedMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.FanPerformanceReports
            .Where(report => report.ReporterMemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);

        var photoIds = photos.Select(photo => photo.Id).ToList();
        var performanceIds = performances.Select(performance => performance.Id).ToList();
        var triviaIds = trivia.Select(submission => submission.Id).ToList();
        await dbContext.PhotoSubmissionAuditLogs
            .Where(log => photoIds.Contains(log.PhotoSubmissionId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.Details, (string?)null), cancellationToken);
        await dbContext.FanPerformanceSubmissionAuditLogs
            .Where(log => performanceIds.Contains(log.FanPerformanceSubmissionId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.Details, (string?)null), cancellationToken);
        await dbContext.TriviaFactSubmissionAuditLogs
            .Where(log => triviaIds.Contains(log.TriviaFactSubmissionId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.Details, (string?)null), cancellationToken);
        await dbContext.QuizQuestionSubmissionAuditLogs
            .Where(log => submissionIds.Contains(log.QuizQuestionSubmissionId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.Details, (string?)null), cancellationToken);
    }

    private async Task AnonymiseRetainedAttributionAsync(
        Guid memberId,
        DateTime occurredAt,
        bool clearMemberLink,
        CancellationToken cancellationToken)
    {
        // Forum search documents are title-only. Keep the thread discoverable while
        // clearing any older index payload that might still contain authored post text.
        var threadKeys = dbContext.ModernForumPosts
            .Where(post => post.AuthorMemberId == memberId)
            .Select(post => "forum-thread:" + post.LegacyThreadTopicId);
        await dbContext.SearchDocuments
            .Where(document => threadKeys.Contains(document.SourceKey))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(document => document.Body, document => document.Title)
                    .SetProperty(document => document.Summary, document => document.Title)
                    .SetProperty(document => document.AuthorDisplayName, (string?)null)
                    .SetProperty(document => document.ImageUrl, (string?)null),
                cancellationToken);

        var starterThreadIds = dbContext.ModernForumPosts
            .Where(post =>
                post.AuthorMemberId == memberId
                && !dbContext.ModernForumPosts.Any(other =>
                    other.ThreadId == post.ThreadId
                    && other.LegacyPostId < post.LegacyPostId))
            .Select(post => post.ThreadId);

        await dbContext.ModernForumThreads
            .Where(thread => starterThreadIds.Contains(thread.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(thread => thread.StartedByDisplayName, MemberAccountDeletionPolicy.DeletedDisplayName)
                    .SetProperty(thread => thread.UpdatedAt, occurredAt),
                cancellationToken);

        var posts = dbContext.ModernForumPosts.Where(post => post.AuthorMemberId == memberId);
        if (clearMemberLink)
        {
            await posts.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(post => post.AuthorMemberId, (Guid?)null)
                    .SetProperty(post => post.AuthorDisplayName, MemberAccountDeletionPolicy.DeletedDisplayName)
                    .SetProperty(post => post.UpdatedAt, occurredAt),
                cancellationToken);
        }
        else
        {
            await posts.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(post => post.AuthorDisplayName, MemberAccountDeletionPolicy.DeletedDisplayName)
                    .SetProperty(post => post.UpdatedAt, occurredAt),
                cancellationToken);
        }

        var articleSourceKeys = dbContext.ArticleSubmissions
            .Where(article => article.AuthorMemberId == memberId)
            .Select(article => "article:" + article.Slug);
        await dbContext.SearchDocuments
            .Where(document => articleSourceKeys.Contains(document.SourceKey))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    document => document.AuthorDisplayName,
                    MemberAccountDeletionPolicy.DeletedDisplayName),
                cancellationToken);
    }

    private async Task RestoreRetainedAttributionAsync(
        Guid memberId,
        string displayName,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        var starterThreadIds = dbContext.ModernForumPosts
            .Where(post =>
                post.AuthorMemberId == memberId
                && !dbContext.ModernForumPosts.Any(other =>
                    other.ThreadId == post.ThreadId
                    && other.LegacyPostId < post.LegacyPostId))
            .Select(post => post.ThreadId);
        await dbContext.ModernForumThreads
            .Where(thread => starterThreadIds.Contains(thread.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(thread => thread.StartedByDisplayName, displayName)
                    .SetProperty(thread => thread.UpdatedAt, occurredAt),
                cancellationToken);
        await dbContext.ModernForumPosts
            .Where(post => post.AuthorMemberId == memberId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(post => post.AuthorDisplayName, displayName)
                    .SetProperty(post => post.UpdatedAt, occurredAt),
                cancellationToken);

        var articleSourceKeys = dbContext.ArticleSubmissions
            .Where(article => article.AuthorMemberId == memberId)
            .Select(article => "article:" + article.Slug);
        await dbContext.SearchDocuments
            .Where(document => articleSourceKeys.Contains(document.SourceKey))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(document => document.AuthorDisplayName, displayName),
                cancellationToken);
    }

    private Task<int> DeleteSocialLinksAsync(Guid memberId, CancellationToken cancellationToken) =>
        dbContext.MemberSocialLinks
            .Where(row => row.MemberId == memberId)
            .ExecuteDeleteAsync(cancellationToken);

    private async Task OutboxAndRemovePromotedGalleryAsync(
        Guid memberId,
        IReadOnlyList<PhotoSubmissionEntity> photos,
        List<MemberDeletionBlob> blobs,
        CancellationToken cancellationToken)
    {
        var picIds = photos
            .Where(photo => photo.PromotedPicId is int)
            .Select(photo => photo.PromotedPicId!.Value)
            .Distinct()
            .ToList();
        if (picIds.Count == 0)
        {
            return;
        }

        var rows = await QueryLegacyRowsAsync<PromotedGalleryBlobRow>(
            $"""
            SELECT PIC_ID AS PicId, Url AS LegacyUrl, Thumb_URL AS LegacyThumbUrl
            FROM {LegacyTable("PIC_FILES_T")}
            WHERE PIC_ID IN ({IdPlaceholders(picIds.Count)})
            """,
            picIds,
            cancellationToken);
        foreach (var row in rows)
        {
            MemberDeletionPromotedMedia.EnqueueGalleryLegacyPaths(
                memberId,
                row.LegacyUrl,
                row.LegacyThumbUrl,
                blobs);
        }

        await ExecuteLegacySqlAsync(
            $"DELETE FROM {LegacyTable("PIC_FILES_T")} WHERE PIC_ID IN ({IdPlaceholders(picIds.Count)})",
            picIds,
            cancellationToken);
    }

    private async Task OutboxAndRemovePromotedStagesAsync(
        Guid memberId,
        IReadOnlyList<FanPerformanceSubmissionEntity> performances,
        List<MemberDeletionBlob> blobs,
        CancellationToken cancellationToken)
    {
        var stageIds = performances
            .Where(performance => performance.PromotedStageId is int)
            .Select(performance => performance.PromotedStageId!.Value)
            .Distinct()
            .ToList();
        if (stageIds.Count == 0)
        {
            return;
        }

        var rows = await QueryLegacyRowsAsync<PromotedStageBlobRow>(
            $"""
            SELECT CAST(Q_STAGE_ID AS int) AS StageId, URL AS AudioFileName
            FROM {LegacyTable("Q_STAGE_T")}
            WHERE Q_STAGE_ID IN ({IdPlaceholders(stageIds.Count)})
            """,
            stageIds,
            cancellationToken);
        foreach (var row in rows)
        {
            MemberDeletionPromotedMedia.EnqueueStageAudio(memberId, row.AudioFileName, blobs);
        }

        await ExecuteLegacySqlAsync(
            $"DELETE FROM {LegacyTable("Q_STAGE_T")} WHERE Q_STAGE_ID IN ({IdPlaceholders(stageIds.Count)})",
            stageIds,
            cancellationToken);
    }

    private async Task<IReadOnlyList<T>> QueryLegacyRowsAsync<T>(
        string sql,
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
        where T : class
    {
        return await dbContext.Database
            .SqlQueryRaw<T>(sql, ids.Cast<object>().ToArray())
            .ToListAsync(cancellationToken);
    }

    private Task<int> ExecuteLegacySqlAsync(
        string sql,
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(sql, ids.Cast<object>(), cancellationToken);

    private string LegacyTable(string tableName) =>
        dbContext.Database.IsSqlServer() ? $"dbo.{tableName}" : tableName;

    private static string IdPlaceholders(int count) =>
        string.Join(", ", Enumerable.Range(0, count).Select(index => $"{{{index}}}"));

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private sealed class PromotedGalleryBlobRow
    {
        public int PicId { get; set; }

        public string? LegacyUrl { get; set; }

        public string? LegacyThumbUrl { get; set; }
    }

    private sealed class PromotedStageBlobRow
    {
        public int StageId { get; set; }

        public string? AudioFileName { get; set; }
    }
}
