using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

[Collection(MaintenanceJobActivityCollection.Name)]
public sealed class MemberAccountDeletionHostedServiceTests
{
    [Fact]
    public void StartupDelay_is_longer_than_app_service_container_start_limit()
    {
        Assert.True(
            MemberAccountDeletionHostedService.DefaultStartupDelay > TimeSpan.FromSeconds(230));
        Assert.Equal(TimeSpan.FromMinutes(5), MemberAccountDeletionHostedService.DefaultStartupDelay);
    }

    [Fact]
    public async Task Does_not_purge_until_the_full_startup_delay_has_elapsed()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPurgeRepository();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(MemberAccountDeletionHostedService.DefaultStartupDelay - TimeSpan.FromTicks(1));

        Assert.Equal(0, repository.PurgeCalls);

        clock.Advance(TimeSpan.FromTicks(1));
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(1, repository.PurgeCalls);
    }

    [Fact]
    public async Task Purges_after_startup_delay_and_again_after_each_run_interval()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPurgeRepository();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(MemberAccountDeletionHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);

        Assert.Equal(1, repository.PurgeCalls);

        clock.Advance(MemberAccountDeletionHostedService.DefaultRunInterval);
        await clock.WaitForTimersCreatedAsync(3);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(2, repository.PurgeCalls);
    }

    [Fact]
    public async Task Stop_during_startup_delay_does_not_purge()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPurgeRepository();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        await hosted.StopAsync(CancellationToken.None);
        clock.Advance(MemberAccountDeletionHostedService.DefaultStartupDelay);

        Assert.Equal(0, repository.PurgeCalls);
    }

    [Fact]
    public async Task Purge_starts_MemberAccountDeletion_activity_during_scoped_work()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPurgeRepository();
        using var listener = QueenZoneActivityTestListener.Listen();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(MemberAccountDeletionHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(1, repository.PurgeCalls);
        var activity = Assert.Single(listener.Started, item => item.OperationName == "MemberAccountDeletion");
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.NotNull(repository.ActivityDuringWork);
        Assert.Equal("MemberAccountDeletion", repository.ActivityDuringWork.OperationName);
        Assert.Equal(activity.Id, repository.ActivityDuringWork.Id);
        Assert.True(activity.IsStopped);
        Assert.Contains(listener.Stopped, item => item.Id == activity.Id);
    }

    private static MemberAccountDeletionHostedService CreateHostedService(
        RecordingPurgeRepository repository,
        TimeProvider clock)
    {
        var memberService = new MemberAccountService(
            repository,
            new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>()),
            new AzureBlobUploadService(
                new InMemoryBlobStorageBackend(),
                Options.Create(new BlobUploadOptions())),
            new MemberUploadQuotaService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                TimeProvider.System,
                Options.Create(new UploadQuotaOptions { Enabled = false })));

        var services = new ServiceCollection();
        services.AddSingleton(memberService);
        var provider = services.BuildServiceProvider();

        return new MemberAccountDeletionHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<MemberAccountDeletionHostedService>.Instance);
    }

    private sealed class RecordingPurgeRepository : IMemberAccountRepository
    {
        public int PurgeCalls;

        public Activity? ActivityDuringWork;

        public Task<MemberAccountDeletionPurgeResult> PurgeDeletedAccountsAsync(
            DateTime purgeBefore,
            DateTime purgedAt,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PurgeCalls);
            ActivityDuringWork = Activity.Current;
            return Task.FromResult(new MemberAccountDeletionPurgeResult(0, []));
        }

        public Task<IReadOnlyList<PendingMemberDeletionBlob>> ListPendingDeletionBlobsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PendingMemberDeletionBlob>>([]);

        public Task CompleteDeletionBlobAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<MemberDeletionProgress?> GetDeletionProgressAsync(Guid memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MemberDeletionProgress?>(null);

        public Task<MemberAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> ListActiveMemberIdsAsync(
            IReadOnlyCollection<Guid> memberIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, string>> ListDisplayNamesAsync(
            IReadOnlyCollection<Guid> memberIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> FindByExternalLoginAsync(
            string provider,
            string providerKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListExternalProvidersAsync(
            Guid memberAccountId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount> CreateAsync(MemberAccount account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddExternalLoginAsync(
            Guid memberAccountId,
            string provider,
            string providerKey,
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAppleRefreshTokenAsync(Guid memberAccountId, string providerKey, string protectedToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PendingAppleRevocation>> ListPendingAppleRevocationsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PendingAppleRevocation>>([]);

        public Task CompleteAppleRevocationAsync(Guid externalLoginId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<MemberAccount?> UpdateDisplayNameAsync(
            Guid memberId,
            string displayName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> UpdateAvatarUrlAsync(
            Guid memberId,
            string? avatarBlobPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> UpdateMessagePrivacyAsync(
            Guid memberId,
            MemberMessagePrivacy messagePrivacy,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> FindByLinkedLegacyUserIdAsync(
            int legacyUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> LinkLegacyUserIdAsync(
            Guid memberId,
            int legacyUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> UnlinkLegacyUserIdAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordLoginAsync(
            Guid memberId,
            DateTime loginAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordPasswordFailureAsync(
            Guid memberId,
            int failureCount,
            DateTime windowStartedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordPasswordSignInAsync(
            Guid memberId,
            DateTime loginAt,
            string? rehashedPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberStats> GetStatsAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RecentLogin>> GetRecentLoginsAsync(
            int count,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DailyRegistration>> GetDailyRegistrationsAsync(
            DateOnly fromDate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MemberRecipientMatch>> SearchByDisplayNameAsync(
            string query,
            Guid? excludeMemberId = null,
            int maxResults = PrivateMessageLimits.MaxRecipientSearchResults,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberSearchResult> SearchMembersAsync(
            string? query,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LocalPasswordAccountSummary>> ListLocalPasswordAccountsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> UpdateLocalPasswordAccountAsync(
            Guid memberId,
            string email,
            string displayName,
            string? passwordHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RemoveLocalPasswordAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> SuspendAsync(
            Guid memberId,
            string reason,
            string suspendedByAdminEmail,
            DateTime suspendedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> ReinstateAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemberAccountDeletionRequestResult?> RequestDeletionAsync(
            Guid memberId,
            DateTime requestedAt,
            CancellationToken cancellationToken = default,
            bool immediate = false) =>
            throw new NotSupportedException();

        public Task<MemberAccount?> CancelDeletionAsync(
            Guid memberId,
            DateTime cancelledAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MemberSocialLink>> ListSocialLinksAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ReplaceSocialLinksAsync(
            Guid memberId,
            IReadOnlyList<MemberSocialLink> links,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
