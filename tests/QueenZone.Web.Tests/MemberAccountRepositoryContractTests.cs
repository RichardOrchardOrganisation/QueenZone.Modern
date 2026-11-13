using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class MemberAccountRepositoryContractTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Avatar = "members/fan/avatar.webp";

    protected abstract IMemberAccountRepository Repository { get; }
    protected abstract Task<IReadOnlyList<(string Action, DateTime OccurredAt)>> ListDeletionAuditAsync(Guid memberId);

    [Fact]
    public async Task LegacyLink_PersistsFirstLinkUntilUnlinked_AllowsRelinking()
    {
        var member = await SeedAsync();
        Assert.Equal(101, (await Repository.LinkLegacyUserIdAsync(member.Id, 101))!.LinkedLegacyUserId);
        Assert.Equal(101, (await Repository.FindByIdAsync(member.Id))!.LinkedLegacyUserId);
        Assert.Equal(member.Id, (await Repository.FindByLinkedLegacyUserIdAsync(101))!.Id);
        Assert.Equal(101, (await Repository.LinkLegacyUserIdAsync(member.Id, 202))!.LinkedLegacyUserId);
        Assert.Equal(101, (await Repository.FindByIdAsync(member.Id))!.LinkedLegacyUserId);
        Assert.Null(await Repository.FindByLinkedLegacyUserIdAsync(202));

        Assert.Null((await Repository.UnlinkLegacyUserIdAsync(member.Id))!.LinkedLegacyUserId);
        Assert.Null((await Repository.FindByIdAsync(member.Id))!.LinkedLegacyUserId);
        Assert.Null(await Repository.FindByLinkedLegacyUserIdAsync(101));
        Assert.Equal(202, (await Repository.LinkLegacyUserIdAsync(member.Id, 202))!.LinkedLegacyUserId);
        Assert.Equal(member.Id, (await Repository.FindByLinkedLegacyUserIdAsync(202))!.Id);
        Assert.Equal(202, (await Repository.FindByIdAsync(member.Id))!.LinkedLegacyUserId);
    }

    [Fact]
    public async Task LegacyLink_MissingMemberOrLink_ReturnsNullOrUnlinkedAccount()
    {
        var missing = Guid.NewGuid();
        Assert.Null(await Repository.LinkLegacyUserIdAsync(missing, 101));
        Assert.Null(await Repository.UnlinkLegacyUserIdAsync(missing));
        Assert.Null(await Repository.FindByLinkedLegacyUserIdAsync(101));
        var member = await SeedAsync();
        var unlinked = await Repository.UnlinkLegacyUserIdAsync(member.Id);
        Assert.NotNull(unlinked);
        Assert.Equal(member.Id, unlinked.Id);
        Assert.Null(unlinked.LinkedLegacyUserId);
        Assert.Null((await Repository.FindByIdAsync(member.Id))!.LinkedLegacyUserId);
    }

    [Fact]
    public async Task Suspend_RepeatedSuspensionOverwritesDetails_ReinstateClearsThem()
    {
        var member = await SeedAsync();
        await Repository.SuspendAsync(member.Id, "First reason", "first@example.test", Now);
        var suspended = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(suspended);
        Assert.True(suspended.IsSuspended);
        Assert.Equal("First reason", suspended.SuspendedReason);
        Assert.Equal("first@example.test", suspended.SuspendedByAdminEmail);
        Assert.Equal(Now, suspended.SuspendedAt);

        await Repository.SuspendAsync(member.Id, "Second reason", "second@example.test", Now.AddHours(1));
        suspended = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(suspended);
        Assert.True(suspended.IsSuspended);
        Assert.Equal("Second reason", suspended.SuspendedReason);
        Assert.Equal("second@example.test", suspended.SuspendedByAdminEmail);
        Assert.Equal(Now.AddHours(1), suspended.SuspendedAt);

        var reinstated = await Repository.ReinstateAsync(member.Id);
        Assert.NotNull(reinstated);
        Assert.False(reinstated.IsSuspended);
        Assert.Null(reinstated.SuspendedAt);
        Assert.Null(reinstated.SuspendedReason);
        Assert.Null(reinstated.SuspendedByAdminEmail);
        var reloaded = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsSuspended);
        Assert.Null(reloaded.SuspendedAt);
        Assert.Null(reloaded.SuspendedReason);
        Assert.Null(reloaded.SuspendedByAdminEmail);
    }

    [Fact]
    public async Task SuspendAndReinstate_MissingMember_ReturnNull()
    {
        var missing = Guid.NewGuid();
        Assert.Null(await Repository.SuspendAsync(missing, "Reason", "admin@example.test", Now));
        Assert.Null(await Repository.ReinstateAsync(missing));
    }

    [Fact]
    public async Task RequestDeletion_NormalRequest_HidesProfileAndPreservesOtherMembers()
    {
        var member = await SeedAsync();
        var other = await SeedAsync(displayName: "Other fan");
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        var link = new MemberSocialLink(MemberSocialChannel.Instagram, "https://instagram.com/fan");
        await Repository.ReplaceSocialLinksAsync(member.Id, [link]);
        await Repository.ReplaceSocialLinksAsync(other.Id, [link]);

        var result = await Repository.RequestDeletionAsync(member.Id, Now, immediate: false);
        Assert.NotNull(result);
        Assert.False(result.AlreadyRequested);
        var reloaded = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, reloaded.DisplayName);
        Assert.Null(reloaded.AvatarUrl);
        Assert.Equal(Now, reloaded.DeletionRequestedAt);
        Assert.Equal("Fan", reloaded.DeletionRecoveryDisplayName);
        Assert.Equal(Avatar, reloaded.DeletionRecoveryAvatarUrl);
        Assert.False(reloaded.IsSuspended);
        Assert.Empty(await Repository.ListSocialLinksAsync(member.Id));
        Assert.Equal(link, Assert.Single(await Repository.ListSocialLinksAsync(other.Id)));
        Assert.Equal((MemberAccountDeletionPolicy.RequestedAuditAction, Now), Assert.Single(await ListDeletionAuditAsync(member.Id)));
        Assert.Equal(other.Id, Assert.Single(await Repository.ListActiveMemberIdsAsync([member.Id, other.Id, Guid.NewGuid()])));
        Assert.Equal(new MemberDeletionProgress(false), await Repository.GetDeletionProgressAsync(member.Id));
        Assert.Null(await Repository.GetDeletionProgressAsync(other.Id));
        var unchanged = await Repository.FindByIdAsync(other.Id);
        Assert.NotNull(unchanged);
        Assert.Equal("Other fan", unchanged.DisplayName);
        Assert.Null(unchanged.DeletionRequestedAt);
        Assert.False(unchanged.IsSuspended);
        Assert.Empty(await ListDeletionAuditAsync(other.Id));
    }

    [Fact]
    public async Task RequestDeletion_PendingRequest_DoesNotCreateAnotherRequest()
    {
        var member = await SeedAsync();
        await Repository.RequestDeletionAsync(member.Id, Now, immediate: false);
        var repeated = await Repository.RequestDeletionAsync(member.Id, Now.AddDays(1), immediate: false);
        Assert.NotNull(repeated);
        Assert.True(repeated.AlreadyRequested);
        Assert.Equal(Now, (await Repository.FindByIdAsync(member.Id))!.DeletionRequestedAt);
        Assert.Equal((MemberAccountDeletionPolicy.RequestedAuditAction, Now), Assert.Single(await ListDeletionAuditAsync(member.Id)));
    }

    [Fact]
    public async Task RequestDeletion_MissingMember_ReturnsNull()
    {
        var missing = Guid.NewGuid();
        Assert.Null(await Repository.RequestDeletionAsync(missing, Now));
        Assert.Empty(await ListDeletionAuditAsync(missing));
    }

    [Fact]
    public async Task RequestDeletion_ImmediateRequest_BackdatesAndSuspendsWithoutAdminDetails()
    {
        var member = await SeedAsync();
        var result = await Repository.RequestDeletionAsync(member.Id, Now, immediate: true);
        Assert.NotNull(result);
        Assert.False(result.AlreadyRequested);
        var reloaded = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(Now.AddDays(-MemberAccountDeletionPolicy.RetentionDays), reloaded.DeletionRequestedAt);
        Assert.True(reloaded.IsSuspended);
        Assert.Null(reloaded.SuspendedAt);
        Assert.Null(reloaded.SuspendedReason);
        Assert.Null(reloaded.SuspendedByAdminEmail);
        Assert.Equal((MemberAccountDeletionPolicy.ImmediateRequestedAuditAction, Now), Assert.Single(await ListDeletionAuditAsync(member.Id)));
    }

    [Fact]
    public async Task CancelDeletion_BeforeCutoff_RestoresProfileAndPreventsPurge()
    {
        var member = await SeedAsync();
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        await Repository.RequestDeletionAsync(member.Id, Now);
        var cancelledAt = Now.AddDays(MemberAccountDeletionPolicy.RetentionDays).AddTicks(-1);
        Assert.NotNull(await Repository.CancelDeletionAsync(member.Id, cancelledAt));
        var restored = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(restored);
        Assert.Equal("Fan", restored.DisplayName);
        Assert.Equal(Avatar, restored.AvatarUrl);
        Assert.Null(restored.DeletionRequestedAt);
        Assert.Null(restored.DeletionRecoveryDisplayName);
        Assert.Null(restored.DeletionRecoveryAvatarUrl);
        Assert.Equal([(MemberAccountDeletionPolicy.RequestedAuditAction, Now), (MemberAccountDeletionPolicy.CancelledAuditAction, cancelledAt)],
            await ListDeletionAuditAsync(member.Id));
        Assert.Equal(0, (await Repository.PurgeDeletedAccountsAsync(Now.AddDays(1), Now.AddDays(31))).PurgedCount);
        Assert.Null((await Repository.FindByIdAsync(member.Id))!.PersonalDataPurgedAt);
    }

    [Fact]
    public async Task CancelDeletion_ExactCutoff_LeavesRequestAndHiddenProfile()
    {
        var member = await SeedAsync();
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        await Repository.RequestDeletionAsync(member.Id, Now);
        Assert.NotNull(await Repository.CancelDeletionAsync(member.Id, Now.AddDays(MemberAccountDeletionPolicy.RetentionDays)));
        var pending = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(pending);
        Assert.Equal(Now, pending.DeletionRequestedAt);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, pending.DisplayName);
        Assert.Null(pending.AvatarUrl);
        Assert.Equal("Fan", pending.DeletionRecoveryDisplayName);
        Assert.Equal(Avatar, pending.DeletionRecoveryAvatarUrl);
        Assert.Equal((MemberAccountDeletionPolicy.RequestedAuditAction, Now), Assert.Single(await ListDeletionAuditAsync(member.Id)));
    }

    [Fact]
    public async Task CancelDeletion_NoRequestOrMissingMember_IsNoOp()
    {
        var member = await SeedAsync();
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        var returned = await Repository.CancelDeletionAsync(member.Id, Now);
        Assert.NotNull(returned);
        Assert.Equal(member.Id, returned.Id);
        Assert.Equal("Fan", returned.DisplayName);
        Assert.Equal(Avatar, returned.AvatarUrl);
        var unchanged = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(unchanged);
        Assert.Equal("Fan", unchanged.DisplayName);
        Assert.Equal(Avatar, unchanged.AvatarUrl);
        Assert.Null(unchanged.DeletionRequestedAt);
        Assert.Empty(await ListDeletionAuditAsync(member.Id));
        var missing = Guid.NewGuid();
        Assert.Null(await Repository.CancelDeletionAsync(missing, Now));
        Assert.Empty(await ListDeletionAuditAsync(missing));
    }

    [Fact]
    public async Task PurgeDeletion_ExactRequestBoundary_AnonymisesOnlyRequestedMember()
    {
        var member = await SeedAsync(
            passwordHash: "hash",
            linkedLegacyUserId: 101,
            lastLoginAt: Now.AddDays(-1),
            passwordFailureCount: 3,
            passwordFailureWindowStartedAt: Now.AddHours(-1));
        var other = await SeedAsync(displayName: "Other fan", passwordHash: "other-hash", linkedLegacyUserId: 202, lastLoginAt: Now);
        var otherEmail = other.Email;
        await Repository.UpdateAvatarUrlAsync(other.Id, Avatar);
        var otherLink = new MemberSocialLink(MemberSocialChannel.X, "https://x.com/other");
        await Repository.ReplaceSocialLinksAsync(other.Id, [otherLink]);
        await Repository.AddExternalLoginAsync(other.Id, "Google", "other-key", otherEmail);
        await Repository.SuspendAsync(member.Id, "Old reason", "admin@example.test", Now.AddDays(-1));
        await Repository.AddExternalLoginAsync(member.Id, "Google", "google-key", member.Email);
        await Repository.RequestDeletionAsync(member.Id, Now, immediate: false);
        var purgedAt = Now.AddDays(MemberAccountDeletionPolicy.RetentionDays);
        Assert.Equal(0, (await Repository.PurgeDeletedAccountsAsync(Now.AddTicks(-1), purgedAt)).PurgedCount);
        Assert.Null((await Repository.FindByIdAsync(member.Id))!.PersonalDataPurgedAt);
        var result = await Repository.PurgeDeletedAccountsAsync(Now, purgedAt);
        Assert.Equal(1, result.PurgedCount);
        Assert.Empty(result.AvatarBlobPaths);
        Assert.NotNull(result.ContentBlobs);
        Assert.Empty(result.ContentBlobs);
        var purged = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(purged);
        var deletedEmail = MemberAccountDeletionPolicy.CreateDeletedEmail(member.Id);
        Assert.Equal(deletedEmail, purged.Email);
        Assert.Equal(deletedEmail.ToUpperInvariant(), purged.NormalizedEmail);
        Assert.Equal(member.Id, (await Repository.FindByEmailAsync(deletedEmail))!.Id);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, purged.DisplayName);
        Assert.Null(purged.PasswordHash);
        Assert.Null(purged.LastLoginAt);
        Assert.Null(purged.LinkedLegacyUserId);
        Assert.Equal(0, purged.PasswordFailureCount);
        Assert.Null(purged.PasswordFailureWindowStartedAt);
        Assert.Null(purged.AvatarUrl);
        Assert.Null(purged.DeletionRecoveryDisplayName);
        Assert.Null(purged.DeletionRecoveryAvatarUrl);
        Assert.True(purged.IsSuspended);
        Assert.Equal(purgedAt, purged.SuspendedAt);
        Assert.Null(purged.SuspendedReason);
        Assert.Null(purged.SuspendedByAdminEmail);
        Assert.Equal(purgedAt, purged.PersonalDataPurgedAt);
        Assert.Empty(await Repository.ListExternalProvidersAsync(member.Id));
        Assert.Null(await Repository.FindByLinkedLegacyUserIdAsync(101));
        Assert.Equal([(MemberAccountDeletionPolicy.RequestedAuditAction, Now), (MemberAccountDeletionPolicy.PurgedAuditAction, purgedAt)],
            await ListDeletionAuditAsync(member.Id));
        Assert.Empty(await Repository.ListPendingDeletionBlobsAsync(10));
        Assert.Equal(new MemberDeletionProgress(true), await Repository.GetDeletionProgressAsync(member.Id));

        var unchanged = await Repository.FindByIdAsync(other.Id);
        Assert.NotNull(unchanged);
        Assert.Equal(otherEmail, unchanged.Email);
        Assert.Equal("Other fan", unchanged.DisplayName);
        Assert.Equal("other-hash", unchanged.PasswordHash);
        Assert.Equal(202, unchanged.LinkedLegacyUserId);
        Assert.Equal(Now, unchanged.LastLoginAt);
        Assert.Equal(Avatar, unchanged.AvatarUrl);
        Assert.Null(unchanged.DeletionRequestedAt);
        Assert.Null(unchanged.PersonalDataPurgedAt);
        Assert.False(unchanged.IsSuspended);
        Assert.Equal(otherLink, Assert.Single(await Repository.ListSocialLinksAsync(other.Id)));
        Assert.Equal("Google", Assert.Single(await Repository.ListExternalProvidersAsync(other.Id)));
        Assert.Empty(await ListDeletionAuditAsync(other.Id));
    }

    [Fact]
    public async Task CancelDeletion_AfterPurge_DoesNotRestorePersonalData()
    {
        var member = await SeedAsync();
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        await Repository.RequestDeletionAsync(member.Id, Now);
        await Repository.PurgeDeletedAccountsAsync(Now, Now.AddDays(1));
        Assert.NotNull(await Repository.CancelDeletionAsync(member.Id, Now.AddDays(2)));
        var purged = await Repository.FindByIdAsync(member.Id);
        Assert.NotNull(purged);
        Assert.Equal(MemberAccountDeletionPolicy.CreateDeletedEmail(member.Id), purged.Email);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, purged.DisplayName);
        Assert.Null(purged.AvatarUrl);
        Assert.Null(purged.DeletionRecoveryDisplayName);
        Assert.Null(purged.DeletionRecoveryAvatarUrl);
        Assert.Equal(Now, purged.DeletionRequestedAt);
        Assert.Equal(Now.AddDays(1), purged.PersonalDataPurgedAt);
        Assert.Equal([(MemberAccountDeletionPolicy.RequestedAuditAction, Now), (MemberAccountDeletionPolicy.PurgedAuditAction, Now.AddDays(1))],
            await ListDeletionAuditAsync(member.Id));
    }

    [Fact]
    public async Task PurgeDeletion_ImmediateRequest_WaitsForEveryAvatarBlob()
    {
        var member = await SeedAsync();
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        await Repository.RequestDeletionAsync(member.Id, Now, immediate: true);
        var result = await Repository.PurgeDeletedAccountsAsync(Now.AddDays(-MemberAccountDeletionPolicy.RetentionDays), Now);
        Assert.Equal(1, result.PurgedCount);
        Assert.Equal(Avatar, Assert.Single(result.AvatarBlobPaths));
        var blobs = await Repository.ListPendingDeletionBlobsAsync(10);
        Assert.Equal(2, blobs.Count);
        Assert.Contains(blobs, blob => blob.MemberAccountId == member.Id && blob.Container == "ugc-avatars" && blob.Path == Avatar);
        Assert.Contains(blobs, blob => blob.MemberAccountId == member.Id && blob.Container == "ugc-avatars"
            && blob.Path == MemberAccountDeletionPolicy.ToAvatarThumbnailPath(Avatar));
        Assert.Equal(new MemberDeletionProgress(false), await Repository.GetDeletionProgressAsync(member.Id));
        await Repository.CompleteDeletionBlobAsync(blobs[0].Id);
        Assert.Equal(new MemberDeletionProgress(false), await Repository.GetDeletionProgressAsync(member.Id));
        Assert.Equal(blobs[1], Assert.Single(await Repository.ListPendingDeletionBlobsAsync(10)));
        await Repository.CompleteDeletionBlobAsync(blobs[1].Id);
        Assert.Empty(await Repository.ListPendingDeletionBlobsAsync(10));
        Assert.Equal(new MemberDeletionProgress(true), await Repository.GetDeletionProgressAsync(member.Id));
    }

    [Fact]
    public async Task PurgeDeletion_AppleToken_RetainsRevocationUntilCompletedThenWaitsForBlobs()
    {
        var member = await SeedAsync();
        await Repository.AddExternalLoginAsync(member.Id, "Apple", "apple-key", member.Email);
        await Repository.AddExternalLoginAsync(member.Id, "Google", "google-key", member.Email);
        await Repository.SaveAppleRefreshTokenAsync(member.Id, "apple-key", "protected-token");
        await Repository.UpdateAvatarUrlAsync(member.Id, Avatar);
        await Repository.RequestDeletionAsync(member.Id, Now, immediate: true);
        var cutoff = Now.AddDays(-MemberAccountDeletionPolicy.RetentionDays);
        Assert.Equal(1, (await Repository.PurgeDeletedAccountsAsync(cutoff, Now)).PurgedCount);
        Assert.Equal("Apple", Assert.Single(await Repository.ListExternalProvidersAsync(member.Id)));
        var revocation = Assert.Single(await Repository.ListPendingAppleRevocationsAsync(10));
        Assert.Equal("protected-token", revocation.ProtectedToken);
        Assert.Equal(new MemberDeletionProgress(false), await Repository.GetDeletionProgressAsync(member.Id));
        await Repository.CompleteAppleRevocationAsync(revocation.ExternalLoginId);
        Assert.Empty(await Repository.ListExternalProvidersAsync(member.Id));
        Assert.Empty(await Repository.ListPendingAppleRevocationsAsync(10));
        Assert.Equal(new MemberDeletionProgress(false), await Repository.GetDeletionProgressAsync(member.Id));
        var blobs = await Repository.ListPendingDeletionBlobsAsync(10);
        Assert.Equal(2, blobs.Count);
        foreach (var blob in blobs)
        {
            await Repository.CompleteDeletionBlobAsync(blob.Id);
        }
        Assert.Empty(await Repository.ListPendingDeletionBlobsAsync(10));
        Assert.Equal(new MemberDeletionProgress(true), await Repository.GetDeletionProgressAsync(member.Id));
        Assert.Equal(0, (await Repository.PurgeDeletedAccountsAsync(cutoff, Now.AddHours(1))).PurgedCount);
    }

    private Task<MemberAccount> SeedAsync(
        string displayName = "Fan",
        string? passwordHash = null,
        int? linkedLegacyUserId = null,
        DateTime? lastLoginAt = null,
        int passwordFailureCount = 0,
        DateTime? passwordFailureWindowStartedAt = null) =>
        Repository.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.test",
            DisplayName = displayName,
            CreatedAt = Now.AddDays(-60),
            PasswordHash = passwordHash,
            LinkedLegacyUserId = linkedLegacyUserId,
            LastLoginAt = lastLoginAt,
            PasswordFailureCount = passwordFailureCount,
            PasswordFailureWindowStartedAt = passwordFailureWindowStartedAt,
        });
}

public sealed class InMemoryMemberAccountRepositoryContractTests : MemberAccountRepositoryContractTests
{
    private readonly InMemoryMemberAccountRepository repository = new();

    protected override IMemberAccountRepository Repository => repository;

    protected override Task<IReadOnlyList<(string Action, DateTime OccurredAt)>> ListDeletionAuditAsync(Guid memberId) =>
        Task.FromResult(repository.GetDeletionAuditLogs(memberId));
}

public sealed class EfMemberAccountRepositoryContractTests : MemberAccountRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly IMemberAccountRepository repository;

    public EfMemberAccountRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables(dbContext);
        repository = new EfMemberAccountRepository(dbContext);
    }

    private static void CreateModernForumTables(QueenZoneDbContext dbContext)
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumCategory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyForumId INTEGER NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL,
                LegacyPostCount INTEGER NOT NULL,
                LastActivityAt TEXT NULL,
                IsSynthetic INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE ModernForumThread
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyTopicId INTEGER NOT NULL UNIQUE,
                LegacyForumId INTEGER NOT NULL,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                StartedByLegacyUserId INTEGER NULL,
                StartedByDisplayName TEXT NOT NULL,
                StartedAt TEXT NULL,
                LastActivityAt TEXT NULL,
                ReplyCount INTEGER NOT NULL,
                IsSticky INTEGER NOT NULL,
                IsLegacyTopicStarter INTEGER NOT NULL,
                LegacyDiscography INTEGER NOT NULL,
                StartedByUserValidated INTEGER NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                StarterAttachment TEXT NULL,
                StarterFileSize TEXT NULL,
                StarterAttachCount INTEGER NOT NULL,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES ModernForumCategory (Id)
            );

            CREATE TABLE ModernForumPost
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyPostId INTEGER NOT NULL UNIQUE,
                LegacyThreadTopicId INTEGER NOT NULL,
                ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                AuthorLegacyUserId INTEGER NULL,
                AuthorDisplayName TEXT NOT NULL,
                AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL,
                BodyHtml TEXT NOT NULL,
                SignatureHtml TEXT NULL,
                PostedAt TEXT NULL,
                LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL,
                Attachment TEXT NULL,
                FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL,
                AuthorMemberId TEXT NULL,
                EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread (Id)
            );
            """);
    }

    protected override IMemberAccountRepository Repository => repository;

    protected override async Task<IReadOnlyList<(string Action, DateTime OccurredAt)>> ListDeletionAuditAsync(Guid memberId)
    {
        var logs = await dbContext.MemberAccountDeletionAuditLogs.AsNoTracking()
            .Where(log => log.MemberAccountId == memberId)
            .OrderBy(log => log.OccurredAt).ThenBy(log => log.Id)
            .Select(log => new { log.Action, log.OccurredAt })
            .ToListAsync();
        return logs.Select(log => (log.Action, log.OccurredAt)).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
