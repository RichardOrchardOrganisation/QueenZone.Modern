using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class NotificationDispatchWritePathTests : IClassFixture<WebHostVariantCache>, IAsyncLifetime
{
    private readonly VariantWebApplicationFactory recording;
    private readonly VariantWebApplicationFactory recordingFakeWatch;
    private readonly VariantWebApplicationFactory recordingAlwaysWatch;
    private readonly VariantWebApplicationFactory throwingDispatcher;

    public NotificationDispatchWritePathTests(WebHostVariantCache variants)
    {
        recording = variants.Get(WebHostVariants.TestingRecordingPushDispatch);
        recordingFakeWatch = variants.Get(WebHostVariants.TestingRecordingPushDispatchFakeWatch);
        recordingAlwaysWatch = variants.Get(WebHostVariants.TestingRecordingPushDispatchAlwaysWatch);
        throwingDispatcher = variants.Get(WebHostVariants.TestingThrowingNotificationDispatcher);
    }

    public async Task InitializeAsync()
    {
        await recording.ResetAsync();
        await recordingFakeWatch.ResetAsync();
        await recordingAlwaysWatch.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Compose_DispatchesOnce_ToRecipientOnly()
    {
        var (service, transport, tokens, alice, bob) = CreatePrivateMessageSystem();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(bob.Id, DevicePushPlatform.Apns, "bob-tok"));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Apns, "alice-tok"));

        var result = await service.ComposeAsync(alice.Id, bob.Id, "Hello Bob");

        Assert.True(result.Succeeded);
        var send = Assert.Single(transport.Sends);
        Assert.Equal(NotificationCategoryNames.PrivateMessage, send.Payload.Category);
        Assert.Equal(result.ConversationId.ToString(), send.Payload.Data["conversationId"]);
        Assert.Equal(bob.Id, Assert.Single(send.Tokens).MemberAccountId);
    }

    [Fact]
    public async Task Reply_DispatchesOnce_ToOtherParticipant()
    {
        var (service, transport, tokens, alice, bob) = CreatePrivateMessageSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        transport.Sends.Clear();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Apns, "alice-tok"));

        var reply = await service.ReplyAsync(created.ConversationId!.Value, bob.Id, "Hi Alice");

        Assert.True(reply.Succeeded);
        var send = Assert.Single(transport.Sends);
        Assert.Equal(alice.Id, Assert.Single(send.Tokens).MemberAccountId);
    }

    [Fact]
    public async Task FailedCompose_DoesNotDispatch()
    {
        var (service, transport, _, alice, _) = CreatePrivateMessageSystem();

        var result = await service.ComposeAsync(alice.Id, alice.Id, "self");

        Assert.False(result.Succeeded);
        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task ProviderThrow_DoesNotFailPrivateMessageWrite()
    {
        var (service, transport, tokens, alice, bob) = CreatePrivateMessageSystem();
        await tokens.UpsertAsync(DeviceTokenTestData.Token(bob.Id, DevicePushPlatform.Apns, "bob-tok"));
        transport.ThrowOnSend = new InvalidOperationException("APNs down");

        var result = await service.ComposeAsync(alice.Id, bob.Id, "Still delivered");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ConversationId);
        var inbox = await service.GetInboxAsync(bob.Id);
        Assert.Equal("Still delivered", Assert.Single(inbox.Items).LastMessagePreview);
    }

    [Fact]
    public async Task DispatcherThrow_DoesNotFailPrivateMessageWrite()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "dispatcher-throw-alice@example.com",
            DisplayName = "Alice",
            CreatedAt = DateTime.UtcNow,
        });
        var bob = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "dispatcher-throw-bob@example.com",
            DisplayName = "Bob",
            CreatedAt = DateTime.UtcNow,
        });
        var messages = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());
        var follows = new InMemoryMemberFollowRepository();
        var rateLimiter = new PrivateMessageRateLimiter(
            messages,
            TimeProvider.System,
            Options.Create(new PrivateMessageRateLimitOptions
            {
                WindowMinutes = 10,
                MaxMessagesPerWindow = 1000,
                MaxNewRecipientsPerWindow = 1000,
                MaxDuplicateMessagesPerWindow = 1000,
                NewAccountAgeDays = 3,
                NewAccountMaxMessagesPerWindow = 1000,
                NewAccountMaxNewRecipientsPerWindow = 1000,
            }),
            NullLogger<PrivateMessageRateLimiter>.Instance);
        var logger = new CollectingLogger<PrivateMessageService>();
        var service = new PrivateMessageService(
            messages,
            messages,
            members,
            follows,
            rateLimiter,
            new ThrowingNotificationDispatcher(new InvalidOperationException("dispatcher down")),
            logger,
            TimeProvider.System);

        var result = await service.ComposeAsync(alice.Id, bob.Id, "Still delivered despite dispatcher failure");

        Assert.True(result.Succeeded);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1200
                && entry.EventId.Name == "PushDispatchFailedAfterPrivateMessage"
                && entry.Message.Contains(
                    "Push dispatch failed after private message to member",
                    StringComparison.Ordinal)
                && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task ForumReply_DispatchesOnce_WhenWatchersExist()
    {
        var transport = recordingFakeWatch.PushTransport!;
        var watch = recordingFakeWatch.TopicWatch!;
        var author = Guid.NewGuid();
        var watcher = Guid.NewGuid();

        await SeedFactoryTokenAsync(recordingFakeWatch, watcher, "watcher-tok");
        await SeedFactoryTokenAsync(recordingFakeWatch, author, "author-tok");

        using (var scope = recordingFakeWatch.Services.CreateScope())
        {
            var write = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
            var topic = await write.CreateTopicAsync(new NewForumTopicRequest(author, "Author", 1, "Dispatch watch thread", "Starter body", Attachments: null, Poll: null));
            Assert.True(topic.Succeeded);
            watch.Watchers[topic.TopicId] = [author, watcher];

            var reply = await write.CreateReplyAsync(
                author,
                "Author",
                topic.TopicId,
                "A reply that should notify watchers",
                attachments: null);
            Assert.True(reply.Succeeded);

            var send = Assert.Single(transport.Sends);
            Assert.Equal(NotificationCategoryNames.ForumReply, send.Payload.Category);
            Assert.Equal(topic.TopicId.ToString(), send.Payload.Data["topicId"]);
            Assert.Equal(reply.PostId.ToString(), send.Payload.Data["postId"]);
            Assert.Equal(watcher, Assert.Single(send.Tokens).MemberAccountId);
        }
    }

    [Fact]
    public async Task ForumReply_DispatcherThrow_DoesNotFailTheReply()
    {
        var author = Guid.NewGuid();
        await SeedFactoryTokenAsync(throwingDispatcher, author, "author-tok");

        using var scope = throwingDispatcher.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var topic = await write.CreateTopicAsync(new NewForumTopicRequest(author, "Author", 1, "Dispatcher throw thread", "Starter body", Attachments: null, Poll: null));
        Assert.True(topic.Succeeded);

        var reply = await write.CreateReplyAsync(
            author,
            "Author",
            topic.TopicId,
            "A reply that should still succeed despite dispatcher failure",
            attachments: null);

        Assert.True(reply.Succeeded);
    }

    [Fact]
    public async Task ForumReply_PersistedWatch_DispatchesOnce_ExcludingAuthor()
    {
        var transport = recording.PushTransport!;
        var author = Guid.NewGuid();
        var watcher = Guid.NewGuid();
        var lurker = Guid.NewGuid();
        await SeedFactoryTokenAsync(recording, watcher, "watcher-tok");
        await SeedFactoryTokenAsync(recording, author, "author-tok");
        await SeedFactoryTokenAsync(recording, lurker, "lurker-tok");

        using var scope = recording.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var watches = scope.ServiceProvider.GetRequiredService<ITopicWatchRepository>();
        var topic = await write.CreateTopicAsync(new NewForumTopicRequest(author, "Author", 1, "Persisted watch thread", "Starter body", Attachments: null, Poll: null));
        Assert.True(topic.Succeeded);

        await watches.WatchAsync(watcher, topic.TopicId, DateTimeOffset.UtcNow);
        await watches.WatchAsync(author, topic.TopicId, DateTimeOffset.UtcNow);
        await watches.WatchAsync(watcher, topic.TopicId, DateTimeOffset.UtcNow);

        var reply = await write.CreateReplyAsync(
            author,
            "Author",
            topic.TopicId,
            "A reply that should notify the Watcher only",
            attachments: null);
        Assert.True(reply.Succeeded);

        var send = Assert.Single(transport.Sends);
        Assert.Equal(NotificationCategoryNames.ForumReply, send.Payload.Category);
        Assert.Equal(topic.TopicId.ToString(), send.Payload.Data["topicId"]);
        Assert.Equal(reply.PostId.ToString(), send.Payload.Data["postId"]);
        Assert.Equal(watcher, Assert.Single(send.Tokens).MemberAccountId);

        await watches.UnwatchAsync(watcher, topic.TopicId);
        await watches.UnwatchAsync(author, topic.TopicId);
        transport.Sends.Clear();
        var second = await write.CreateReplyAsync(
            lurker,
            "Lurker",
            topic.TopicId,
            "No watchers left except the author",
            attachments: null);
        Assert.True(second.Succeeded);
        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task ForumReply_EmptyWatchers_SendsNothing()
    {
        var transport = recording.PushTransport!;

        using var scope = recording.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var topic = await write.CreateTopicAsync(new NewForumTopicRequest(Guid.NewGuid(), "Author", 1, "Empty watch thread", "Starter", Attachments: null, Poll: null));
        transport.Sends.Clear();

        var reply = await write.CreateReplyAsync(
            Guid.NewGuid(),
            "Replier",
            topic.TopicId,
            "Reply with no watchers",
            attachments: null);

        Assert.True(reply.Succeeded);
        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task CreateTopic_DoesNotDispatch()
    {
        var transport = recordingAlwaysWatch.PushTransport!;
        var watcher = Guid.NewGuid();
        recordingAlwaysWatch.AlwaysWatch!.MemberId = watcher;

        await SeedFactoryTokenAsync(recordingAlwaysWatch, watcher, "watcher-tok");
        using var scope = recordingAlwaysWatch.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var outcome = await write.CreateTopicAsync(new NewForumTopicRequest(Guid.NewGuid(), "Author", 1, "No notify on start", "Starter", Attachments: null, Poll: null));

        Assert.True(outcome.Succeeded);
        Assert.Empty(transport.Sends);
    }

    [Fact]
    public async Task ProviderThrow_DoesNotFailForumReplyWrite()
    {
        var transport = recordingFakeWatch.PushTransport!;
        transport.ThrowOnSend = new InvalidOperationException("FCM down");
        var watch = recordingFakeWatch.TopicWatch!;
        var watcher = Guid.NewGuid();

        await SeedFactoryTokenAsync(recordingFakeWatch, watcher, "watcher-tok");
        using var scope = recordingFakeWatch.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var topic = await write.CreateTopicAsync(new NewForumTopicRequest(Guid.NewGuid(), "Author", 1, "Throwing transport thread", "Starter", Attachments: null, Poll: null));
        watch.Watchers[topic.TopicId] = [watcher];

        var reply = await write.CreateReplyAsync(
            Guid.NewGuid(),
            "Replier",
            topic.TopicId,
            "Reply that must persist",
            attachments: null);

        Assert.True(reply.Succeeded);
        Assert.True(reply.PostId > 0);
    }

    [Fact]
    public async Task NewsDraftSave_SendsNothing_FirstPublish_SendsOnce()
    {
        var transport = new RecordingPushTransport();
        var newsStore = new SharedNewsStore();
        var prefs = new InMemoryNotificationPreferenceRepository(new SharedNotificationPreferenceStore());
        var tokens = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());
        var subscriber = Guid.NewGuid();
        await prefs.ApplyAsync(subscriber, new NotificationPreferencePatch(null, null, true));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(subscriber, DevicePushPlatform.Fcm, "news-tok"));
        var dispatcher = new NotificationDispatcher(
            prefs,
            tokens,
            new EmptyTopicWatchLookup(),
            transport,
            NullLogger<NotificationDispatcher>.Instance);
        var admin = new InMemoryAdminNewsRepository(newsStore);
        var write = new AdminNewsWriteService(
            admin,
            dispatcher,
            new NoOpNewsForumTopicService(),
            NullLogger<AdminNewsWriteService>.Instance);

        var draftId = await admin.CreateDraftAsync(
            new AdminNewsDraft(
                "Dispatch article",
                null,
                "Excerpt",
                "Body",
                DateTime.UtcNow.Date,
                null),
            "editor@test.local");
        var draft = await admin.GetByIdAsync(draftId);
        Assert.NotNull(draft);
        Assert.False(draft.IsPublished);
        Assert.Empty(transport.Sends);

        await admin.UpdateAsync(
            draftId,
            new AdminNewsDraft(
                "Dispatch article edited",
                null,
                "Excerpt",
                "Body edited",
                DateTime.UtcNow.Date,
                null),
            "editor@test.local");
        Assert.Empty(transport.Sends);

        var edited = await admin.GetByIdAsync(draftId);
        Assert.NotNull(edited);
        await write.PublishAsync(edited, "editor@test.local");
        Assert.Single(transport.Sends);
        Assert.Equal("news", transport.Sends[0].Payload.Category);
        Assert.Equal(draftId.ToString(), transport.Sends[0].Payload.Data["articleId"]);

        var published = await admin.GetByIdAsync(draftId);
        Assert.True(published!.IsPublished);
        await write.PublishAsync(published, "editor@test.local");
        Assert.Single(transport.Sends);
    }

    [Fact]
    public async Task ProviderThrow_DoesNotFailNewsPublish()
    {
        var transport = new RecordingPushTransport
        {
            ThrowOnSend = new InvalidOperationException("FCM down"),
        };
        var newsStore = new SharedNewsStore();
        var prefs = new InMemoryNotificationPreferenceRepository(new SharedNotificationPreferenceStore());
        var tokens = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());
        var subscriber = Guid.NewGuid();
        await prefs.ApplyAsync(subscriber, new NotificationPreferencePatch(null, null, true));
        await tokens.UpsertAsync(DeviceTokenTestData.Token(subscriber, DevicePushPlatform.Fcm, "news-tok"));
        var write = new AdminNewsWriteService(
            new InMemoryAdminNewsRepository(newsStore),
            new NotificationDispatcher(
                prefs,
                tokens,
                new EmptyTopicWatchLookup(),
                transport,
                NullLogger<NotificationDispatcher>.Instance),
            new NoOpNewsForumTopicService(),
            NullLogger<AdminNewsWriteService>.Instance);
        var id = newsStore.CreateDraft(
            new AdminNewsDraft("Throw publish", null, "Ex", "Body", DateTime.UtcNow.Date, null),
            "editor@test.local");
        var article = newsStore.GetArticle(id)!;

        await write.PublishAsync(article, "editor@test.local");

        Assert.True(newsStore.GetArticle(id)!.IsPublished);
    }

    private static (
        PrivateMessageService Service,
        RecordingPushTransport Transport,
        InMemoryDeviceTokenRepository Tokens,
        MemberAccount Alice,
        MemberAccount Bob) CreatePrivateMessageSystem()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "dispatch-alice@example.com",
            DisplayName = "Alice",
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();
        var bob = members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "dispatch-bob@example.com",
            DisplayName = "Bob",
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();
        var messages = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());
        var follows = new InMemoryMemberFollowRepository();
        var rateLimiter = new PrivateMessageRateLimiter(
            messages,
            TimeProvider.System,
            Options.Create(new PrivateMessageRateLimitOptions
            {
                WindowMinutes = 10,
                MaxMessagesPerWindow = 1000,
                MaxNewRecipientsPerWindow = 1000,
                MaxDuplicateMessagesPerWindow = 1000,
                NewAccountAgeDays = 3,
                NewAccountMaxMessagesPerWindow = 1000,
                NewAccountMaxNewRecipientsPerWindow = 1000,
            }),
            NullLogger<PrivateMessageRateLimiter>.Instance);
        var transport = new RecordingPushTransport();
        var tokens = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());
        var dispatcher = new NotificationDispatcher(
            new InMemoryNotificationPreferenceRepository(new SharedNotificationPreferenceStore()),
            tokens,
            new EmptyTopicWatchLookup(),
            transport,
            NullLogger<NotificationDispatcher>.Instance);
        var service = new PrivateMessageService(
            messages,
            messages,
            members,
            follows,
            rateLimiter,
            dispatcher,
            NullLogger<PrivateMessageService>.Instance,
            TimeProvider.System);
        return (service, transport, tokens, alice, bob);
    }

    private static async Task SeedFactoryTokenAsync(
        QueenZoneWebApplicationFactory scopedFactory,
        Guid memberId,
        string token)
    {
        using var scope = scopedFactory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDeviceTokenRepository>();
        await repository.UpsertAsync(DeviceTokenTestData.Token(memberId, DevicePushPlatform.Apns, token));
    }

    private sealed class NoOpNewsForumTopicService : INewsForumTopicService
    {
        public Task EnsureTopicOnFirstPublishAsync(
            AdminNewsArticle article,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
