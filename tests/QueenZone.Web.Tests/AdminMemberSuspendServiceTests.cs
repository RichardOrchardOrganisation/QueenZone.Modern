using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class AdminMemberSuspendServiceTests
{
    [Fact]
    public async Task SuspendAsync_ReturnsNotFound_WhenMemberMissing()
    {
        var members = new InMemoryMemberAccountRepository();
        var service = CreateService(members, new RecordingForumWriteRepository());

        var result = await service.SuspendAsync(
            Guid.NewGuid(), "Spam", "admin@example.com", DateTime.UtcNow);

        Assert.Equal(AdminMemberSuspendStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SuspendAsync_HidesThenSuspendsThenRevokes()
    {
        var members = new InMemoryMemberAccountRepository();
        var account = await SeedMemberAsync(members);
        var forum = new RecordingForumWriteRepository();
        var grants = new RecordingMobileAuthGrantRepository();
        var service = CreateService(members, forum, grants);

        var result = await service.SuspendAsync(
            account.Id, "Posting spam", "admin@example.com", DateTime.UtcNow);

        Assert.Equal(AdminMemberSuspendStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "hide" }, forum.Calls);
        Assert.True((await members.FindByIdAsync(account.Id))!.IsSuspended);
        Assert.Equal(1, grants.RevokeCalls);
    }

    [Fact]
    public async Task SuspendAsync_SecondCallIsIdempotent()
    {
        var members = new InMemoryMemberAccountRepository();
        var account = await SeedMemberAsync(members);
        var forum = new RecordingForumWriteRepository();
        var grants = new RecordingMobileAuthGrantRepository();
        var service = CreateService(members, forum, grants);
        var now = DateTime.UtcNow;

        var first = await service.SuspendAsync(account.Id, "Spam", "admin@example.com", now);
        var second = await service.SuspendAsync(account.Id, "Spam", "admin@example.com", now);

        Assert.Equal(AdminMemberSuspendStatus.Succeeded, first.Status);
        Assert.Equal(AdminMemberSuspendStatus.Succeeded, second.Status);
        Assert.Equal(new[] { "hide", "hide" }, forum.Calls);
        Assert.True((await members.FindByIdAsync(account.Id))!.IsSuspended);
        Assert.Equal(2, grants.RevokeCalls);
    }

    [Fact]
    public async Task SuspendAsync_HideTimeout_LeavesAccountActive_AndDoesNotRevoke()
    {
        var members = new InMemoryMemberAccountRepository();
        var account = await SeedMemberAsync(members);
        var timeout = SiteSearchSqlTimeoutTests.CreateSqlException(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");
        var forum = new RecordingForumWriteRepository { HideException = timeout };
        var grants = new RecordingMobileAuthGrantRepository();
        var logger = new CollectingLogger<AdminMemberSuspendService>();
        var service = CreateService(members, forum, grants, logger);

        var result = await service.SuspendAsync(
            account.Id, "Spam", "admin@example.com", DateTime.UtcNow);

        Assert.Equal(AdminMemberSuspendStatus.HideTimedOut, result.Status);
        Assert.False((await members.FindByIdAsync(account.Id))!.IsSuspended);
        Assert.Equal(0, grants.RevokeCalls);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("timed out", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(timeout, warning.Exception);
    }

    [Fact]
    public async Task SuspendAsync_RevokeFailure_AfterHideAndSuspend_IsDiagnosable()
    {
        var members = new InMemoryMemberAccountRepository();
        var account = await SeedMemberAsync(members);
        var forum = new RecordingForumWriteRepository();
        var grants = new RecordingMobileAuthGrantRepository
        {
            RevokeException = new InvalidOperationException("token store unavailable"),
        };
        var logger = new CollectingLogger<AdminMemberSuspendService>();
        var service = CreateService(members, forum, grants, logger);

        var result = await service.SuspendAsync(
            account.Id, "Spam", "admin@example.com", DateTime.UtcNow);

        Assert.Equal(AdminMemberSuspendStatus.RevokeFailed, result.Status);
        Assert.Equal(new[] { "hide" }, forum.Calls);
        Assert.True((await members.FindByIdAsync(account.Id))!.IsSuspended);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("revoke failed", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AdminMemberSuspendService CreateService(
        IMemberAccountRepository members,
        IForumWriteRepository forum,
        IMobileAuthGrantRepository? grants = null,
        ILogger<AdminMemberSuspendService>? logger = null) =>
        new(
            members,
            forum,
            grants ?? new RecordingMobileAuthGrantRepository(),
            logger ?? new CollectingLogger<AdminMemberSuspendService>());

    private static async Task<MemberAccount> SeedMemberAsync(InMemoryMemberAccountRepository members)
    {
        return await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "spammer@example.com",
            DisplayName = "Board Spammer",
            CreatedAt = DateTime.UtcNow,
        });
    }

    private sealed class RecordingForumWriteRepository : IForumWriteRepository
    {
        public List<string> Calls { get; } = [];

        public Exception? HideException { get; set; }

        public Task<ForumThreadCreateResult> CreateThreadAsync(
            NewForumThread thread, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumThreadCreateResult(1, 1));

        public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumEditablePost?>(null);

        public Task<ForumPostUpdateResult> UpdatePostAsync(
            int postId,
            Guid editorMemberId,
            string sanitisedBody,
            bool isAdmin,
            int editWindowMinutes,
            DateTimeOffset? expectedUpdatedAt = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.NotFound));

        public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumWriteThread?>(null);

        public Task<int> CountPostsByMemberSinceAsync(
            Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CountApprovedPostsByMemberAsync(
            Guid memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task HideAuthorForumContentAsync(
            Guid? memberId, string displayName, CancellationToken cancellationToken = default)
        {
            Calls.Add("hide");
            if (HideException is not null)
            {
                throw HideException;
            }

            return Task.CompletedTask;
        }

        public Task UnhideAuthorForumContentAsync(
            Guid? memberId, string displayName, CancellationToken cancellationToken = default)
        {
            Calls.Add("unhide");
            return Task.CompletedTask;
        }

        public Task<int> EnsureCategoryAsync(
            string slug, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private sealed class RecordingMobileAuthGrantRepository : IMobileAuthGrantRepository
    {
        public int RevokeCalls { get; private set; }

        public Exception? RevokeException { get; set; }

        public Task StoreAuthorizationCodeAsync(
            QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity code,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity?> RedeemAuthorizationCodeAsync(
            string codeHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity?>(null);

        public Task StoreRefreshTokenAsync(
            QueenZone.Data.Entities.MobileAuthRefreshTokenEntity token,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<QueenZone.Data.Entities.MobileAuthRefreshTokenEntity?> FindRefreshTokenByHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<QueenZone.Data.Entities.MobileAuthRefreshTokenEntity?>(null);

        public Task<bool> TryRevokeRefreshTokenAsync(
            string tokenHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> RevokeAllRefreshTokensForMemberAsync(
            Guid memberAccountId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            RevokeCalls++;
            if (RevokeException is not null)
            {
                throw RevokeException;
            }

            return Task.FromResult(0);
        }

        public Task<bool> LinkRefreshTokenRotationAsync(
            string oldTokenHash,
            string newTokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
