using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.E2E;

/// <summary>
/// Two-party private messaging journey (#547): compose (via <c>Messages/Compose</c> and the
/// <c>Members/Profile</c> "Message" affordance), reply, tip ordering, unread state, archive,
/// blocking, and inbox pagination — asserted from the rendered UI, mirroring the ordering
/// guarantees <c>scripts/Probe-PrivateMessaging.ps1</c> checks at the repository layer. Runs
/// against the SQL Express mirror (<c>ASPNETCORE_ENVIRONMENT=E2E</c>); every row this fixture
/// creates is tagged with the <c>uie2e-{runId}-...</c> marker convention and deleted in
/// <see cref="CleanupCreatedRowsAsync"/>. Part of #540.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.RealData)]
public class PrivateMessagingWorkflowTests : RealDataPageTest
{
    private const string MemberIdHeader = "X-Test-Member-Id";
    private const string MemberNameHeader = "X-Test-Member-Name";
    private const string MemberEmailHeader = "X-Test-Member-Email";

    [Test]
    public async Task Two_members_can_compose_reply_archive_and_block_through_the_ui()
    {
        var memberA = await CreateMemberAsync("pm-a");
        var memberB = await CreateMemberAsync("pm-b");

        await Context.SetExtraHTTPHeadersAsync(HeadersFor(memberA));

        var contextB = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = HeadersFor(memberB),
        });
        var pageB = await contextB.NewPageAsync();

        // Compose via the "Message" affordance on B's Members/Profile.
        var firstBody = $"{memberA.Marker} first message to B";
        await Page.GotoAsync($"/members/{memberB.Id}");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Message", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/messages/compose\\?to=.*"));

        // Textbox role, not GetByLabel("Message"): Playwright substring-matches the masthead
        // aria-label="Messages" (and "Messages, N unread conversations") on every signed-in page.
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Message", Exact = true }).FillAsync(firstBody);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/messages/[0-9a-fA-F-]{36}$"));
        var conversationUrl = Page.Url;
        await Expect(Page.Locator(".qz-message-thread__item").Filter(new() { HasText = firstBody })).ToBeVisibleAsync();

        // Appears in A's sent view (their inbox — there is no separate "sent" page).
        await Page.GotoAsync("/messages");
        await Expect(Page.Locator(".qz-message-list__item").Filter(new() { HasText = firstBody })).ToBeVisibleAsync();

        // Appears in B's inbox with unread state.
        await pageB.GotoAsync("/messages");
        var rowB = pageB.Locator(".qz-message-list__item").Filter(new() { HasText = firstBody });
        await Expect(rowB).ToBeVisibleAsync();
        await Expect(rowB).ToHaveClassAsync(new Regex("qz-message-list__item--unread"));
        await Expect(rowB.Locator(".qz-message-list__badge")).ToContainTextAsync("1 unread");

        // B opens the conversation: unread clears, then replies.
        await rowB.Locator(".qz-message-list__link").ClickAsync();
        await Expect(pageB.Locator(".qz-message-thread__item").Filter(new() { HasText = firstBody })).ToBeVisibleAsync();

        var replyBody = $"{memberB.Marker} reply to A";
        await pageB.GetByRole(AriaRole.Textbox, new() { Name = "Reply", Exact = true }).FillAsync(replyBody);
        await pageB.GetByRole(AriaRole.Button, new() { Name = "Send reply" }).ClickAsync();
        await Expect(pageB.Locator(".qz-message-thread__item").Filter(new() { HasText = replyBody })).ToBeVisibleAsync();

        await pageB.GotoAsync("/messages");
        var rowBAfterReply = pageB.Locator(".qz-message-list__item").Filter(new() { HasText = replyBody });
        await Expect(rowBAfterReply).ToBeVisibleAsync();
        await Expect(rowBAfterReply).Not.ToHaveClassAsync(new Regex("qz-message-list__item--unread"));

        // A sees the reply; the conversation is at the tip of A's inbox (LastMessageSortKey ordering).
        await Page.GotoAsync("/messages");
        var topRowA = Page.Locator(".qz-message-list__item").First;
        await Expect(topRowA).ToContainTextAsync(replyBody);
        await Expect(topRowA).ToHaveClassAsync(new Regex("qz-message-list__item--unread"));

        await topRowA.Locator(".qz-message-list__link").ClickAsync();
        await Expect(Page.Locator(".qz-message-thread__item").Filter(new() { HasText = replyBody })).ToBeVisibleAsync();

        // Archive: leaves Messages/Index, appears in Messages/Archived.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Archive conversation" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/messages$"));
        await Expect(Page.GetByText("Conversation archived.")).ToBeVisibleAsync();
        await Expect(Page.Locator(".qz-message-list__item").Filter(new() { HasText = replyBody })).ToHaveCountAsync(0);

        await Page.GotoAsync("/messages/archived");
        var archivedRow = Page.Locator(".qz-message-list__item").Filter(new() { HasText = replyBody });
        await Expect(archivedRow).ToBeVisibleAsync();

        // Unarchive restores it to the inbox.
        await archivedRow.GetByRole(AriaRole.Button, new() { Name = "Unarchive" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/messages/archived$"));
        await Page.GotoAsync("/messages");
        await Expect(Page.Locator(".qz-message-list__item").Filter(new() { HasText = replyBody })).ToBeVisibleAsync();

        // Blocking: B blocks A from B's view of A's profile; confirm in the styled dialog.
        var profilePath = $"/members/{memberA.Id}";
        try
        {
            await pageB.GotoAsync(profilePath, new PageGotoOptions { Timeout = 60_000 });
        }
        catch (TimeoutException ex)
        {
            Assert.Fail(
                $"Timed out navigating to member profile for block step ({profilePath}). " +
                $"Last URL: {pageB.Url}. {ex.Message}");
        }

        await pageB.GetByRole(AriaRole.Button, new() { Name = "Block", Exact = true }).ClickAsync();
        await pageB.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Block" }).ClickAsync();
        await Expect(pageB.GetByText("Member blocked. They can no longer send you private messages.")).ToBeVisibleAsync();

        // A can no longer message B: the "Message" affordance disappears from B's profile...
        await Page.GotoAsync($"/members/{memberB.Id}");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Message", Exact = true })).ToHaveCountAsync(0);

        // ...and the conversation view shows that sending is no longer available.
        await Page.GotoAsync(conversationUrl);
        await Expect(Page.GetByText("Unable to send message.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send reply" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Inbox_paginates_when_more_than_a_page_of_conversations_exist()
    {
        var owner = await CreateMemberAsync("pm-inbox-page");
        const int conversationCount = PrivateMessageLimits.InboxPageSize + 1;
        List<string> bodiesBySortKeyDescending;

        await using (var db = RealDataDb.CreateContext())
        {
            var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
            var conversations = new List<PrivateConversationEntity>();

            for (var i = 0; i < conversationCount; i++)
            {
                var peerMarker = $"{owner.Marker}-peer-{i}";
                var peerId = Guid.NewGuid();
                var peerEmail = $"{peerMarker}@e2e.queenzone.local";
                db.MemberAccounts.Add(new MemberAccount
                {
                    Id = peerId,
                    Email = peerEmail,
                    NormalizedEmail = peerEmail.ToUpperInvariant(),
                    DisplayName = $"E2E {peerMarker}",
                    CreatedAt = DateTime.UtcNow,
                });

                var (low, high) = owner.Id.CompareTo(peerId) < 0 ? (owner.Id, peerId) : (peerId, owner.Id);
                var sentAt = baseTime.AddSeconds(i);
                var body = $"{owner.Marker} pagination message {i}";
                var conversation = new PrivateConversationEntity
                {
                    Id = Guid.NewGuid(),
                    MemberLowId = low,
                    MemberHighId = high,
                    CreatedAt = sentAt,
                    LastMessageAt = sentAt,
                    LastMessageSortKey = 0,
                    LastMessagePreview = body,
                    LastMessageSenderId = peerId,
                };
                db.PrivateConversations.Add(conversation);
                conversations.Add(conversation);

                db.PrivateConversationParticipants.Add(new PrivateConversationParticipantEntity
                {
                    ConversationId = conversation.Id,
                    MemberId = owner.Id,
                    LastReadAt = null,
                    LastReadSortKey = null,
                    IsArchived = false,
                    IsRemoved = false,
                });
                db.PrivateConversationParticipants.Add(new PrivateConversationParticipantEntity
                {
                    ConversationId = conversation.Id,
                    MemberId = peerId,
                    LastReadAt = sentAt,
                    LastReadSortKey = null,
                    IsArchived = false,
                    IsRemoved = false,
                });

                db.PrivateMessages.Add(new PrivateMessageEntity
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    SenderMemberId = peerId,
                    Body = body,
                    CreatedAt = sentAt,
                });
            }

            await db.SaveChangesAsync();

            // IDENTITY SortKey order is not guaranteed to match EF batch insert order. Rank by
            // the values SQL actually assigned, then mirror those tips onto LastMessageSortKey.
            var assigned = await db.PrivateMessages
                .AsNoTracking()
                .Where(m => m.Body.StartsWith(owner.Marker + " pagination message "))
                .OrderByDescending(m => m.SortKey)
                .Select(m => new { m.ConversationId, m.SortKey, m.Body })
                .ToListAsync();

            Assert.That(assigned, Has.Count.EqualTo(conversationCount));

            var byId = conversations.ToDictionary(c => c.Id);
            foreach (var row in assigned)
            {
                byId[row.ConversationId].LastMessageSortKey = row.SortKey;
            }

            await db.SaveChangesAsync();
            bodiesBySortKeyDescending = assigned.Select(a => a.Body).ToList();
        }

        await Context.SetExtraHTTPHeadersAsync(HeadersFor(owner));

        await Page.GotoAsync("/messages");
        await Expect(Page.Locator(".archive-pagination-summary").First).ToContainTextAsync("Page 1 of 2");
        var firstPageBodies = await ReadVisibleMessageBodiesAsync(Page);
        Assert.That(
            firstPageBodies,
            Is.EqualTo(bodiesBySortKeyDescending.Take(PrivateMessageLimits.InboxPageSize).ToList()),
            "Page 1 must list the highest LastMessageSortKey conversations with no overlap into page 2.");

        await Page.Locator("a.archive-pagination-next").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/messages\\?pageNumber=2$"));
        await Expect(Page.Locator(".archive-pagination-summary").First).ToContainTextAsync("Page 2 of 2");
        var secondPageBodies = await ReadVisibleMessageBodiesAsync(Page);

        Assert.That(secondPageBodies, Is.Not.Empty);
        Assert.That(
            firstPageBodies.Concat(secondPageBodies).ToList(),
            Is.EqualTo(bodiesBySortKeyDescending));
        Assert.That(
            secondPageBodies,
            Is.EqualTo(bodiesBySortKeyDescending.Skip(PrivateMessageLimits.InboxPageSize).ToList()));
    }

    private static async Task<IReadOnlyList<string>> ReadVisibleMessageBodiesAsync(IPage page)
    {
        var previews = page.Locator(".qz-message-list__preview");
        var count = await previews.CountAsync();
        var bodies = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            bodies.Add((await previews.Nth(i).InnerTextAsync()).Trim());
        }

        return bodies;
    }

    private async Task<MemberContext> CreateMemberAsync(string fixtureSlug)
    {
        var marker = NextMarker(fixtureSlug);
        var memberId = Guid.NewGuid();
        var email = $"{marker}@e2e.queenzone.local";
        var displayName = $"E2E {marker}";

        await using (var db = RealDataDb.CreateContext())
        {
            db.MemberAccounts.Add(new MemberAccount
            {
                Id = memberId,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return new MemberContext(memberId, marker, displayName, email);
    }

    private static Dictionary<string, string> HeadersFor(MemberContext member) => new()
    {
        [MemberIdHeader] = member.Id.ToString(),
        [MemberNameHeader] = member.DisplayName,
        [MemberEmailHeader] = member.Email,
    };

    protected override async Task CleanupCreatedRowsAsync(IReadOnlyList<string> markers)
    {
        await using var db = RealDataDb.CreateContext();
        foreach (var marker in markers)
        {
            var memberIds = await db.MemberAccounts
                .Where(m => m.Email.Contains(marker))
                .Select(m => m.Id)
                .ToListAsync();
            if (memberIds.Count == 0)
            {
                continue;
            }

            var conversationIds = await db.PrivateConversations
                .Where(c => memberIds.Contains(c.MemberLowId) || memberIds.Contains(c.MemberHighId))
                .Select(c => c.Id)
                .ToListAsync();

            if (conversationIds.Count > 0)
            {
                await db.PrivateMessages
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .ExecuteDeleteAsync();
                await db.PrivateConversationParticipants
                    .Where(p => conversationIds.Contains(p.ConversationId))
                    .ExecuteDeleteAsync();
                await db.PrivateConversations
                    .Where(c => conversationIds.Contains(c.Id))
                    .ExecuteDeleteAsync();
            }

            await db.MemberMessageBlocks
                .Where(b => memberIds.Contains(b.BlockerMemberId) || memberIds.Contains(b.BlockedMemberId))
                .ExecuteDeleteAsync();

            await db.MemberFollows
                .Where(f => memberIds.Contains(f.FollowerMemberId) || memberIds.Contains(f.FollowedMemberId))
                .ExecuteDeleteAsync();

            await db.MemberAccounts
                .Where(m => memberIds.Contains(m.Id))
                .ExecuteDeleteAsync();
        }
    }

    private sealed record MemberContext(Guid Id, string Marker, string DisplayName, string Email);
}
