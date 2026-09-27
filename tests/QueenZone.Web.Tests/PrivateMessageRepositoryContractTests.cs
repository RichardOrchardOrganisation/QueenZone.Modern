using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>Runs the same observable messaging behavior against the route fake and SQLite EF.</summary>
public abstract class PrivateMessageRepositoryContractTests
{
    protected abstract IPrivateMessageRepository Repository { get; }
    protected abstract Guid AliceId { get; }
    protected abstract Guid BobId { get; }
    protected abstract Guid CarolId { get; }
    protected abstract string BobName { get; }
    protected abstract string CarolName { get; }

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-08-01T10:00:00Z");

    [Fact]
    public async Task Inbox_UsesInsertOrder_AndIsolatedPerParticipant()
    {
        await Repository.SendNewOrExistingAsync(AliceId, CarolId, "Carol first", Start.AddHours(10));
        await Repository.SendNewOrExistingAsync(AliceId, BobId, "Bob second", Start.AddHours(-2));

        var inbox = await Repository.GetInboxAsync(AliceId);
        Assert.Equal([BobName, CarolName], inbox.Items.Select(i => i.OtherParticipantDisplayName).ToArray());
        Assert.True(inbox.Items[0].LastMessageAt < inbox.Items[1].LastMessageAt);
        Assert.Equal(AliceId, Assert.Single((await Repository.GetInboxAsync(BobId)).Items).OtherParticipantId);
        Assert.DoesNotContain((await Repository.GetInboxAsync(BobId)).Items, i => i.OtherParticipantId == CarolId);
    }

    [Fact]
    public async Task Conversation_PagesFromLatest_AndPreservesMessageOrder()
    {
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Msg 1", Start);
        for (var i = 2; i <= 5; i++)
        {
            await Repository.ReplyAsync(created.ConversationId!.Value, AliceId, $"Msg {i}", Start.AddMinutes(i));
        }

        var latest = await Repository.GetConversationAsync(created.ConversationId!.Value, BobId, pageSize: 2);
        Assert.Equal(3, latest!.Page);
        Assert.Equal(["Msg 4", "Msg 5"], latest.Messages.Select(m => m.Body).ToArray());
        var first = await Repository.GetConversationAsync(created.ConversationId.Value, BobId, page: 1, pageSize: 2);
        Assert.Equal(["Msg 1", "Msg 2"], first!.Messages.Select(m => m.Body).ToArray());
    }

    [Fact]
    public async Task Preview_IsTruncatedWithoutChangingStoredBody()
    {
        var body = new string('x', PrivateMessageLimits.PreviewLength + 40);
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, body, Start);
        var preview = Assert.Single((await Repository.GetInboxAsync(BobId)).Items).LastMessagePreview;
        Assert.Equal(PrivateMessageLimits.PreviewLength, preview.Length);
        Assert.Equal(body, Assert.Single((await Repository.GetConversationAsync(created.ConversationId!.Value, BobId))!.Messages).Body);
    }

    [Fact]
    public async Task ComposeAndReply_PreserveMarkupAsPlainText()
    {
        const string markup = "<script>alert(1)</script>";
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, markup, Start);
        Assert.True(created.Succeeded);
        var reply = await Repository.ReplyAsync(created.ConversationId!.Value, BobId, markup + " reply", Start.AddMinutes(1));
        Assert.True(reply.Succeeded);
        var detail = await Repository.GetConversationAsync(created.ConversationId.Value, BobId);
        Assert.Equal(markup, detail!.Messages[0].Body);
        Assert.Equal(markup + " reply", detail.Messages[1].Body);
        Assert.Equal(markup + " reply", Assert.Single((await Repository.GetInboxAsync(AliceId)).Items).LastMessagePreview);
    }

    [Fact]
    public async Task Reply_TipFollowsSortKey_WhileTimestampRemainsMonotonic()
    {
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Start", Start);
        await Repository.ReplyAsync(created.ConversationId!.Value, BobId, "Newer", Start.AddMinutes(2));
        await Repository.ReplyAsync(created.ConversationId.Value, AliceId, "Older", Start.AddMinutes(1));

        var item = Assert.Single((await Repository.GetInboxAsync(AliceId)).Items);
        Assert.Equal("Older", item.LastMessagePreview);
        Assert.Equal(Start.AddMinutes(2), item.LastMessageAt);
    }

    [Fact]
    public async Task Archive_IsPerParticipant_AndReplyReopensIt()
    {
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Start", Start);
        var id = created.ConversationId!.Value;
        Assert.True(await Repository.ArchiveConversationAsync(id, AliceId));
        Assert.False(await Repository.ArchiveConversationAsync(id, CarolId));
        Assert.Empty((await Repository.GetInboxAsync(AliceId)).Items);
        Assert.Single((await Repository.GetInboxAsync(BobId)).Items);
        Assert.Single((await Repository.GetArchivedInboxAsync(AliceId)).Items);

        await Repository.ReplyAsync(id, BobId, "Reopen", Start.AddMinutes(1));
        Assert.Single((await Repository.GetInboxAsync(AliceId)).Items);
        Assert.Empty((await Repository.GetArchivedInboxAsync(AliceId)).Items);
    }

    [Fact]
    public async Task Unarchive_ReturnsConversationToInbox()
    {
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Start", Start);
        var id = created.ConversationId!.Value;
        await Repository.ArchiveConversationAsync(id, AliceId);
        Assert.True(await Repository.UnarchiveConversationAsync(id, AliceId));
        Assert.Single((await Repository.GetInboxAsync(AliceId)).Items);
        Assert.Empty((await Repository.GetArchivedInboxAsync(AliceId)).Items);
    }

    [Fact]
    public async Task Remove_IsPerParticipant_AndReplyRestoresIt()
    {
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Start", Start);
        var id = created.ConversationId!.Value;
        Assert.True(await Repository.RemoveConversationAsync(id, AliceId));
        Assert.False(await Repository.RemoveConversationAsync(id, CarolId));
        Assert.Empty((await Repository.GetInboxAsync(AliceId)).Items);
        Assert.Single((await Repository.GetInboxAsync(BobId)).Items);

        await Repository.ReplyAsync(id, BobId, "Reopen", Start.AddMinutes(1));
        Assert.Single((await Repository.GetInboxAsync(AliceId)).Items);
    }

    [Fact]
    public async Task RateLimitCounts_RespectSenderBodyAndWindow()
    {
        var created = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Repeat", Start.AddMinutes(-1));
        await Repository.ReplyAsync(created.ConversationId!.Value, AliceId, "Repeat", Start.AddMinutes(1));
        await Repository.ReplyAsync(created.ConversationId.Value, AliceId, "Different", Start.AddMinutes(2));

        Assert.Equal(2, await Repository.CountMessagesBySenderSinceAsync(AliceId, Start));
        Assert.Equal(0, await Repository.CountMessagesBySenderSinceAsync(BobId, Start));
        Assert.Equal(1, await Repository.CountIdenticalMessagesBySenderSinceAsync(AliceId, "Repeat", Start));
        Assert.Equal(1, await Repository.CountIdenticalMessagesBySenderSinceAsync(AliceId, "Different", Start));
    }

    [Fact]
    public async Task DistinctNewRecipients_ExcludesRepliesInOldConversations()
    {
        var old = await Repository.SendNewOrExistingAsync(AliceId, BobId, "Old", Start.AddMinutes(-30));
        await Repository.ReplyAsync(old.ConversationId!.Value, AliceId, "Reply", Start.AddMinutes(1));
        await Repository.SendNewOrExistingAsync(AliceId, CarolId, "New", Start.AddMinutes(2));
        Assert.Equal(1, await Repository.CountDistinctNewRecipientsSinceAsync(AliceId, Start));
    }
}
