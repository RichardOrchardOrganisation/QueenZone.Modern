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
        CancellationToken cancellationToken = default)
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

            if (account.DeletionRequestedAt is not null)
            {
                return new MemberAccountDeletionRequestResult(account, AlreadyRequested: true);
            }

            account.DeletionRecoveryDisplayName = account.DisplayName;
            account.DeletionRecoveryAvatarUrl = account.AvatarUrl;
            account.DisplayName = MemberAccountDeletionPolicy.DeletedDisplayName;
            account.AvatarUrl = null;
            account.DeletionRequestedAt = requestedAt;

            await DeleteSocialLinksAsync(memberId, cancellationToken);
            await AnonymiseRetainedAttributionAsync(memberId, requestedAt, clearMemberLink: false, cancellationToken);

            dbContext.MemberAccountDeletionAuditLogs.Add(new MemberAccountDeletionAuditLogEntity
            {
                MemberAccountId = memberId,
                Action = MemberAccountDeletionPolicy.RequestedAuditAction,
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

            foreach (var account in accounts)
            {
                await AnonymiseRetainedAttributionAsync(account.Id, purgedAt, clearMemberLink: true, cancellationToken);
            }

            await dbContext.MemberExternalLogins
                .Where(login => memberIds.Contains(login.MemberAccountId))
                .ExecuteDeleteAsync(cancellationToken);

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
                account.LastLoginAt = null;
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
            return new MemberAccountDeletionPurgeResult(accounts.Count, avatarBlobPaths);
        });
    }

    private async Task AnonymiseRetainedAttributionAsync(
        Guid memberId,
        DateTime occurredAt,
        bool clearMemberLink,
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

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
