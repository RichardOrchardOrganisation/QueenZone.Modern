using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PrivateMessageServiceTests
{
    [Fact]
    public async Task Compose_CreatesConversation_AndRecipientSeesUnread()
    {
        var (service, members, messages, alice, bob) = CreateSystem();

        var result = await service.ComposeAsync(alice.Id, bob.Id, "Hello Bob");
        Assert.True(result.Succeeded);
        Assert.NotNull(result.ConversationId);

        var bobInbox = await service.GetInboxAsync(bob.Id);
        var item = Assert.Single(bobInbox.Items);
        Assert.Equal(alice.Id, item.OtherParticipantId);
        Assert.True(item.HasUnread);
        Assert.Equal(1, item.UnreadCount);
        Assert.Equal("Hello Bob", item.LastMessagePreview);

        Assert.Equal(1, await service.CountUnreadConversationsAsync(bob.Id));
        Assert.Equal(0, await service.CountUnreadConversationsAsync(alice.Id));
    }

    [Fact]
    public async Task PendingDeletion_AnonymisesAndBlocksMessages_AndCancellationRestoresThem()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Retained message");
        var requestedAt = new DateTime(2026, 8, 12, 7, 0, 0, DateTimeKind.Utc);
        await members.RequestDeletionAsync(alice.Id, requestedAt);

        var detail = await service.GetConversationAsync(
            created.ConversationId!.Value,
            bob.Id,
            markRead: false);
        var reply = await service.ReplyAsync(created.ConversationId.Value, bob.Id, "Are you there?");
        var recipientMatches = await service.SearchRecipientsAsync(bob.Id, "Alice");

        Assert.NotNull(detail);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, detail.OtherParticipantDisplayName);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, Assert.Single(detail.Messages).SenderDisplayName);
        Assert.False(await service.CanSendReplyAsync(bob.Id, detail));
        Assert.False(reply.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, reply.ErrorMessage);
        Assert.Empty(recipientMatches);

        await members.CancelDeletionAsync(alice.Id, requestedAt.AddDays(2));
        detail = await service.GetConversationAsync(created.ConversationId.Value, bob.Id, markRead: false);
        reply = await service.ReplyAsync(created.ConversationId.Value, bob.Id, "Welcome back");
        recipientMatches = await service.SearchRecipientsAsync(bob.Id, "Alice");

        Assert.Equal("Alice", detail!.OtherParticipantDisplayName);
        Assert.Equal("Alice", detail.Messages[0].SenderDisplayName);
        Assert.True(await service.CanSendReplyAsync(bob.Id, detail));
        Assert.True(reply.Succeeded);
        Assert.Single(recipientMatches);
    }

    [Fact]
    public async Task Compose_RejectsEmptySelfAndMissingRecipient()
    {
        var (service, _, _, alice, bob) = CreateSystem();

        var empty = await service.ComposeAsync(alice.Id, bob.Id, "   ");
        Assert.False(empty.Succeeded);
        Assert.Equal(PrivateMessageService.BodyRequiredMessage, empty.ErrorMessage);

        var self = await service.ComposeAsync(alice.Id, alice.Id, "hi");
        Assert.False(self.Succeeded);
        Assert.Equal(PrivateMessageService.CannotMessageYourselfMessage, self.ErrorMessage);

        var missing = await service.ComposeAsync(alice.Id, Guid.NewGuid(), "hi");
        Assert.False(missing.Succeeded);
        Assert.Contains("not found", missing.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compose_RejectsSelfSendWithoutCallingTheRepository()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "alice-self@example.com",
            DisplayName = "Alice",
            CreatedAt = DateTime.UtcNow,
        });
        var messages = DispatchProxy.Create<IPrivateMessageRepository, RepositoryMustNotBeCalled>();
        var moderation = DispatchProxy.Create<IPrivateMessageModerationRepository, RepositoryMustNotBeCalled>();
        var service = new PrivateMessageService(
            messages,
            moderation,
            members,
            new InMemoryMemberFollowRepository(),
            new PrivateMessageRateLimiter(
                messages,
                TimeProvider.System,
                Options.Create(PermissiveRateLimitOptions()),
                NullLogger<PrivateMessageRateLimiter>.Instance),
            NoOpNotificationDispatcher.Instance,
            NullLogger<PrivateMessageService>.Instance,
            TimeProvider.System);

        var self = await service.ComposeAsync(alice.Id, alice.Id, "hi");

        Assert.False(self.Succeeded);
        Assert.Equal(PrivateMessageService.CannotMessageYourselfMessage, self.ErrorMessage);
    }

    [Fact]
    public async Task Compose_And_Reply_StoreMarkupAsPlainText_AndRejectOverLength()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        const string markup = "<script>alert(1)</script>";

        var created = await service.ComposeAsync(alice.Id, bob.Id, markup);
        Assert.True(created.Succeeded);
        var conversationId = created.ConversationId!.Value;
        Assert.True((await service.ReplyAsync(conversationId, bob.Id, markup + " reply")).Succeeded);

        var tooLong = await service.ComposeAsync(
            alice.Id,
            bob.Id,
            new string('x', PrivateMessageLimits.MaxBodyLength + 1));
        Assert.False(tooLong.Succeeded);
        Assert.Equal(PrivateMessageService.BodyTooLongMessage, tooLong.ErrorMessage);

        var emptyReply = await service.ReplyAsync(conversationId, bob.Id, "  ");
        Assert.Equal(PrivateMessageService.BodyRequiredMessage, emptyReply.ErrorMessage);

        var detail = await service.GetConversationAsync(conversationId, bob.Id, markRead: false);
        Assert.Equal(markup, detail!.Messages[0].Body);
        Assert.Equal(markup + " reply", detail.Messages[1].Body);
        Assert.Equal(markup + " reply", (await service.GetInboxAsync(alice.Id)).Items[0].LastMessagePreview);
    }

    [Fact]
    public async Task Compose_And_Reply_RejectSuspendedSender()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        await members.SuspendAsync(alice.Id, "Spam reports", "admin@example.com", DateTime.UtcNow);

        var compose = await service.ComposeAsync(alice.Id, bob.Id, "Hi");
        Assert.False(compose.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, compose.ErrorMessage);

        var created = await service.ComposeAsync(bob.Id, alice.Id, "Hi Alice");
        var reply = await service.ReplyAsync(created.ConversationId!.Value, alice.Id, "Reply");
        Assert.False(reply.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, reply.ErrorMessage);
    }

    [Fact]
    public async Task Compose_IsRateLimited_WhenMessageVolumeExceedsWindow()
    {
        var tightOptions = new PrivateMessageRateLimitOptions
        {
            MaxMessagesPerWindow = 2,
            NewAccountMaxMessagesPerWindow = 2,
        };
        var (service, _, _, alice, bob) = CreateSystem(tightOptions);

        Assert.True((await service.ComposeAsync(alice.Id, bob.Id, "Msg 1")).Succeeded);
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Msg 2");
        Assert.True(created.Succeeded);

        var third = await service.ReplyAsync(created.ConversationId!.Value, alice.Id, "Msg 3");
        Assert.False(third.Succeeded);
        Assert.Equal(PrivateMessageService.RateLimitedMessage, third.ErrorMessage);
    }

    [Fact]
    public async Task Compose_IsRateLimited_WhenTooManyNewRecipients()
    {
        var tightOptions = new PrivateMessageRateLimitOptions
        {
            MaxNewRecipientsPerWindow = 1,
            NewAccountMaxNewRecipientsPerWindow = 1,
        };
        var (service, members, _, alice, bob) = CreateSystem(tightOptions);
        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "carol-fanout@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTime.UtcNow,
        });

        Assert.True((await service.ComposeAsync(alice.Id, bob.Id, "Hi Bob")).Succeeded);
        var second = await service.ComposeAsync(alice.Id, carol.Id, "Hi Carol");

        Assert.False(second.Succeeded);
        Assert.Equal(PrivateMessageService.RateLimitedMessage, second.ErrorMessage);
    }

    [Fact]
    public async Task Reply_IsNotRateLimited_ByNewRecipientFanOut()
    {
        var tightOptions = new PrivateMessageRateLimitOptions
        {
            MaxNewRecipientsPerWindow = 1,
            NewAccountMaxNewRecipientsPerWindow = 1,
        };
        var (service, _, _, alice, bob) = CreateSystem(tightOptions);
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hi Bob");

        // The fan-out limit already used its one allowance on the compose above; replying in the
        // same (already-existing) conversation must not be blocked by the recipient fan-out check.
        var reply = await service.ReplyAsync(created.ConversationId!.Value, alice.Id, "Following up");

        Assert.True(reply.Succeeded);
    }

    [Fact]
    public async Task Compose_IsRateLimited_WhenSameMessageRepeatedTooOften()
    {
        var tightOptions = new PrivateMessageRateLimitOptions { MaxDuplicateMessagesPerWindow = 2 };
        var (service, _, _, alice, bob) = CreateSystem(tightOptions);
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Buy my product");
        Assert.True(created.Succeeded);

        var secondIdentical = await service.ReplyAsync(created.ConversationId!.Value, alice.Id, "Buy my product");
        Assert.True(secondIdentical.Succeeded);

        var thirdIdentical = await service.ReplyAsync(created.ConversationId.Value, alice.Id, "Buy my product");
        Assert.False(thirdIdentical.Succeeded);
        Assert.Equal(PrivateMessageService.RateLimitedMessage, thirdIdentical.ErrorMessage);
    }

    [Fact]
    public async Task Compose_ReusesExistingOneToOneConversation()
    {
        var (service, _, _, alice, bob) = CreateSystem();

        var first = await service.ComposeAsync(alice.Id, bob.Id, "First");
        var second = await service.ComposeAsync(alice.Id, bob.Id, "Second");

        Assert.Equal(first.ConversationId, second.ConversationId);

        var detail = await service.GetConversationAsync(first.ConversationId!.Value, alice.Id, markRead: false);
        Assert.Equal(2, detail!.Messages.Count);
        Assert.Equal("Second", detail.Messages[^1].Body);
    }

    [Fact]
    public async Task Reply_UpdatesUnread_AndOrdersOldestFirst()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        await service.GetConversationAsync(conversationId, bob.Id, markRead: true);
        Assert.Equal(0, await service.CountUnreadConversationsAsync(bob.Id));

        var reply = await service.ReplyAsync(conversationId, bob.Id, "Hi Alice");
        Assert.True(reply.Succeeded);

        Assert.Equal(1, await service.CountUnreadConversationsAsync(alice.Id));
        Assert.Equal(0, await service.CountUnreadConversationsAsync(bob.Id));

        var aliceView = await service.GetConversationAsync(conversationId, alice.Id, markRead: true);
        Assert.Equal(["Hello", "Hi Alice"], aliceView!.Messages.Select(m => m.Body).ToArray());
        Assert.True(aliceView.Messages[0].SortKey < aliceView.Messages[1].SortKey);
        Assert.Equal(0, await service.CountUnreadConversationsAsync(alice.Id));
    }

    [Fact]
    public async Task GetConversation_PagesMessages_AndMarkReadUsesReturnedPage()
    {
        var (service, _, messages, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Msg 1");
        var conversationId = created.ConversationId!.Value;
        for (var i = 2; i <= 5; i++)
        {
            Assert.True((await service.ReplyAsync(conversationId, alice.Id, $"Msg {i}")).Succeeded);
        }

        var latest = await service.GetConversationAsync(
            conversationId,
            bob.Id,
            markRead: false,
            page: null,
            pageSize: 2);
        Assert.Equal(3, latest!.Page);
        Assert.Equal(["Msg 4", "Msg 5"], latest.Messages.Select(m => m.Body).ToArray());

        // Opening an older page only advances the read cursor as far as that page.
        await service.GetConversationAsync(
            conversationId,
            bob.Id,
            markRead: true,
            page: 1,
            pageSize: 2);
        var afterOlder = Assert.Single((await service.GetInboxAsync(bob.Id)).Items);
        Assert.True(afterOlder.HasUnread);
        Assert.Equal(3, afterOlder.UnreadCount);

        // Latest page marks through the newest returned message.
        await service.GetConversationAsync(
            conversationId,
            bob.Id,
            markRead: true,
            page: null,
            pageSize: 2);
        Assert.Equal(0, await service.CountUnreadConversationsAsync(bob.Id));
        Assert.Equal(5, (await messages.GetConversationAsync(conversationId, bob.Id))!.TotalCount);
    }

    [Fact]
    public async Task ReplyingFromOlderPage_DoesNotClearMessagesThatWereNotDisplayed()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Msg 1");
        var conversationId = created.ConversationId!.Value;
        for (var i = 2; i <= 5; i++)
        {
            Assert.True((await service.ReplyAsync(conversationId, alice.Id, $"Msg {i}")).Succeeded);
        }

        await service.GetConversationAsync(
            conversationId,
            bob.Id,
            markRead: true,
            page: 1,
            pageSize: 2);

        Assert.True((await service.ReplyAsync(conversationId, bob.Id, "Reply from page one")).Succeeded);

        var inboxItem = Assert.Single((await service.GetInboxAsync(bob.Id)).Items);
        Assert.True(inboxItem.HasUnread);
        Assert.Equal(3, inboxItem.UnreadCount);
    }

    [Fact]
    public async Task ComposeToExistingConversation_DoesNotClearUnreadMessages()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        await service.ComposeAsync(alice.Id, bob.Id, "Unread from Alice");

        Assert.True((await service.ComposeAsync(bob.Id, alice.Id, "Bob composes without opening")).Succeeded);

        var inboxItem = Assert.Single((await service.GetInboxAsync(bob.Id)).Items);
        Assert.True(inboxItem.HasUnread);
        Assert.Equal(1, inboxItem.UnreadCount);
    }

    [Fact]
    public async Task GetInbox_PagesConversations()
    {
        var (service, members, _, alice, _) = CreateSystem();
        for (var i = 1; i <= 5; i++)
        {
            var peer = await members.CreateAsync(new MemberAccount
            {
                Id = Guid.NewGuid(),
                Email = $"peer{i}@example.com",
                DisplayName = $"Peer {i}",
                CreatedAt = DateTime.UtcNow,
            });
            Assert.True((await service.ComposeAsync(alice.Id, peer.Id, $"Hello {i}")).Succeeded);
        }

        var page1 = await service.GetInboxAsync(alice.Id, page: 1, pageSize: 2);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);

        var page3 = await service.GetInboxAsync(alice.Id, page: 3, pageSize: 2);
        Assert.Equal(3, page3.Page);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task Reply_RejectsNonParticipant()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "carol@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTime.UtcNow,
        });

        var created = await service.ComposeAsync(alice.Id, bob.Id, "Private");
        var result = await service.ReplyAsync(created.ConversationId!.Value, carol.Id, "Intruder");

        Assert.False(result.Succeeded);
        Assert.Contains("not a participant", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetConversation_ReturnsNullForNonParticipant()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "carol2@example.com",
            DisplayName = "Carol Two",
            CreatedAt = DateTime.UtcNow,
        });

        var created = await service.ComposeAsync(alice.Id, bob.Id, "Secret");
        var detail = await service.GetConversationAsync(created.ConversationId!.Value, carol.Id);
        Assert.Null(detail);
    }

    [Fact]
    public async Task OwnSentMessages_DoNotCreateUnreadForSender()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        await service.ComposeAsync(alice.Id, bob.Id, "Ping");

        var aliceInbox = await service.GetInboxAsync(alice.Id);
        Assert.False(Assert.Single(aliceInbox.Items).HasUnread);
        Assert.Equal(0, await service.CountUnreadConversationsAsync(alice.Id));
    }

    [Fact]
    public async Task SearchRecipients_ExcludesSelf_AndMatchesDisplayName()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "bobby@example.com",
            DisplayName = "Bobby",
            CreatedAt = DateTime.UtcNow,
        });

        var matches = await service.SearchRecipientsAsync(alice.Id, "Bob");
        Assert.Contains(matches, m => m.MemberId == bob.Id);
        Assert.DoesNotContain(matches, m => m.MemberId == alice.Id);
        Assert.Contains(matches, m => m.DisplayName == "Bobby");
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("11111111-1111-1111-1111-111111111111", null, false)]
    [InlineData("11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", true)]
    public void CanMessage_EnforcesSelfAndPresence(string? current, string? target, bool expected)
    {
        Guid? currentId = current is null ? null : Guid.Parse(current);
        Guid? targetId = target is null ? null : Guid.Parse(target);
        Assert.Equal(expected, PrivateMessageService.CanMessage(currentId, targetId));
    }


    [Fact]
    public async Task GetConversation_MarkRead_UsesReturnedSortKeyCursor()
    {
        var (service, _, messages, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Visible");
        var conversationId = created.ConversationId!.Value;

        var detail = await messages.GetConversationAsync(conversationId, bob.Id);
        Assert.NotNull(detail);
        var lastReturned = detail!.Messages[^1];

        // Later SortKey with an earlier CreatedAt (delayed commit / clock skew) must stay unread.
        await messages.ReplyAsync(
            conversationId,
            alice.Id,
            "Delayed commit / earlier clock",
            lastReturned.CreatedAt.AddMinutes(-5));

        await messages.MarkConversationReadAsync(
            conversationId,
            bob.Id,
            lastReturned.SortKey,
            lastReturned.CreatedAt);

        Assert.Equal(1, await service.CountUnreadConversationsAsync(bob.Id));
    }

    [Fact]
    public async Task GetConversation_MarkRead_KeepsEqualTimestampLaterSortKeyUnread()
    {
        var (service, _, messages, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        await service.GetConversationAsync(conversationId, bob.Id, markRead: true);

        var detail = await messages.GetConversationAsync(conversationId, bob.Id);
        var lastReturned = detail!.Messages[^1];
        await messages.ReplyAsync(
            conversationId,
            alice.Id,
            "Same timestamp later sort key",
            lastReturned.CreatedAt);

        Assert.Equal(1, await service.CountUnreadConversationsAsync(bob.Id));
    }

    [Fact]
    public async Task ArchiveConversation_RemovesFromInbox_AndListsInArchived()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "carol-archive@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTime.UtcNow,
        });

        var created = await service.ComposeAsync(alice.Id, bob.Id, "Archive this");
        var conversationId = created.ConversationId!.Value;

        Assert.False(await service.ArchiveConversationAsync(conversationId, carol.Id));

        Assert.True(await service.ArchiveConversationAsync(conversationId, alice.Id));
        Assert.Empty((await service.GetInboxAsync(alice.Id)).Items);
        Assert.Single((await service.GetInboxAsync(bob.Id)).Items);
        Assert.Single((await service.GetArchivedInboxAsync(alice.Id)).Items);
    }

    [Fact]
    public async Task ArchiveConversation_ReopensOnNewMessage_AndCanBeUnarchivedManually()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        Assert.True(await service.ArchiveConversationAsync(conversationId, alice.Id));
        Assert.True((await service.ReplyAsync(conversationId, bob.Id, "Still here")).Succeeded);

        Assert.Single((await service.GetInboxAsync(alice.Id)).Items);
        Assert.Empty((await service.GetArchivedInboxAsync(alice.Id)).Items);

        Assert.True(await service.ArchiveConversationAsync(conversationId, alice.Id));
        Assert.True(await service.UnarchiveConversationAsync(conversationId, alice.Id));
        Assert.Single((await service.GetInboxAsync(alice.Id)).Items);
    }

    [Fact]
    public async Task RemoveConversation_RemovesFromInbox_ButNotOtherParticipant()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "carol-remove@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTime.UtcNow,
        });

        var created = await service.ComposeAsync(alice.Id, bob.Id, "Remove this");
        var conversationId = created.ConversationId!.Value;

        Assert.False(await service.RemoveConversationAsync(conversationId, carol.Id));

        Assert.True(await service.RemoveConversationAsync(conversationId, alice.Id));
        Assert.Empty((await service.GetInboxAsync(alice.Id)).Items);
        Assert.Single((await service.GetInboxAsync(bob.Id)).Items);
    }

    [Fact]
    public async Task RemoveConversation_RestoredOnNewMessage()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        Assert.True(await service.RemoveConversationAsync(conversationId, alice.Id));
        Assert.True((await service.ReplyAsync(conversationId, bob.Id, "Still here")).Succeeded);

        Assert.Single((await service.GetInboxAsync(alice.Id)).Items);
    }

    [Fact]
    public async Task Block_RejectsSelfAndMissingMember()
    {
        var (service, _, _, alice, _) = CreateSystem();

        var self = await service.BlockAsync(alice.Id, alice.Id);
        Assert.False(self.Succeeded);
        Assert.Contains("yourself", self.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var missing = await service.BlockAsync(alice.Id, Guid.NewGuid());
        Assert.False(missing.Succeeded);
        Assert.Contains("not found", missing.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Block_StopsBlockedUserFromComposingAndReplying_WithGenericError()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        Assert.True((await service.BlockAsync(alice.Id, bob.Id)).Succeeded);
        Assert.True(await service.HasBlockedAsync(alice.Id, bob.Id));
        Assert.False(await service.CanMessageAsync(bob.Id, alice.Id));
        Assert.False(await service.CanMessageAsync(alice.Id, bob.Id));

        var bobCompose = await service.ComposeAsync(bob.Id, alice.Id, "Blocked compose");
        Assert.False(bobCompose.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, bobCompose.ErrorMessage);

        var bobReply = await service.ReplyAsync(conversationId, bob.Id, "Blocked reply");
        Assert.False(bobReply.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, bobReply.ErrorMessage);

        var aliceDetail = await service.GetConversationAsync(conversationId, alice.Id, markRead: false);
        var bobDetail = await service.GetConversationAsync(conversationId, bob.Id, markRead: false);
        Assert.False(await service.CanSendReplyAsync(alice.Id, aliceDetail!));
        Assert.False(await service.CanSendReplyAsync(bob.Id, bobDetail!));

        // Existing conversation remains visible for both participants.
        Assert.Single((await service.GetInboxAsync(alice.Id)).Items);
        Assert.Single((await service.GetInboxAsync(bob.Id)).Items);
    }

    [Fact]
    public async Task Block_UnfollowsTheBlockedMember()
    {
        var (service, _, _, follows, alice, bob) = CreateSystemWithFollows();
        await follows.FollowAsync(alice.Id, bob.Id, DateTimeOffset.UtcNow);
        await follows.FollowAsync(bob.Id, alice.Id, DateTimeOffset.UtcNow);
        Assert.True(await follows.IsFollowingAsync(alice.Id, bob.Id));

        Assert.True((await service.BlockAsync(alice.Id, bob.Id)).Succeeded);

        Assert.False(await follows.IsFollowingAsync(alice.Id, bob.Id));
        Assert.True(await follows.IsFollowingAsync(bob.Id, alice.Id));
    }

    [Fact]
    public async Task Unblock_RestoresMessaging()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        Assert.True((await service.ComposeAsync(alice.Id, bob.Id, "Hello")).Succeeded);
        Assert.True((await service.BlockAsync(alice.Id, bob.Id)).Succeeded);

        Assert.True(await service.UnblockAsync(alice.Id, bob.Id));
        Assert.False(await service.HasBlockedAsync(alice.Id, bob.Id));
        Assert.True(await service.CanMessageAsync(bob.Id, alice.Id));

        var restored = await service.ComposeAsync(bob.Id, alice.Id, "Back again");
        Assert.True(restored.Succeeded);
    }

    [Fact]
    public async Task NobodyPrivacy_BlocksNewConversation_ButAllowsExistingThread()
    {
        var (service, members, _, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        Assert.True(created.Succeeded);

        await members.UpdateMessagePrivacyAsync(bob.Id, MemberMessagePrivacy.Nobody);

        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "carol-privacy@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTime.UtcNow,
        });
        var carolCompose = await service.ComposeAsync(carol.Id, bob.Id, "New contact");
        Assert.False(carolCompose.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, carolCompose.ErrorMessage);
        Assert.False(await service.CanMessageAsync(carol.Id, bob.Id));

        var continueThread = await service.ComposeAsync(alice.Id, bob.Id, "Still here");
        Assert.True(continueThread.Succeeded);
        Assert.Equal(created.ConversationId, continueThread.ConversationId);
        Assert.True(await service.CanMessageAsync(alice.Id, bob.Id));

        var reply = await service.ReplyAsync(created.ConversationId!.Value, alice.Id, "Reply still works");
        Assert.True(reply.Succeeded);
    }

    [Fact]
    public async Task FollowedPrivacy_RequiresRecipientFollowsSender()
    {
        var (service, members, _, follows, alice, bob) = CreateSystemWithFollows();
        await members.UpdateMessagePrivacyAsync(bob.Id, MemberMessagePrivacy.Followed);

        var denied = await service.ComposeAsync(alice.Id, bob.Id, "Not followed yet");
        Assert.False(denied.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, denied.ErrorMessage);
        Assert.False(await service.CanMessageAsync(alice.Id, bob.Id));

        await follows.FollowAsync(bob.Id, alice.Id, DateTimeOffset.UtcNow);
        Assert.True(await service.CanMessageAsync(alice.Id, bob.Id));

        var allowed = await service.ComposeAsync(alice.Id, bob.Id, "Now followed");
        Assert.True(allowed.Succeeded);

        await follows.UnfollowAsync(bob.Id, alice.Id);
        var continueExisting = await service.ComposeAsync(alice.Id, bob.Id, "Existing thread continues");
        Assert.True(continueExisting.Succeeded);
        Assert.True(await service.CanMessageAsync(alice.Id, bob.Id));
    }

    [Fact]
    public async Task Block_WinsOverFollowedPrivacy()
    {
        var (service, members, _, follows, alice, bob) = CreateSystemWithFollows();
        await members.UpdateMessagePrivacyAsync(bob.Id, MemberMessagePrivacy.Followed);
        await follows.FollowAsync(bob.Id, alice.Id, DateTimeOffset.UtcNow);
        Assert.True((await service.BlockAsync(bob.Id, alice.Id)).Succeeded);

        var result = await service.ComposeAsync(alice.Id, bob.Id, "Blocked");
        Assert.False(result.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, result.ErrorMessage);
        Assert.False(await service.CanMessageAsync(alice.Id, bob.Id));
    }

    [Fact]
    public async Task ReportMessage_SnapshotsContext_AndDoesNotNotifyReportedMember()
    {
        var (service, _, messages, alice, bob) = CreateSystem();
        var first = await service.ComposeAsync(alice.Id, bob.Id, "Earlier context");
        var conversationId = first.ConversationId!.Value;
        Assert.True((await service.ReplyAsync(conversationId, alice.Id, "Abusive follow-up")).Succeeded);

        var unreadBefore = await service.CountUnreadConversationsAsync(alice.Id);
        var detailBefore = await service.GetConversationAsync(conversationId, bob.Id, markRead: false);
        var reported = detailBefore!.Messages[^1];

        var result = await service.ReportMessageAsync(bob.Id, conversationId, reported.Id, "Harassment");
        Assert.True(result.Succeeded);
        Assert.False(result.AlreadyReported);
        Assert.NotNull(result.ReportId);

        var report = await messages.GetReportAsync(result.ReportId!.Value);
        Assert.NotNull(report);
        Assert.Equal(PrivateMessageReportStatus.Open, report!.Status);
        Assert.Equal(bob.Id, report.ReporterMemberId);
        Assert.Equal(alice.Id, report.ReportedMemberId);
        Assert.Equal("Harassment", report.Reason);
        Assert.Equal("Abusive follow-up", report.MessageBodySnapshot);
        Assert.Equal("Alice", report.SenderDisplayNameSnapshot);
        var preceding = Assert.Single(report.PrecedingMessages);
        Assert.Equal("Earlier context", preceding.Body);

        var detailAfter = await service.GetConversationAsync(conversationId, bob.Id, markRead: false);
        Assert.Equal(detailBefore.Messages.Count, detailAfter!.Messages.Count);
        Assert.True(detailAfter.Messages[^1].ReportedByViewer);
        Assert.Equal(unreadBefore, await service.CountUnreadConversationsAsync(alice.Id));

        var again = await service.ReportMessageAsync(bob.Id, conversationId, reported.Id, "Second reason");
        Assert.True(again.Succeeded);
        Assert.True(again.AlreadyReported);
        Assert.Equal(result.ReportId, again.ReportId);
        Assert.Equal("Harassment", (await messages.GetReportAsync(again.ReportId!.Value))!.Reason);
    }

    [Fact]
    public async Task ReportMessage_RejectsNonParticipant_OwnMessage_AndOversizedReason()
    {
        var (service, members, messages, alice, bob) = CreateSystem();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Please report me");
        var conversationId = created.ConversationId!.Value;
        var message = Assert.Single(
            (await service.GetConversationAsync(conversationId, bob.Id, markRead: false))!.Messages);

        var own = await service.ReportMessageAsync(alice.Id, conversationId, message.Id, null);
        Assert.False(own.Succeeded);
        Assert.Equal(PrivateMessageReportText.CannotReportOwn, own.ErrorMessage);

        var carol = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Email = "carol@example.com",
            DisplayName = "Carol",
            CreatedAt = DateTime.UtcNow,
        });
        var outsider = await service.ReportMessageAsync(carol.Id, conversationId, message.Id, "Nope");
        Assert.False(outsider.Succeeded);
        Assert.Equal(PrivateMessageReportText.NotAParticipant, outsider.ErrorMessage);

        var tooLong = await service.ReportMessageAsync(
            bob.Id,
            conversationId,
            message.Id,
            new string('x', PrivateMessageLimits.MaxReportReasonLength + 1));
        Assert.False(tooLong.Succeeded);
        Assert.Equal(PrivateMessageReportText.ReasonTooLong, tooLong.ErrorMessage);

        var omitted = await service.ReportMessageAsync(bob.Id, conversationId, message.Id, "   ");
        Assert.True(omitted.Succeeded);
        Assert.Null((await messages.GetReportAsync(omitted.ReportId!.Value))!.Reason);
    }

    private static (
        PrivateMessageService Service,
        IMemberAccountRepository Members,
        InMemoryPrivateMessageRepository Messages,
        MemberAccount Alice,
        MemberAccount Bob) CreateSystem(PrivateMessageRateLimitOptions? rateLimitOptions = null)
    {
        var created = CreateSystemWithFollows(rateLimitOptions);
        return (created.Service, created.Members, created.Messages, created.Alice, created.Bob);
    }

    private static (
        PrivateMessageService Service,
        IMemberAccountRepository Members,
        InMemoryPrivateMessageRepository Messages,
        IMemberFollowRepository Follows,
        MemberAccount Alice,
        MemberAccount Bob) CreateSystemWithFollows(PrivateMessageRateLimitOptions? rateLimitOptions = null)
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = members.CreateAsync(new MemberAccount
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "alice@example.com",
            DisplayName = "Alice",
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();
        var bob = members.CreateAsync(new MemberAccount
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Email = "bob@example.com",
            DisplayName = "Bob",
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();

        var messages = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());
        var follows = new InMemoryMemberFollowRepository();
        var rateLimiter = new PrivateMessageRateLimiter(
            messages,
            TimeProvider.System,
            Options.Create(rateLimitOptions ?? PermissiveRateLimitOptions()),
            NullLogger<PrivateMessageRateLimiter>.Instance);
        var service = new PrivateMessageService(
            messages,
            messages,
            members,
            follows,
            rateLimiter,
            NoOpNotificationDispatcher.Instance,
            NullLogger<PrivateMessageService>.Instance,
            TimeProvider.System);
        return (service, members, messages, follows, alice, bob);
    }

    /// <summary>
    /// Effectively unlimited thresholds so tests unrelated to rate limiting are unaffected,
    /// regardless of whether the test member accounts are treated as "new".
    /// </summary>
    private static PrivateMessageRateLimitOptions PermissiveRateLimitOptions() => new()
    {
        WindowMinutes = 10,
        MaxMessagesPerWindow = 1000,
        MaxNewRecipientsPerWindow = 1000,
        MaxDuplicateMessagesPerWindow = 1000,
        NewAccountAgeDays = 3,
        NewAccountMaxMessagesPerWindow = 1000,
        NewAccountMaxNewRecipientsPerWindow = 1000,
    };

    public class RepositoryMustNotBeCalled : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(targetMethod?.Name);
    }
}
