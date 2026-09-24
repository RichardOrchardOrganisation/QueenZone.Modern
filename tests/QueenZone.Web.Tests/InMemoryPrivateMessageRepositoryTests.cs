using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class InMemoryPrivateMessageRepositoryTests
{
    [Fact]
    public async Task Inbox_IsOrderedByMostRecentSortKey_AndIsolatedPerParticipant()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b@example.com", "Bob"));
        var carol = await members.CreateAsync(NewMember("c@example.com", "Carol"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var older = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
        var newer = DateTimeOffset.Parse("2026-08-01T12:00:00Z");

        await repo.SendNewOrExistingAsync(alice.Id, bob.Id, "To Bob", older);
        await repo.SendNewOrExistingAsync(alice.Id, carol.Id, "To Carol", newer);

        var aliceInbox = await repo.GetInboxAsync(alice.Id);
        Assert.Equal(["Carol", "Bob"], aliceInbox.Items.Select(i => i.OtherParticipantDisplayName).ToArray());

        var bobInbox = await repo.GetInboxAsync(bob.Id);
        Assert.Equal(["Alice"], bobInbox.Items.Select(i => i.OtherParticipantDisplayName).ToArray());
        Assert.DoesNotContain(bobInbox.Items, i => i.OtherParticipantId == carol.Id);
    }

    [Fact]
    public async Task Inbox_OrdersByLastMessageSortKey_EvenWhenTimestampsSkew()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-skew@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-skew@example.com", "Bob"));
        var carol = await members.CreateAsync(NewMember("c-skew@example.com", "Carol"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        await repo.SendNewOrExistingAsync(
            alice.Id,
            carol.Id,
            "Carol first insert",
            DateTimeOffset.Parse("2026-08-01T20:00:00Z"));
        await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Bob later insert, earlier clock",
            DateTimeOffset.Parse("2026-08-01T08:00:00Z"));

        var aliceInbox = await repo.GetInboxAsync(alice.Id);
        Assert.Equal(["Bob", "Carol"], aliceInbox.Items.Select(i => i.OtherParticipantDisplayName).ToArray());
        Assert.True(aliceInbox.Items[0].LastMessageAt < aliceInbox.Items[1].LastMessageAt);
    }

    [Fact]
    public async Task Preview_IsPlainTextTruncated()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a2@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b2@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var body = new string('x', PrivateMessageLimits.PreviewLength + 40);
        await repo.SendNewOrExistingAsync(alice.Id, bob.Id, body, DateTimeOffset.UtcNow);

        var item = Assert.Single((await repo.GetInboxAsync(bob.Id)).Items);
        Assert.Equal(PrivateMessageLimits.PreviewLength, item.LastMessagePreview.Length);
        Assert.DoesNotContain('<', item.LastMessagePreview);
    }

    [Fact]
    public async Task ComposeAndReply_StoreMarkupAsPlainText()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-xss@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-xss@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());
        const string markup = "<script>alert(1)</script>";

        var created = await repo.SendNewOrExistingAsync(alice.Id, bob.Id, markup, DateTimeOffset.UtcNow);
        Assert.True(created.Succeeded);
        var conversationId = created.ConversationId!.Value;
        var reply = await repo.ReplyAsync(conversationId, bob.Id, markup + " reply", DateTimeOffset.UtcNow);
        Assert.True(reply.Succeeded);

        var detail = await repo.GetConversationAsync(conversationId, bob.Id);
        Assert.Equal(markup, detail!.Messages[0].Body);
        Assert.Equal(markup + " reply", detail.Messages[1].Body);
        Assert.Equal(markup + " reply", (await repo.GetInboxAsync(alice.Id)).Items[0].LastMessagePreview);
    }

    [Fact]
    public async Task GetConversation_PagesMessages_DefaultingToLatestPage()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-page@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-page@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Msg 1",
            DateTimeOffset.Parse("2026-08-01T11:00:00Z"));
        var conversationId = created.ConversationId!.Value;
        for (var i = 2; i <= 5; i++)
        {
            await repo.ReplyAsync(
                conversationId,
                alice.Id,
                $"Msg {i}",
                DateTimeOffset.Parse("2026-08-01T11:00:00Z").AddMinutes(i));
        }

        var latest = await repo.GetConversationAsync(conversationId, bob.Id, page: null, pageSize: 2);
        Assert.Equal(3, latest!.Page);
        Assert.Equal(["Msg 4", "Msg 5"], latest.Messages.Select(m => m.Body).ToArray());

        var first = await repo.GetConversationAsync(conversationId, bob.Id, page: 1, pageSize: 2);
        Assert.Equal(["Msg 1", "Msg 2"], first!.Messages.Select(m => m.Body).ToArray());
    }

    [Fact]
    public async Task Reply_UpdatesPreviewAndSortKeyTip_KeepsMonotonicLastMessageAt()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a3@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b3@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Start",
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repo.ReplyAsync(
            conversationId,
            bob.Id,
            "Newer",
            DateTimeOffset.Parse("2026-08-01T10:02:00Z"));
        await repo.ReplyAsync(
            conversationId,
            alice.Id,
            "Older",
            DateTimeOffset.Parse("2026-08-01T10:01:00Z"));

        var item = Assert.Single((await repo.GetInboxAsync(alice.Id)).Items);
        // Tip-by-SortKey preview; LastMessageAt remains the max observed timestamp.
        Assert.Equal("Older", item.LastMessagePreview);
        Assert.Equal(DateTimeOffset.Parse("2026-08-01T10:02:00Z"), item.LastMessageAt);
    }

    [Fact]
    public async Task GetInbox_PagesConversations()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-inbox@example.com", "Alice"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        for (var i = 1; i <= 5; i++)
        {
            var peer = await members.CreateAsync(NewMember($"peer-inbox{i}@example.com", $"Peer {i}"));
            await repo.SendNewOrExistingAsync(
                alice.Id,
                peer.Id,
                $"Hello {i}",
                DateTimeOffset.Parse("2026-08-01T12:00:00Z").AddMinutes(i));
        }

        var page1 = await repo.GetInboxAsync(alice.Id, page: 1, pageSize: 2);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("Peer 5", page1.Items[0].OtherParticipantDisplayName);

        var page3 = await repo.GetInboxAsync(alice.Id, page: 3, pageSize: 2);
        Assert.Equal(3, page3.Page);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task Archive_HidesConversationFromInbox_ButNotForOtherParticipant()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-archive@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-archive@example.com", "Bob"));
        var carol = await members.CreateAsync(NewMember("c-archive@example.com", "Carol"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Archive me",
            DateTimeOffset.Parse("2026-08-01T09:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        Assert.True(await repo.ArchiveConversationAsync(conversationId, alice.Id));
        Assert.False(await repo.ArchiveConversationAsync(conversationId, carol.Id));

        Assert.Empty((await repo.GetInboxAsync(alice.Id)).Items);
        Assert.Single((await repo.GetInboxAsync(bob.Id)).Items);

        var archived = Assert.Single((await repo.GetArchivedInboxAsync(alice.Id)).Items);
        Assert.Equal(bob.Id, archived.OtherParticipantId);
    }

    [Fact]
    public async Task NewMessage_UnarchivesConversation_ForBothParticipants()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-reopen@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-reopen@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Start",
            DateTimeOffset.Parse("2026-08-01T09:10:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repo.ArchiveConversationAsync(conversationId, alice.Id);
        Assert.Empty((await repo.GetInboxAsync(alice.Id)).Items);

        await repo.ReplyAsync(
            conversationId,
            bob.Id,
            "Reopens it",
            DateTimeOffset.Parse("2026-08-01T09:11:00Z"));

        Assert.Single((await repo.GetInboxAsync(alice.Id)).Items);
        Assert.Empty((await repo.GetArchivedInboxAsync(alice.Id)).Items);
    }

    [Fact]
    public async Task Unarchive_MovesConversationBackToInbox()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-unarchive@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-unarchive@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Toggle",
            DateTimeOffset.Parse("2026-08-01T09:20:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repo.ArchiveConversationAsync(conversationId, alice.Id);
        Assert.True(await repo.UnarchiveConversationAsync(conversationId, alice.Id));

        Assert.Single((await repo.GetInboxAsync(alice.Id)).Items);
        Assert.Empty((await repo.GetArchivedInboxAsync(alice.Id)).Items);
    }

    [Fact]
    public async Task Remove_HidesConversationFromInbox_ButNotForOtherParticipant()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-remove@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-remove@example.com", "Bob"));
        var carol = await members.CreateAsync(NewMember("c-remove@example.com", "Carol"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Remove me",
            DateTimeOffset.Parse("2026-08-01T09:00:00Z"));
        var conversationId = created.ConversationId!.Value;

        Assert.True(await repo.RemoveConversationAsync(conversationId, alice.Id));
        Assert.False(await repo.RemoveConversationAsync(conversationId, carol.Id));

        Assert.Empty((await repo.GetInboxAsync(alice.Id)).Items);
        Assert.Single((await repo.GetInboxAsync(bob.Id)).Items);
    }

    [Fact]
    public async Task NewMessage_RestoresRemovedConversation_ForBothParticipants()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-restore@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-restore@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Start",
            DateTimeOffset.Parse("2026-08-01T09:10:00Z"));
        var conversationId = created.ConversationId!.Value;

        await repo.RemoveConversationAsync(conversationId, alice.Id);
        Assert.Empty((await repo.GetInboxAsync(alice.Id)).Items);

        await repo.ReplyAsync(
            conversationId,
            bob.Id,
            "Reopens it",
            DateTimeOffset.Parse("2026-08-01T09:11:00Z"));

        Assert.Single((await repo.GetInboxAsync(alice.Id)).Items);
    }

    [Fact]
    public async Task CountMessagesBySenderSinceAsync_CountsOnlyMessagesInWindow()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-count@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-count@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var since = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
        var created = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Before window",
            since.AddMinutes(-5));
        await repo.ReplyAsync(created.ConversationId!.Value, alice.Id, "Inside window", since.AddMinutes(1));
        await repo.ReplyAsync(created.ConversationId.Value, alice.Id, "Also inside window", since.AddMinutes(2));

        Assert.Equal(2, await repo.CountMessagesBySenderSinceAsync(alice.Id, since));
        Assert.Equal(0, await repo.CountMessagesBySenderSinceAsync(bob.Id, since));
    }

    [Fact]
    public async Task CountIdenticalMessagesBySenderSinceAsync_MatchesExactBodyOnly()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-dup@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-dup@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var since = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
        var created = await repo.SendNewOrExistingAsync(alice.Id, bob.Id, "Repeat me", since);
        await repo.ReplyAsync(created.ConversationId!.Value, alice.Id, "Repeat me", since.AddMinutes(1));
        await repo.ReplyAsync(created.ConversationId.Value, alice.Id, "Different", since.AddMinutes(2));

        Assert.Equal(2, await repo.CountIdenticalMessagesBySenderSinceAsync(alice.Id, "Repeat me", since));
        Assert.Equal(1, await repo.CountIdenticalMessagesBySenderSinceAsync(alice.Id, "Different", since));
    }

    [Fact]
    public async Task CountDistinctNewRecipientsSinceAsync_ExcludesRepliesInOlderConversations()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-fanout@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-fanout@example.com", "Bob"));
        var carol = await members.CreateAsync(NewMember("c-fanout@example.com", "Carol"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var since = DateTimeOffset.Parse("2026-08-01T10:00:00Z");

        // Existing conversation, started before the window: a reply here should not count.
        var oldConversation = await repo.SendNewOrExistingAsync(
            alice.Id,
            bob.Id,
            "Old conversation",
            since.AddMinutes(-30));
        await repo.ReplyAsync(oldConversation.ConversationId!.Value, alice.Id, "Reply in old thread", since.AddMinutes(1));

        // New conversation, started inside the window: this should count.
        await repo.SendNewOrExistingAsync(alice.Id, carol.Id, "New conversation", since.AddMinutes(2));

        Assert.Equal(1, await repo.CountDistinctNewRecipientsSinceAsync(alice.Id, since));
    }

    [Fact]
    public async Task CreateReport_SurvivesInboxRemoval_AndRejectsOutsiders()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-report@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-report@example.com", "Bob"));
        var carol = await members.CreateAsync(NewMember("c-report@example.com", "Carol"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(alice.Id, bob.Id, "Context", DateTimeOffset.UtcNow);
        var conversationId = created.ConversationId!.Value;
        await repo.ReplyAsync(conversationId, alice.Id, "Target", DateTimeOffset.UtcNow);
        var target = (await repo.GetConversationAsync(conversationId, bob.Id))!.Messages[^1];

        var outsider = await repo.CreateReportAsync(
            carol.Id,
            conversationId,
            target.Id,
            "Nope",
            DateTimeOffset.UtcNow);
        Assert.False(outsider.Succeeded);
        Assert.Equal(PrivateMessageReportText.NotAParticipant, outsider.ErrorMessage);

        var reported = await repo.CreateReportAsync(
            bob.Id,
            conversationId,
            target.Id,
            "Abuse",
            DateTimeOffset.UtcNow);
        Assert.True(reported.Succeeded);
        Assert.True(await repo.RemoveConversationAsync(conversationId, bob.Id));

        var snapshot = await repo.GetReportAsync(reported.ReportId!.Value);
        Assert.NotNull(snapshot);
        Assert.Equal("Target", snapshot!.MessageBodySnapshot);
        Assert.Equal("Abuse", snapshot.Reason);
        Assert.Equal("Context", Assert.Single(snapshot.PrecedingMessages).Body);
        Assert.Contains(target.Id, await repo.GetReportedMessageIdsAsync(conversationId, bob.Id));
    }

    [Fact]
    public async Task ListReportsAsync_And_UpdateReportStatusAsync_TrackStatusAndAudit()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-mod@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-mod@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        var created = await repo.SendNewOrExistingAsync(alice.Id, bob.Id, "Hi", DateTimeOffset.UtcNow);
        var conversationId = created.ConversationId!.Value;
        var target = (await repo.GetConversationAsync(conversationId, bob.Id))!.Messages[^1];
        var report = await repo.CreateReportAsync(bob.Id, conversationId, target.Id, "Abuse", DateTimeOffset.UtcNow);
        var reportId = report.ReportId!.Value;

        var openPage = await repo.ListReportsAsync(PrivateMessageReportStatus.Open, 1, 50);
        var listed = Assert.Single(openPage.Items);
        Assert.Equal("Alice", listed.ReportedDisplayName);
        Assert.Equal("Bob", listed.ReporterDisplayName);
        Assert.Equal(1, await repo.CountOpenReportsAsync());

        await repo.AppendReportViewedAuditAsync(reportId, "mod@example.com");

        var updated = await repo.UpdateReportStatusAsync(reportId, PrivateMessageReportStatus.Actioned, "mod@example.com");
        Assert.Equal(PrivateMessageReportStatus.Actioned, updated!.Status);
        Assert.Equal(0, await repo.CountOpenReportsAsync());

        var actionedPage = await repo.ListReportsAsync(PrivateMessageReportStatus.Actioned, 1, 50);
        Assert.Equal(reportId, Assert.Single(actionedPage.Items).Id);

        var missing = await repo.UpdateReportStatusAsync(Guid.NewGuid(), PrivateMessageReportStatus.Actioned, "mod@example.com");
        Assert.Null(missing);
    }

    [Fact]
    public async Task PurgeExpiredReportsAsync_OnlyRemovesTerminalReportsPastRetention()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = await members.CreateAsync(NewMember("a-purge@example.com", "Alice"));
        var bob = await members.CreateAsync(NewMember("b-purge@example.com", "Bob"));
        var repo = new InMemoryPrivateMessageRepository(id =>
            members.FindByIdAsync(id).GetAwaiter().GetResult());

        // Status-change audits are stamped with DateTimeOffset.UtcNow (same as EF).
        // Anchor `now` to the clock so the retention window does not drift off a hardcoded day.
        var now = DateTimeOffset.UtcNow;
        var sent = await repo.SendNewOrExistingAsync(alice.Id, bob.Id, "Hi", now);
        var target = (await repo.GetConversationAsync(sent.ConversationId!.Value, bob.Id))!.Messages[^1];
        var report = await repo.CreateReportAsync(bob.Id, sent.ConversationId!.Value, target.Id, "Reason", now);
        var reportId = report.ReportId!.Value;

        await repo.UpdateReportStatusAsync(reportId, PrivateMessageReportStatus.Dismissed, "mod@example.com");

        // Not yet past the retention window: nothing purged.
        Assert.Equal(0, await repo.PurgeExpiredReportsAsync(now));
        Assert.NotNull(await repo.GetReportAsync(reportId));

        var wellPastRetention = now + PrivateMessageLimits.ReportRetentionAfterTerminalStatus + TimeSpan.FromDays(1);
        Assert.Equal(1, await repo.PurgeExpiredReportsAsync(wellPastRetention));
        Assert.Null(await repo.GetReportAsync(reportId));
    }

    private static MemberAccount NewMember(string email, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = name,
            CreatedAt = DateTime.UtcNow,
        };
}
