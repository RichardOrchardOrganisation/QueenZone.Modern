using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class InMemoryPrivateMessageRepositoryTests : PrivateMessageRepositoryContractTests
{
    private readonly InMemoryMemberAccountRepository contractMembers = new();
    private readonly InMemoryPrivateMessageRepository contractRepository;
    private readonly Guid contractAliceId = Guid.NewGuid();
    private readonly Guid contractBobId = Guid.NewGuid();
    private readonly Guid contractCarolId = Guid.NewGuid();

    public InMemoryPrivateMessageRepositoryTests()
    {
        contractMembers.CreateAsync(new MemberAccount { Id = contractAliceId, Email = "contract-alice@example.test", DisplayName = "Alice", CreatedAt = DateTime.UtcNow }).GetAwaiter().GetResult();
        contractMembers.CreateAsync(new MemberAccount { Id = contractBobId, Email = "contract-bob@example.test", DisplayName = "Bob", CreatedAt = DateTime.UtcNow }).GetAwaiter().GetResult();
        contractMembers.CreateAsync(new MemberAccount { Id = contractCarolId, Email = "contract-carol@example.test", DisplayName = "Carol", CreatedAt = DateTime.UtcNow }).GetAwaiter().GetResult();
        contractRepository = new InMemoryPrivateMessageRepository(id => contractMembers.FindByIdAsync(id).GetAwaiter().GetResult());
    }

    protected override IPrivateMessageRepository Repository => contractRepository;
    protected override Guid AliceId => contractAliceId;
    protected override Guid BobId => contractBobId;
    protected override Guid CarolId => contractCarolId;
    protected override string BobName => "Bob";
    protected override string CarolName => "Carol";

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
