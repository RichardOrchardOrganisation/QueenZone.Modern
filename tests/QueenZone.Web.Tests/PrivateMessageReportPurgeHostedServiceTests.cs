using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

[Collection(MaintenanceJobActivityCollection.Name)]
public sealed class PrivateMessageReportPurgeHostedServiceTests
{
    [Fact]
    public void StartupDelay_is_longer_than_app_service_container_start_limit()
    {
        Assert.True(
            PrivateMessageReportPurgeHostedService.DefaultStartupDelay > TimeSpan.FromSeconds(230));
        Assert.Equal(TimeSpan.FromMinutes(5), PrivateMessageReportPurgeHostedService.DefaultStartupDelay);
    }

    [Fact]
    public async Task Does_not_purge_until_the_full_startup_delay_has_elapsed()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPrivateMessageRepository();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(PrivateMessageReportPurgeHostedService.DefaultStartupDelay - TimeSpan.FromTicks(1));

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
        var repository = new RecordingPrivateMessageRepository();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(PrivateMessageReportPurgeHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);

        Assert.Equal(1, repository.PurgeCalls);

        clock.Advance(PrivateMessageReportPurgeHostedService.DefaultRunInterval);
        await clock.WaitForTimersCreatedAsync(3);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(2, repository.PurgeCalls);
    }

    [Fact]
    public async Task Stop_during_startup_delay_does_not_purge()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPrivateMessageRepository();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        await hosted.StopAsync(CancellationToken.None);
        clock.Advance(PrivateMessageReportPurgeHostedService.DefaultStartupDelay);

        Assert.Equal(0, repository.PurgeCalls);
    }

    [Fact]
    public async Task Purge_starts_PrivateMessageReportPurge_activity_during_scoped_work()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var repository = new RecordingPrivateMessageRepository();
        using var listener = QueenZoneActivityTestListener.Listen();
        using var hosted = CreateHostedService(repository, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(PrivateMessageReportPurgeHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(1, repository.PurgeCalls);
        var activity = Assert.Single(listener.Started, item => item.OperationName == "PrivateMessageReportPurge");
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.NotNull(repository.ActivityDuringWork);
        Assert.Equal("PrivateMessageReportPurge", repository.ActivityDuringWork.OperationName);
        Assert.Equal(activity.Id, repository.ActivityDuringWork.Id);
        Assert.True(activity.IsStopped);
        Assert.Contains(listener.Stopped, item => item.Id == activity.Id);
    }

    private static PrivateMessageReportPurgeHostedService CreateHostedService(
        RecordingPrivateMessageRepository repository,
        TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPrivateMessageModerationRepository>(repository);
        var provider = services.BuildServiceProvider();

        return new PrivateMessageReportPurgeHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<PrivateMessageReportPurgeHostedService>.Instance);
    }

    private sealed class RecordingPrivateMessageRepository
        : IPrivateMessageRepository, IPrivateMessageModerationRepository
    {
        public int PurgeCalls;

        public Activity? ActivityDuringWork;

        public Task<int> PurgeExpiredReportsAsync(
            DateTimeOffset asOfUtc,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PurgeCalls);
            ActivityDuringWork = Activity.Current;
            return Task.FromResult(0);
        }

        public Task<PrivateInboxPage> GetInboxAsync(
            Guid memberId,
            int page = 1,
            int pageSize = PrivateMessageLimits.InboxPageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountUnreadConversationsAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> ListBlockedMemberIdsAsync(
            Guid blockerMemberId,
            IReadOnlyCollection<Guid> candidateMemberIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateConversationDetail?> GetConversationAsync(
            Guid conversationId,
            Guid memberId,
            int? page = null,
            int pageSize = PrivateMessageLimits.ConversationPageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsParticipantAsync(
            Guid conversationId,
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkConversationReadAsync(
            Guid conversationId,
            Guid memberId,
            long lastReadSortKey,
            DateTimeOffset readAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateMessageSendResult> SendNewOrExistingAsync(
            Guid senderMemberId,
            Guid recipientMemberId,
            string body,
            DateTimeOffset sentAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateMessageSendResult> ReplyAsync(
            Guid conversationId,
            Guid senderMemberId,
            string body,
            DateTimeOffset sentAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateInboxPage> GetArchivedInboxAsync(
            Guid memberId,
            int page = 1,
            int pageSize = PrivateMessageLimits.InboxPageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ArchiveConversationAsync(
            Guid conversationId,
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UnarchiveConversationAsync(
            Guid conversationId,
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RemoveConversationAsync(
            Guid conversationId,
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetOtherParticipantIdAsync(
            Guid conversationId,
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasConversationBetweenAsync(
            Guid memberA,
            Guid memberB,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsBlockedAsync(
            Guid blockerMemberId,
            Guid blockedMemberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsMessagingBlockedAsync(
            Guid memberA,
            Guid memberB,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task BlockAsync(
            Guid blockerMemberId,
            Guid blockedMemberId,
            DateTimeOffset blockedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UnblockAsync(
            Guid blockerMemberId,
            Guid blockedMemberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountMessagesBySenderSinceAsync(
            Guid senderMemberId,
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountIdenticalMessagesBySenderSinceAsync(
            Guid senderMemberId,
            string body,
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountDistinctNewRecipientsSinceAsync(
            Guid senderMemberId,
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateMessageReportResult> CreateReportAsync(
            Guid reporterMemberId,
            Guid conversationId,
            Guid messageId,
            string? reason,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateMessageReport?> GetReportAsync(
            Guid reportId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetReportedMessageIdsAsync(
            Guid conversationId,
            Guid reporterMemberId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateMessageReportListPage> ListReportsAsync(
            string? status,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountOpenReportsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PrivateMessageReport?> UpdateReportStatusAsync(
            Guid reportId,
            string status,
            string actorEmail,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AppendReportViewedAuditAsync(
            Guid reportId,
            string actorEmail,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
