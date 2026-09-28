using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class NotificationDispatcherTests
{
    [Fact]
    public async Task PrivateMessage_SendsRecipientDevices_ExcludesSender()
    {
        var (dispatcher, transport, _, tokens) = CreateDispatcher();
        var recipient = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(recipient, DevicePushPlatform.Apns, "recipient-phone"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(recipient, DevicePushPlatform.Fcm, "recipient-android"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(sender, DevicePushPlatform.Apns, "sender-phone"));

        await dispatcher.NotifyPrivateMessageAsync(conversationId, recipient, sender);

        var send = Assert.Single(transport.Sends);
        Assert.Equal(2, send.Tokens.Count);
        Assert.All(send.Tokens, token => Assert.Equal(recipient, token.MemberAccountId));
        Assert.Equal(NotificationCategoryNames.PrivateMessage, send.Payload.Category);
        Assert.Equal(conversationId.ToString(), send.Payload.Data["conversationId"]);
        Assert.False(string.Join(' ', send.Payload.Data.Values).Contains("recipient-phone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrivateMessage_PrefOff_SendsNothing()
    {
        var (dispatcher, transport, prefs, tokens) = CreateDispatcher();
        var recipient = Guid.NewGuid();
        await prefs.ApplyAsync(recipient, new NotificationPreferencePatch(null, false, null));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(recipient, DevicePushPlatform.Apns, "tok"));

        await dispatcher.NotifyPrivateMessageAsync(Guid.NewGuid(), recipient, Guid.NewGuid());

        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task PrivateMessage_NoTokens_SendsNothing()
    {
        var (dispatcher, transport, _, _) = CreateDispatcher();

        await dispatcher.NotifyPrivateMessageAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task PrivateMessage_SenderIsRecipient_SendsNothing()
    {
        var (dispatcher, transport, _, tokens) = CreateDispatcher();
        var member = Guid.NewGuid();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(member, DevicePushPlatform.Apns, "tok"));

        await dispatcher.NotifyPrivateMessageAsync(Guid.NewGuid(), member, member);

        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task ForumReply_UsesWatchers_ExcludesAuthor()
    {
        var watch = new FakeTopicWatchLookup();
        var (dispatcher, transport, _, tokens) = CreateDispatcher(watch);
        var author = Guid.NewGuid();
        var watcher = Guid.NewGuid();
        watch.Watchers[42] = [author, watcher];
        await tokens.UpsertAsync(DeviceTokenTestData.Token(author, DevicePushPlatform.Apns, "author-tok"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(watcher, DevicePushPlatform.Apns, "watcher-tok"));

        await dispatcher.NotifyForumReplyAsync(42, 99, author, "Topic title");

        var send = Assert.Single(transport.Sends);
        var device = Assert.Single(send.Tokens);
        Assert.Equal(watcher, device.MemberAccountId);
        Assert.Equal("forumReply", send.Payload.Category);
        Assert.Equal("42", send.Payload.Data["topicId"]);
        Assert.Equal("99", send.Payload.Data["postId"]);
        Assert.Equal("Topic title", send.Payload.Title);
    }

    [Fact]
    public async Task ForumReply_EmptyWatchers_SendsNothing()
    {
        var (dispatcher, transport, _, tokens) = CreateDispatcher();
        var author = Guid.NewGuid();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(author, DevicePushPlatform.Apns, "tok"));

        await dispatcher.NotifyForumReplyAsync(1, 2, author, "Title");

        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task ForumReply_PrefOff_SendsNothing()
    {
        var watch = new FakeTopicWatchLookup();
        var (dispatcher, transport, prefs, tokens) = CreateDispatcher(watch);
        var watcher = Guid.NewGuid();
        watch.Watchers[7] = [watcher];
        await prefs.ApplyAsync(watcher, new NotificationPreferencePatch(false, null, null));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(watcher, DevicePushPlatform.Apns, "tok"));

        await dispatcher.NotifyForumReplyAsync(7, 8, Guid.NewGuid(), "Title");

        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task News_UsesExplicitEnabledRowsOnly()
    {
        var (dispatcher, transport, prefs, tokens) = CreateDispatcher();
        var optedIn = Guid.NewGuid();
        var optedOut = Guid.NewGuid();
        var noRow = Guid.NewGuid();
        await prefs.ApplyAsync(optedIn, new NotificationPreferencePatch(null, null, true));
        await prefs.ApplyAsync(optedOut, new NotificationPreferencePatch(null, null, false));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(optedIn, DevicePushPlatform.Fcm, "news-tok"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(optedOut, DevicePushPlatform.Fcm, "out-tok"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(noRow, DevicePushPlatform.Fcm, "default-tok"));

        await dispatcher.NotifyNewsPublishedAsync(1003, "QueenZone modernisation begins");

        var send = Assert.Single(transport.Sends);
        var device = Assert.Single(send.Tokens);
        Assert.Equal(optedIn, device.MemberAccountId);
        Assert.Equal("news", send.Payload.Category);
        Assert.Equal("1003", send.Payload.Data["articleId"]);
    }

    [Fact]
    public async Task ProviderConfirmedUnregisteredToken_IsRemovedAfterSend()
    {
        var transport = new RecordingPushTransport();
        var (dispatcher, _, _, tokens) = CreateDispatcher(transport: transport);
        var recipient = Guid.NewGuid();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(
            recipient, DevicePushPlatform.Apns, "retired-apns", "device-one"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(
            recipient, DevicePushPlatform.Apns, "retired-apns", "device-two"));
        transport.UnregisteredTokens =
            [DeviceTokenTestData.PushToken(recipient, PushDevicePlatform.Apns, "retired-apns")];

        await dispatcher.NotifyPrivateMessageAsync(Guid.NewGuid(), recipient, Guid.NewGuid());

        Assert.Single(transport.Sends);
        Assert.Empty(await tokens.ListByMemberIdsAsync([recipient]));
    }

    [Fact]
    public async Task ProviderThrow_IsSwallowed()
    {
        var transport = new RecordingPushTransport
        {
            ThrowOnSend = new InvalidOperationException("provider down"),
        };
        var (dispatcher, _, _, tokens) = CreateDispatcher(transport: transport);
        var recipient = Guid.NewGuid();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(recipient, DevicePushPlatform.Apns, "tok"));

        var ex = await Record.ExceptionAsync(
            () => dispatcher.NotifyPrivateMessageAsync(Guid.NewGuid(), recipient, Guid.NewGuid()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DispatchLogs_DoNotIncludeDeviceToken()
    {
        var logger = new CollectingLogger<NotificationDispatcher>();
        var transport = new RecordingPushTransport
        {
            ThrowOnSend = new InvalidOperationException("boom-secret-token-xyz"),
        };
        var prefs = new InMemoryNotificationPreferenceRepository(new SharedNotificationPreferenceStore());
        var tokens = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());
        var dispatcher = new NotificationDispatcher(
            prefs,
            tokens,
            new EmptyTopicWatchLookup(),
            transport,
            logger);
        var recipient = Guid.NewGuid();
        const string deviceToken = "super-secret-device-token-xyz";
        await tokens.UpsertAsync(DeviceTokenTestData.Token(recipient, DevicePushPlatform.Apns, deviceToken));

        await dispatcher.NotifyPrivateMessageAsync(Guid.NewGuid(), recipient, Guid.NewGuid());

        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1400
                && entry.EventId.Name == "PushDispatchFailedForCategory"
                && entry.Message.Contains("Push dispatch failed for category", StringComparison.Ordinal)
                && entry.Exception is InvalidOperationException);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(deviceToken, StringComparison.Ordinal));
    }

    private static (
        NotificationDispatcher Dispatcher,
        RecordingPushTransport Transport,
        InMemoryNotificationPreferenceRepository Prefs,
        InMemoryDeviceTokenRepository Tokens) CreateDispatcher(
        ITopicWatchLookup? watch = null,
        RecordingPushTransport? transport = null)
    {
        transport ??= new RecordingPushTransport();
        var prefs = new InMemoryNotificationPreferenceRepository(new SharedNotificationPreferenceStore());
        var tokens = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());
        var dispatcher = new NotificationDispatcher(
            prefs,
            tokens,
            watch ?? new EmptyTopicWatchLookup(),
            transport,
            NullLogger<NotificationDispatcher>.Instance);
        return (dispatcher, transport, prefs, tokens);
    }
}
