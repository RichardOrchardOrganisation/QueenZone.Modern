using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfPrivateMessageRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfPrivateMessageRepository repository;
    private readonly EfPrivateMessageModerationRepository moderation;
    private readonly Guid aliceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid bobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid carolId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public EfPrivateMessageRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        dbContext.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = aliceId,
                Email = "alice-ef@example.com",
                NormalizedEmail = "ALICE-EF@EXAMPLE.COM",
                DisplayName = "Alice EF",
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = bobId,
                Email = "bob-ef@example.com",
                NormalizedEmail = "BOB-EF@EXAMPLE.COM",
                DisplayName = "Bob EF",
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = carolId,
                Email = "carol-ef@example.com",
                NormalizedEmail = "CAROL-EF@EXAMPLE.COM",
                DisplayName = "Carol EF",
                CreatedAt = DateTime.UtcNow,
            });
        dbContext.SaveChanges();

        repository = new EfPrivateMessageRepository(dbContext);
        moderation = new EfPrivateMessageModerationRepository(dbContext);
    }

    [Fact]
    public async Task Send_And_GetConversation_RoundTrip()
    {
        var sentAt = DateTimeOffset.Parse("2026-08-02T10:00:00Z");
        var result = await repository.SendNewOrExistingAsync(aliceId, bobId, "EF hello", sentAt);
        Assert.True(result.Succeeded);

        var detail = await repository.GetConversationAsync(result.ConversationId!.Value, bobId);
        Assert.NotNull(detail);
        Assert.Equal("Alice EF", detail!.OtherParticipantDisplayName);
        Assert.Equal("EF hello", Assert.Single(detail.Messages).Body);

        Assert.True(await repository.IsParticipantAsync(result.ConversationId.Value, aliceId));
        Assert.False(await repository.IsParticipantAsync(result.ConversationId.Value, carolId));
        Assert.True(await repository.HasConversationBetweenAsync(aliceId, bobId));
        Assert.True(await repository.HasConversationBetweenAsync(bobId, aliceId));
        Assert.False(await repository.HasConversationBetweenAsync(aliceId, carolId));
    }

    [Fact]
    public async Task Block_Unblock_AndOtherParticipant_RoundTrip()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Before block",
            DateTimeOffset.Parse("2026-08-05T10:00:00Z"));
        Assert.True(created.Succeeded);
        var conversationId = created.ConversationId!.Value;

        Assert.Equal(bobId, await repository.GetOtherParticipantIdAsync(conversationId, aliceId));
        Assert.Null(await repository.GetOtherParticipantIdAsync(conversationId, carolId));

        await moderation.BlockAsync(aliceId, bobId, DateTimeOffset.Parse("2026-08-05T10:05:00Z"));
        Assert.True(await moderation.IsBlockedAsync(aliceId, bobId));
        Assert.False(await moderation.IsBlockedAsync(bobId, aliceId));
        Assert.True(await moderation.IsMessagingBlockedAsync(aliceId, bobId));

        // Idempotent second block.
        await moderation.BlockAsync(aliceId, bobId, DateTimeOffset.Parse("2026-08-05T10:06:00Z"));
        Assert.Equal(1, await dbContext.MemberMessageBlocks.CountAsync());

        Assert.True(await moderation.UnblockAsync(aliceId, bobId));
        Assert.False(await moderation.IsBlockedAsync(aliceId, bobId));
        Assert.False(await moderation.UnblockAsync(aliceId, bobId));
    }

    [Fact]
    public async Task ListBlockedMemberIdsAsync_ReturnsOnlyBlockedCandidates()
    {
        await moderation.BlockAsync(aliceId, bobId, DateTimeOffset.Parse("2026-08-05T10:05:00Z"));

        var blocked = await moderation.ListBlockedMemberIdsAsync(aliceId, [bobId, carolId]);

        Assert.Equal([bobId], blocked);
    }

    [Fact]
    public async Task ListBlockedMemberIdsAsync_IsDirectional()
    {
        await moderation.BlockAsync(aliceId, bobId, DateTimeOffset.Parse("2026-08-05T10:05:00Z"));

        // Bob has not blocked Alice, so the reverse lookup stays empty.
        Assert.Empty(await moderation.ListBlockedMemberIdsAsync(bobId, [aliceId, carolId]));
    }

    [Fact]
    public async Task ListBlockedMemberIdsAsync_EmptyInput_DoesNotQuery()
    {
        Assert.Empty(await moderation.ListBlockedMemberIdsAsync(aliceId, []));
    }

    [Fact]
    public async Task UnreadCount_AndMarkRead_ArePerParticipant()
    {
        var result = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Unread me",
            DateTimeOffset.Parse("2026-08-02T11:00:00Z"));

        Assert.Equal(1, await repository.CountUnreadConversationsAsync(bobId));
        Assert.Equal(0, await repository.CountUnreadConversationsAsync(aliceId));

        var bobView = await repository.GetConversationAsync(result.ConversationId!.Value, bobId);
        var last = Assert.Single(bobView!.Messages);
        await repository.MarkConversationReadAsync(
            result.ConversationId!.Value,
            bobId,
            last.SortKey,
            last.CreatedAt);
        Assert.Equal(0, await repository.CountUnreadConversationsAsync(bobId));

        await repository.ReplyAsync(
            result.ConversationId.Value,
            bobId,
            "Reply",
            DateTimeOffset.Parse("2026-08-02T11:10:00Z"));
        Assert.Equal(1, await repository.CountUnreadConversationsAsync(aliceId));
    }

    [Fact]
    public async Task Inbox_OrdersByLastMessageSortKey_AndHidesOtherConversations()
    {
        await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Older",
            DateTimeOffset.Parse("2026-08-02T08:00:00Z"));
        await repository.SendNewOrExistingAsync(
            aliceId,
            carolId,
            "Newer",
            DateTimeOffset.Parse("2026-08-02T09:00:00Z"));

        var inbox = await repository.GetInboxAsync(aliceId);
        Assert.Equal(["Carol EF", "Bob EF"], inbox.Items.Select(i => i.OtherParticipantDisplayName).ToArray());

        var bobInbox = await repository.GetInboxAsync(bobId);
        Assert.DoesNotContain(bobInbox.Items, i => i.OtherParticipantId == carolId);
    }

    [Fact]
    public async Task Inbox_OrdersByLastMessageSortKey_EvenWhenTimestampsSkew()
    {
        // Insert Carol first with a late wall-clock timestamp, then Bob with an earlier timestamp.
        // SortKey (insert order) must win inbox ranking over LastMessageAt.
        await repository.SendNewOrExistingAsync(
            aliceId,
            carolId,
            "Carol first insert",
            DateTimeOffset.Parse("2026-08-02T20:00:00Z"));
        await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Bob later insert, earlier clock",
            DateTimeOffset.Parse("2026-08-02T08:00:00Z"));

        var inbox = await repository.GetInboxAsync(aliceId);
        Assert.Equal(["Bob EF", "Carol EF"], inbox.Items.Select(i => i.OtherParticipantDisplayName).ToArray());
        Assert.True(inbox.Items[0].LastMessageAt < inbox.Items[1].LastMessageAt);

        var bobConversation = await dbContext.PrivateConversations
            .AsNoTracking()
            .SingleAsync(c =>
                (c.MemberLowId == aliceId && c.MemberHighId == bobId)
                || (c.MemberLowId == bobId && c.MemberHighId == aliceId));
        var carolConversation = await dbContext.PrivateConversations
            .AsNoTracking()
            .SingleAsync(c =>
                (c.MemberLowId == aliceId && c.MemberHighId == carolId)
                || (c.MemberLowId == carolId && c.MemberHighId == aliceId));
        Assert.True(bobConversation.LastMessageSortKey > carolConversation.LastMessageSortKey);
    }

    [Fact]
    public async Task Reply_RejectsNonParticipant()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Private",
            DateTimeOffset.UtcNow);
        var reply = await repository.ReplyAsync(
            created.ConversationId!.Value,
            carolId,
            "Nope",
            DateTimeOffset.UtcNow);
        Assert.False(reply.Succeeded);
    }

    [Fact]
    public async Task Archive_HidesConversationFromInbox_ButNotForOtherParticipant()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Archive me",
            DateTimeOffset.Parse("2026-08-03T09:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        Assert.True(await repository.ArchiveConversationAsync(conversationId, aliceId));

        var aliceInbox = await repository.GetInboxAsync(aliceId);
        Assert.Empty(aliceInbox.Items);

        var bobInbox = await repository.GetInboxAsync(bobId);
        Assert.Single(bobInbox.Items);

        var aliceArchived = await repository.GetArchivedInboxAsync(aliceId);
        Assert.Single(aliceArchived.Items);
        Assert.Equal(bobId, aliceArchived.Items[0].OtherParticipantId);
    }

    [Fact]
    public async Task Archive_ReturnsFalse_WhenMemberIsNotParticipant()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Not yours",
            DateTimeOffset.Parse("2026-08-03T09:05:00Z"));

        Assert.False(await repository.ArchiveConversationAsync(created.ConversationId!.Value, carolId));
    }

    [Fact]
    public async Task NewMessage_UnarchivesConversation_ForBothParticipants()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Start",
            DateTimeOffset.Parse("2026-08-03T09:10:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repository.ArchiveConversationAsync(conversationId, aliceId);
        Assert.Empty((await repository.GetInboxAsync(aliceId)).Items);

        await repository.ReplyAsync(
            conversationId,
            bobId,
            "New reply reopens it",
            DateTimeOffset.Parse("2026-08-03T09:11:00Z"));

        var aliceInbox = await repository.GetInboxAsync(aliceId);
        Assert.Single(aliceInbox.Items);

        var aliceArchived = await repository.GetArchivedInboxAsync(aliceId);
        Assert.Empty(aliceArchived.Items);
    }

    [Fact]
    public async Task Unarchive_MovesConversationBackToInbox()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Toggle me",
            DateTimeOffset.Parse("2026-08-03T09:15:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repository.ArchiveConversationAsync(conversationId, aliceId);
        Assert.True(await repository.UnarchiveConversationAsync(conversationId, aliceId));

        Assert.Single((await repository.GetInboxAsync(aliceId)).Items);
        Assert.Empty((await repository.GetArchivedInboxAsync(aliceId)).Items);
    }

    [Fact]
    public async Task Remove_HidesConversationFromInbox_ButNotForOtherParticipant()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Remove me",
            DateTimeOffset.Parse("2026-08-03T09:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        Assert.True(await repository.RemoveConversationAsync(conversationId, aliceId));

        var aliceInbox = await repository.GetInboxAsync(aliceId);
        Assert.Empty(aliceInbox.Items);

        var bobInbox = await repository.GetInboxAsync(bobId);
        Assert.Single(bobInbox.Items);
    }

    [Fact]
    public async Task Remove_ReturnsFalse_WhenMemberIsNotParticipant()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Not yours",
            DateTimeOffset.Parse("2026-08-03T09:05:00Z"));

        Assert.False(await repository.RemoveConversationAsync(created.ConversationId!.Value, carolId));
    }

    [Fact]
    public async Task Remove_ExcludesConversationFromUnreadCount()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Unread then removed",
            DateTimeOffset.Parse("2026-08-03T09:07:00Z"));
        var conversationId = created.ConversationId!.Value;

        Assert.Equal(1, await repository.CountUnreadConversationsAsync(bobId));

        Assert.True(await repository.RemoveConversationAsync(conversationId, bobId));
        Assert.Equal(0, await repository.CountUnreadConversationsAsync(bobId));
    }

    [Fact]
    public async Task NewMessage_RestoresRemovedConversation_ForBothParticipants()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Start",
            DateTimeOffset.Parse("2026-08-03T09:10:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repository.RemoveConversationAsync(conversationId, aliceId);
        Assert.Empty((await repository.GetInboxAsync(aliceId)).Items);

        await repository.ReplyAsync(
            conversationId,
            bobId,
            "New reply reopens it",
            DateTimeOffset.Parse("2026-08-03T09:11:00Z"));

        var aliceInbox = await repository.GetInboxAsync(aliceId);
        Assert.Single(aliceInbox.Items);
    }

    [Fact]
    public async Task Reply_UpdatesPreviewAndSortKeyTip_KeepsMonotonicLastMessageAt()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Start",
            DateTimeOffset.Parse("2026-08-02T12:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repository.ReplyAsync(
            conversationId,
            bobId,
            "Newer reply",
            DateTimeOffset.Parse("2026-08-02T12:02:00Z"));
        await repository.ReplyAsync(
            conversationId,
            aliceId,
            "Older reply commits later",
            DateTimeOffset.Parse("2026-08-02T12:01:00Z"));

        var inbox = await repository.GetInboxAsync(aliceId);
        var item = Assert.Single(inbox.Items);
        // Insert order / SortKey wins for preview and tip ranking; LastMessageAt stays monotonic.
        Assert.Equal("Older reply commits later", item.LastMessagePreview);
        Assert.Equal(DateTimeOffset.Parse("2026-08-02T12:02:00Z"), item.LastMessageAt);

        var conversation = await dbContext.PrivateConversations
            .AsNoTracking()
            .SingleAsync(c => c.Id == conversationId);
        var tipSortKey = await dbContext.PrivateMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .MaxAsync(m => m.SortKey);
        Assert.Equal(tipSortKey, conversation.LastMessageSortKey);

        var detail = await repository.GetConversationAsync(conversationId, aliceId);
        Assert.Equal(3, detail!.Messages.Count);
        Assert.Equal("Older reply commits later", detail.Messages[^1].Body);
    }

    [Fact]
    public async Task ConcurrentFirstSends_ReuseSingleConversation()
    {
        const string shared = "Data Source=file:pm-race?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(shared);
        keepAlive.Open();

        await using (var setup = CreateContext(shared))
        {
            setup.Database.EnsureCreated();
            setup.MemberAccounts.AddRange(
                new MemberAccount
                {
                    Id = aliceId,
                    Email = "race-alice@example.com",
                    NormalizedEmail = "RACE-ALICE@EXAMPLE.COM",
                    DisplayName = "Race Alice",
                    CreatedAt = DateTime.UtcNow,
                },
                new MemberAccount
                {
                    Id = bobId,
                    Email = "race-bob@example.com",
                    NormalizedEmail = "RACE-BOB@EXAMPLE.COM",
                    DisplayName = "Race Bob",
                    CreatedAt = DateTime.UtcNow,
                });
            await setup.SaveChangesAsync();
        }

        async Task<PrivateMessageSendResult> SendAsync(string body, DateTimeOffset sentAt)
        {
            await using var context = CreateContext(shared);
            var repo = new EfPrivateMessageRepository(context);
            return await repo.SendNewOrExistingAsync(aliceId, bobId, body, sentAt);
        }

        var t1 = DateTimeOffset.Parse("2026-08-02T13:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-08-02T13:00:01Z");
        var results = await Task.WhenAll(
            SendAsync("Concurrent A", t1),
            SendAsync("Concurrent B", t2));

        Assert.All(results, r => Assert.True(r.Succeeded, r.ErrorMessage));
        Assert.Equal(results[0].ConversationId, results[1].ConversationId);

        await using var verify = CreateContext(shared);
        var conversations = await verify.PrivateConversations.CountAsync();
        var messageCount = await verify.PrivateMessages.CountAsync();
        var sortKeyCount = await verify.PrivateMessages.Select(m => m.SortKey).Distinct().CountAsync();
        Assert.Equal(1, conversations);
        Assert.Equal(2, messageCount);
        Assert.Equal(2, sortKeyCount);
    }

    [Fact]
    public async Task ConcurrentReplies_SerializeUnderWriteLock()
    {
        const string shared = "Data Source=file:pm-reply-race?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(shared);
        keepAlive.Open();

        Guid conversationId;
        await using (var setup = CreateContext(shared))
        {
            setup.Database.EnsureCreated();
            setup.MemberAccounts.AddRange(
                new MemberAccount
                {
                    Id = aliceId,
                    Email = "reply-race-alice@example.com",
                    NormalizedEmail = "REPLY-RACE-ALICE@EXAMPLE.COM",
                    DisplayName = "Reply Race Alice",
                    CreatedAt = DateTime.UtcNow,
                },
                new MemberAccount
                {
                    Id = bobId,
                    Email = "reply-race-bob@example.com",
                    NormalizedEmail = "REPLY-RACE-BOB@EXAMPLE.COM",
                    DisplayName = "Reply Race Bob",
                    CreatedAt = DateTime.UtcNow,
                });
            await setup.SaveChangesAsync();

            var seedRepo = new EfPrivateMessageRepository(setup);
            var created = await seedRepo.SendNewOrExistingAsync(
                aliceId,
                bobId,
                "Seed",
                DateTimeOffset.Parse("2026-08-02T18:00:00Z"));
            Assert.True(created.Succeeded, created.ErrorMessage);
            conversationId = created.ConversationId!.Value;
        }

        async Task<PrivateMessageSendResult> ReplyAsync(Guid senderId, string body, DateTimeOffset sentAt)
        {
            await using var context = CreateContext(shared);
            var repo = new EfPrivateMessageRepository(context);
            return await repo.ReplyAsync(conversationId, senderId, body, sentAt);
        }

        const int replyCount = 12;
        var replyTasks = Enumerable.Range(0, replyCount)
            .Select(i =>
            {
                var senderId = i % 2 == 0 ? aliceId : bobId;
                var sentAt = DateTimeOffset.Parse("2026-08-02T18:01:00Z").AddMilliseconds(i);
                return ReplyAsync(senderId, $"Reply {i}", sentAt);
            })
            .ToArray();

        var results = await Task.WhenAll(replyTasks);
        Assert.All(results, r => Assert.True(r.Succeeded, r.ErrorMessage));

        await using var verify = CreateContext(shared);
        var messages = await verify.PrivateMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SortKey)
            .ToListAsync();
        Assert.Equal(replyCount + 1, messages.Count);
        Assert.Equal(messages.Count, messages.Select(m => m.SortKey).Distinct().Count());
        for (var i = 1; i < messages.Count; i++)
        {
            Assert.True(messages[i].SortKey > messages[i - 1].SortKey);
        }

        var conversation = await verify.PrivateConversations
            .AsNoTracking()
            .SingleAsync(c => c.Id == conversationId);
        Assert.Equal(messages[^1].SortKey, conversation.LastMessageSortKey);
        Assert.Equal(messages[^1].Body, conversation.LastMessagePreview);
    }

    [Fact]
    public async Task Writes_RunInsideConfiguredRetryingExecutionStrategy()
    {
        await using var retryConnection = new SqliteConnection("Data Source=:memory:");
        await retryConnection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(retryConnection)
            .ReplaceService<IExecutionStrategyFactory, RetryingExecutionStrategyFactory>()
            .Options;
        await using var context = new QueenZoneDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = aliceId,
                Email = "retry-alice@example.com",
                NormalizedEmail = "RETRY-ALICE@EXAMPLE.COM",
                DisplayName = "Retry Alice",
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = bobId,
                Email = "retry-bob@example.com",
                NormalizedEmail = "RETRY-BOB@EXAMPLE.COM",
                DisplayName = "Retry Bob",
                CreatedAt = DateTime.UtcNow,
            });
        await context.SaveChangesAsync();

        var retryingRepository = new EfPrivateMessageRepository(context);
        var created = await retryingRepository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Created under retry strategy",
            DateTimeOffset.Parse("2026-08-02T17:00:00Z"));
        var replied = await retryingRepository.ReplyAsync(
            created.ConversationId!.Value,
            bobId,
            "Reply under retry strategy",
            DateTimeOffset.Parse("2026-08-02T17:01:00Z"));

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.True(replied.Succeeded, replied.ErrorMessage);
        Assert.Equal(2, await context.PrivateMessages.CountAsync());
    }

    [Fact]
    public async Task Writes_RetryTransientFault_WithoutDuplicatingMessages()
    {
        await using var retryConnection = new SqliteConnection("Data Source=:memory:");
        await retryConnection.OpenAsync();
        var failOnce = new FailOnceSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(retryConnection)
            .ReplaceService<IExecutionStrategyFactory, ForcedRetryExecutionStrategyFactory>()
            .AddInterceptors(failOnce)
            .Options;
        await using var context = new QueenZoneDbContext(options);
        failOnce.Armed = false;
        await context.Database.EnsureCreatedAsync();
        context.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = aliceId,
                Email = "forced-retry-alice@example.com",
                NormalizedEmail = "FORCED-RETRY-ALICE@EXAMPLE.COM",
                DisplayName = "Forced Retry Alice",
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = bobId,
                Email = "forced-retry-bob@example.com",
                NormalizedEmail = "FORCED-RETRY-BOB@EXAMPLE.COM",
                DisplayName = "Forced Retry Bob",
                CreatedAt = DateTime.UtcNow,
            });
        await context.SaveChangesAsync();

        var retryingRepository = new EfPrivateMessageRepository(context);
        failOnce.Armed = true;
        var created = await retryingRepository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Created with forced retry",
            DateTimeOffset.Parse("2026-08-02T19:00:00Z"));
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.True(failOnce.FailuresInjected >= 1);

        failOnce.ResetArm();
        var replied = await retryingRepository.ReplyAsync(
            created.ConversationId!.Value,
            bobId,
            "Reply with forced retry",
            DateTimeOffset.Parse("2026-08-02T19:01:00Z"));
        Assert.True(replied.Succeeded, replied.ErrorMessage);
        Assert.True(failOnce.FailuresInjected >= 2);

        Assert.Equal(2, await context.PrivateMessages.CountAsync());
        Assert.Equal(1, await context.PrivateConversations.CountAsync());
    }

    [Fact]
    public void IsUniqueConstraintViolation_DetectsSqliteUniqueErrors()
    {
        var sqlite = new Exception("UNIQUE constraint failed: PrivateConversations.MemberLowId, PrivateConversations.MemberHighId");
        var wrapped = new DbUpdateException("conflict", sqlite);
        Assert.True(EfPrivateMessageRepository.IsUniqueConstraintViolation(wrapped));
    }

    [Fact]
    public async Task CreateConversation_SetsSenderLastReadSortKeyToFirstMessage()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "First",
            DateTimeOffset.Parse("2026-08-02T17:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        var detail = await repository.GetConversationAsync(conversationId, aliceId);
        var firstSortKey = detail!.Messages[0].SortKey;

        var participant = await dbContext.PrivateConversationParticipants
            .AsNoTracking()
            .SingleAsync(p => p.ConversationId == conversationId && p.MemberId == aliceId);
        Assert.Equal(firstSortKey, participant.LastReadSortKey);
        Assert.NotNull(participant.LastReadAt);

        var conversation = await dbContext.PrivateConversations
            .AsNoTracking()
            .SingleAsync(c => c.Id == conversationId);
        Assert.Equal(firstSortKey, conversation.LastMessageSortKey);
    }

    [Fact]
    public async Task GetConversation_PagesMessages_DefaultingToLatestPage()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Msg 1",
            DateTimeOffset.Parse("2026-08-02T16:00:00Z"));
        var conversationId = created.ConversationId!.Value;
        for (var i = 2; i <= 5; i++)
        {
            await repository.ReplyAsync(
                conversationId,
                aliceId,
                $"Msg {i}",
                DateTimeOffset.Parse("2026-08-02T16:00:00Z").AddMinutes(i));
        }

        var latest = await repository.GetConversationAsync(conversationId, bobId, page: null, pageSize: 2);
        Assert.NotNull(latest);
        Assert.Equal(5, latest.TotalCount);
        Assert.Equal(3, latest.Page);
        Assert.Equal(3, latest.TotalPages);
        // Latest window is newest pageSize messages (not a short remainder page).
        Assert.Equal(["Msg 4", "Msg 5"], latest.Messages.Select(m => m.Body).ToArray());

        var first = await repository.GetConversationAsync(conversationId, bobId, page: 1, pageSize: 2);
        Assert.NotNull(first);
        Assert.Equal(1, first.Page);
        Assert.Equal(["Msg 1", "Msg 2"], first.Messages.Select(m => m.Body).ToArray());

        var middle = await repository.GetConversationAsync(conversationId, bobId, page: 2, pageSize: 2);
        Assert.NotNull(middle);
        Assert.Equal(["Msg 3", "Msg 4"], middle.Messages.Select(m => m.Body).ToArray());

        var explicitLast = await repository.GetConversationAsync(conversationId, bobId, page: 3, pageSize: 2);
        Assert.Equal(["Msg 4", "Msg 5"], explicitLast!.Messages.Select(m => m.Body).ToArray());
    }

    [Fact]
    public async Task MarkConversationRead_IsConditionalAcrossContexts()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "One",
            DateTimeOffset.Parse("2026-08-02T14:00:00Z"));
        var conversationId = created.ConversationId!.Value;
        await repository.ReplyAsync(
            conversationId,
            aliceId,
            "Two",
            DateTimeOffset.Parse("2026-08-02T14:01:00Z"));

        var detail = await repository.GetConversationAsync(conversationId, bobId);
        Assert.Equal(2, detail!.Messages.Count);
        var older = detail.Messages[0];
        var newer = detail.Messages[1];

        await repository.MarkConversationReadAsync(conversationId, bobId, newer.SortKey, newer.CreatedAt);
        await repository.MarkConversationReadAsync(conversationId, bobId, older.SortKey, older.CreatedAt);

        Assert.Equal(0, await repository.CountUnreadConversationsAsync(bobId));
    }

    [Fact]
    public async Task UnreadCount_UsesSortKeyAggregate_NotFullHistoryScanSemantics()
    {
        var created = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Seed",
            DateTimeOffset.Parse("2026-08-02T15:00:00Z"));
        var conversationId = created.ConversationId!.Value;
        for (var i = 0; i < 25; i++)
        {
            await repository.ReplyAsync(
                conversationId,
                aliceId,
                $"Msg {i}",
                DateTimeOffset.Parse("2026-08-02T15:00:00Z").AddSeconds(i + 1));
        }

        var detail = await repository.GetConversationAsync(conversationId, bobId);
        var midpoint = detail!.Messages[10];
        await repository.MarkConversationReadAsync(
            conversationId,
            bobId,
            midpoint.SortKey,
            midpoint.CreatedAt);

        var inbox = await repository.GetInboxAsync(bobId);
        var item = Assert.Single(inbox.Items);
        Assert.True(item.HasUnread);
        Assert.Equal(detail.Messages.Count(m => !m.IsMine && m.SortKey > midpoint.SortKey), item.UnreadCount);
    }

    [Fact]
    public async Task CountMessagesBySenderSinceAsync_CountsOnlyMessagesInWindow()
    {
        var since = DateTimeOffset.Parse("2026-08-06T10:00:00Z");
        var created = await repository.SendNewOrExistingAsync(aliceId, bobId, "Before window", since.AddMinutes(-5));
        await repository.ReplyAsync(created.ConversationId!.Value, aliceId, "Inside window", since.AddMinutes(1));
        await repository.ReplyAsync(created.ConversationId.Value, aliceId, "Also inside window", since.AddMinutes(2));

        Assert.Equal(2, await repository.CountMessagesBySenderSinceAsync(aliceId, since));
        Assert.Equal(0, await repository.CountMessagesBySenderSinceAsync(bobId, since));
    }

    [Fact]
    public async Task CountIdenticalMessagesBySenderSinceAsync_MatchesExactBodyOnly()
    {
        var since = DateTimeOffset.Parse("2026-08-06T10:00:00Z");
        var created = await repository.SendNewOrExistingAsync(aliceId, bobId, "Repeat me", since);
        await repository.ReplyAsync(created.ConversationId!.Value, aliceId, "Repeat me", since.AddMinutes(1));
        await repository.ReplyAsync(created.ConversationId.Value, aliceId, "Different", since.AddMinutes(2));

        Assert.Equal(2, await repository.CountIdenticalMessagesBySenderSinceAsync(aliceId, "Repeat me", since));
        Assert.Equal(1, await repository.CountIdenticalMessagesBySenderSinceAsync(aliceId, "Different", since));
    }

    [Fact]
    public async Task CountDistinctNewRecipientsSinceAsync_ExcludesRepliesInOlderConversations()
    {
        var since = DateTimeOffset.Parse("2026-08-06T10:00:00Z");

        // Existing conversation, started before the window: a reply here should not count.
        var oldConversation = await repository.SendNewOrExistingAsync(
            aliceId,
            bobId,
            "Old conversation",
            since.AddMinutes(-30));
        await repository.ReplyAsync(
            oldConversation.ConversationId!.Value,
            aliceId,
            "Reply in old thread",
            since.AddMinutes(1));

        // New conversation, started inside the window: this should count.
        await repository.SendNewOrExistingAsync(aliceId, carolId, "New conversation", since.AddMinutes(2));

        Assert.Equal(1, await repository.CountDistinctNewRecipientsSinceAsync(aliceId, since));
    }

    [Fact]
    public async Task CreateReport_SnapshotsPrecedingMessages_AndIsIdempotent()
    {
        var first = await repository.SendNewOrExistingAsync(aliceId, bobId, "First", DateTimeOffset.UtcNow);
        var conversationId = first.ConversationId!.Value;
        await repository.ReplyAsync(conversationId, aliceId, "Second", DateTimeOffset.UtcNow);
        await repository.ReplyAsync(conversationId, aliceId, "Third", DateTimeOffset.UtcNow);
        var target = (await repository.GetConversationAsync(conversationId, bobId))!.Messages[^1];

        var created = await moderation.CreateReportAsync(
            bobId,
            conversationId,
            target.Id,
            "Abuse",
            DateTimeOffset.UtcNow);
        Assert.True(created.Succeeded);
        var report = await moderation.GetReportAsync(created.ReportId!.Value);
        Assert.Equal("Third", report!.MessageBodySnapshot);
        Assert.Equal("Alice EF", report.SenderDisplayNameSnapshot);
        Assert.Equal(["First", "Second"], report.PrecedingMessages.Select(m => m.Body).ToArray());

        var again = await moderation.CreateReportAsync(
            bobId,
            conversationId,
            target.Id,
            "Ignored",
            DateTimeOffset.UtcNow);
        Assert.True(again.AlreadyReported);
        Assert.Equal(created.ReportId, again.ReportId);

        var outsider = await moderation.CreateReportAsync(
            carolId,
            conversationId,
            target.Id,
            "Nope",
            DateTimeOffset.UtcNow);
        Assert.False(outsider.Succeeded);
        Assert.Equal(PrivateMessageReportText.NotAParticipant, outsider.ErrorMessage);
    }

    [Fact]
    public async Task ListReportsAsync_FiltersByStatus_AndOrdersNewestFirst()
    {
        var first = await repository.SendNewOrExistingAsync(aliceId, bobId, "First", DateTimeOffset.UtcNow);
        var firstMessageId = (await repository.GetConversationAsync(first.ConversationId!.Value, bobId))!.Messages[^1].Id;
        var olderReport = await moderation.CreateReportAsync(
            bobId, first.ConversationId!.Value, firstMessageId, "Older", DateTimeOffset.UtcNow.AddMinutes(-5));

        var second = await repository.SendNewOrExistingAsync(aliceId, carolId, "Hi Carol", DateTimeOffset.UtcNow);
        var secondMessageId = (await repository.GetConversationAsync(second.ConversationId!.Value, carolId))!.Messages[^1].Id;
        var newerReport = await moderation.CreateReportAsync(
            carolId, second.ConversationId!.Value, secondMessageId, "Newer", DateTimeOffset.UtcNow);

        var openPage = await moderation.ListReportsAsync(PrivateMessageReportStatus.Open, 1, 50);
        Assert.Equal(2, openPage.TotalCount);
        Assert.Equal(newerReport.ReportId, openPage.Items[0].Id);
        Assert.Equal(olderReport.ReportId, openPage.Items[1].Id);
        Assert.Equal("Alice EF", openPage.Items[0].ReportedDisplayName);
        Assert.Equal("Carol EF", openPage.Items[0].ReporterDisplayName);

        await moderation.UpdateReportStatusAsync(
            olderReport.ReportId!.Value, PrivateMessageReportStatus.Dismissed, "mod@example.com");

        var dismissedPage = await moderation.ListReportsAsync(PrivateMessageReportStatus.Dismissed, 1, 50);
        Assert.Equal(olderReport.ReportId, Assert.Single(dismissedPage.Items).Id);

        var stillOpenPage = await moderation.ListReportsAsync(PrivateMessageReportStatus.Open, 1, 50);
        Assert.Equal(newerReport.ReportId, Assert.Single(stillOpenPage.Items).Id);

        var allPage = await moderation.ListReportsAsync("all", 1, 50);
        Assert.Equal(2, allPage.TotalCount);

        Assert.Equal(1, await moderation.CountOpenReportsAsync());
    }

    [Fact]
    public async Task UpdateReportStatusAsync_WritesAuditRowOnlyOnActualTransition()
    {
        var sent = await repository.SendNewOrExistingAsync(aliceId, bobId, "Hi", DateTimeOffset.UtcNow);
        var messageId = (await repository.GetConversationAsync(sent.ConversationId!.Value, bobId))!.Messages[^1].Id;
        var created = await moderation.CreateReportAsync(
            bobId, sent.ConversationId!.Value, messageId, "Reason", DateTimeOffset.UtcNow);
        var reportId = created.ReportId!.Value;

        var updated = await moderation.UpdateReportStatusAsync(
            reportId, PrivateMessageReportStatus.Reviewed, "mod@example.com");
        Assert.Equal(PrivateMessageReportStatus.Reviewed, updated!.Status);

        // Re-applying the same status should not add a second audit row.
        await moderation.UpdateReportStatusAsync(reportId, PrivateMessageReportStatus.Reviewed, "mod@example.com");

        var auditRows = await dbContext.PrivateMessageReportAuditLogs
            .Where(log => log.ReportId == reportId)
            .ToListAsync();
        var statusChangeRow = Assert.Single(
            auditRows,
            log => log.Action == PrivateMessageReportAuditAction.StatusChanged);
        Assert.Equal("mod@example.com", statusChangeRow.ActorEmail);
        Assert.Equal($"{PrivateMessageReportStatus.Open} -> {PrivateMessageReportStatus.Reviewed}", statusChangeRow.Details);

        var missing = await moderation.UpdateReportStatusAsync(
            Guid.NewGuid(), PrivateMessageReportStatus.Reviewed, "mod@example.com");
        Assert.Null(missing);
    }

    [Fact]
    public async Task AppendReportViewedAuditAsync_WritesOneRowPerCall()
    {
        var sent = await repository.SendNewOrExistingAsync(aliceId, bobId, "Hi", DateTimeOffset.UtcNow);
        var messageId = (await repository.GetConversationAsync(sent.ConversationId!.Value, bobId))!.Messages[^1].Id;
        var created = await moderation.CreateReportAsync(
            bobId, sent.ConversationId!.Value, messageId, "Reason", DateTimeOffset.UtcNow);
        var reportId = created.ReportId!.Value;

        await moderation.AppendReportViewedAuditAsync(reportId, "mod@example.com");
        await moderation.AppendReportViewedAuditAsync(reportId, "mod@example.com");

        var viewedRows = await dbContext.PrivateMessageReportAuditLogs
            .Where(log => log.ReportId == reportId && log.Action == PrivateMessageReportAuditAction.Viewed)
            .ToListAsync();
        Assert.Equal(2, viewedRows.Count);
        Assert.All(viewedRows, row => Assert.Equal("mod@example.com", row.ActorEmail));
    }

    [Fact]
    public async Task PurgeExpiredReportsAsync_DeletesOnlyTerminalReportsPastTheRetentionWindow()
    {
        var now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var retention = PrivateMessageLimits.ReportRetentionAfterTerminalStatus;

        var sentOpen = await repository.SendNewOrExistingAsync(aliceId, bobId, "Still open", now);
        var openReport = await moderation.CreateReportAsync(
            bobId, sentOpen.ConversationId!.Value,
            (await repository.GetConversationAsync(sentOpen.ConversationId!.Value, bobId))!.Messages[^1].Id,
            "Reason", now);

        var sentExpired = await repository.SendNewOrExistingAsync(aliceId, carolId, "Long dismissed", now);
        var expiredReport = await moderation.CreateReportAsync(
            carolId, sentExpired.ConversationId!.Value,
            (await repository.GetConversationAsync(sentExpired.ConversationId!.Value, carolId))!.Messages[^1].Id,
            "Reason", now);
        await moderation.UpdateReportStatusAsync(
            expiredReport.ReportId!.Value, PrivateMessageReportStatus.Dismissed, "mod@example.com");
        // Backdate the status-change audit row past the retention window (repository writes it at UtcNow).
        var expiredAuditRow = await dbContext.PrivateMessageReportAuditLogs
            .SingleAsync(log => log.ReportId == expiredReport.ReportId!.Value);
        expiredAuditRow.OccurredAt = now - retention - TimeSpan.FromDays(1);
        await dbContext.SaveChangesAsync();

        var sentRecent = await repository.SendNewOrExistingAsync(bobId, carolId, "Recently dismissed", now);
        var recentReport = await moderation.CreateReportAsync(
            carolId, sentRecent.ConversationId!.Value,
            (await repository.GetConversationAsync(sentRecent.ConversationId!.Value, carolId))!.Messages[^1].Id,
            "Reason", now);
        await moderation.UpdateReportStatusAsync(
            recentReport.ReportId!.Value, PrivateMessageReportStatus.Dismissed, "mod@example.com");

        var purgedCount = await moderation.PurgeExpiredReportsAsync(now);

        Assert.Equal(1, purgedCount);
        Assert.Null(await moderation.GetReportAsync(expiredReport.ReportId!.Value));
        Assert.NotNull(await moderation.GetReportAsync(openReport.ReportId!.Value));
        Assert.NotNull(await moderation.GetReportAsync(recentReport.ReportId!.Value));

        // Audit rows for the purged report are retained (ADR 0015 decision 3).
        var remainingAuditRows = await dbContext.PrivateMessageReportAuditLogs
            .Where(log => log.ReportId == expiredReport.ReportId!.Value)
            .CountAsync();
        Assert.True(remainingAuditRows > 0);
    }

    private static QueenZoneDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new QueenZoneDbContext(options);
    }

    private sealed class RetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryingExecutionStrategy(dependencies);
    }

    private sealed class RetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private sealed class ForcedRetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new ForcedRetryExecutionStrategy(dependencies);
    }

    private sealed class ForcedRetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is ForcedTransientException;
    }

    private sealed class ForcedTransientException() : Exception("Forced transient failure for retry coverage.");

    private sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int failuresInjected;

        public bool Armed { get; set; }

        public int FailuresInjected => failuresInjected;

        public void ResetArm()
        {
            Armed = true;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfArmed();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed();
            return ValueTask.FromResult(result);
        }

        private void ThrowIfArmed()
        {
            if (!Armed)
            {
                return;
            }

            Armed = false;
            Interlocked.Increment(ref failuresInjected);
            throw new ForcedTransientException();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
