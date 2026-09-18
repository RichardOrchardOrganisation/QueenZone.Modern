using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.RealData)]
public class ForumBlockWorkflowTests : RealDataPageTest
{
    [Test]
    public async Task MemberBlocksForumAuthorAndTheirPostCollapses()
    {
        var marker = NextMarker("forum-block");
        var author = await CreateMemberAsync(marker, "author");
        var reader = await CreateMemberAsync(marker, "reader");
        var subject = $"{marker} forum thread";
        var body = $"{marker} forum body";

        var authorContext = await CreateMemberContextAsync(author);
        var authorPage = await authorContext.NewPageAsync();
        var categoryPath = await FindWritableCategoryPathAsync(authorPage);
        await authorPage.GotoAsync($"{categoryPath}/new-thread");
        await authorPage.GetByLabel("Subject").FillAsync(subject);
        await FillEditorAsync(authorPage, body);
        await authorPage.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();
        await Expect(authorPage).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/.*"));
        var topicUrl = authorPage.Url;
        await Context.SetExtraHTTPHeadersAsync(HeadersFor(reader));
        await Page.GotoAsync(topicUrl);
        var post = Page.Locator(".qz-forum-post").Filter(new() { HasText = body });
        await Expect(post.GetByRole(AriaRole.Button, new() { Name = "Block member" })).ToBeVisibleAsync();
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await post.GetByRole(AriaRole.Button, new() { Name = "Block member" }).ClickAsync();

        await Expect(Page.Locator(".qz-account-notice[role='status']")).ToContainTextAsync("Member blocked");
        var blockedPost = Page.Locator(".qz-forum-post").Filter(new() { HasText = body });
        await Expect(blockedPost.GetByText("Post from a blocked member. Show this post")).ToBeVisibleAsync();
        await Expect(blockedPost.GetByRole(AriaRole.Button, new() { Name = "Block member" })).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(Page.Locator(".qz-forum-post").Filter(new() { HasText = body })
            .GetByText("Post from a blocked member. Show this post")).ToBeVisibleAsync();
    }

    private async Task<MemberContext> CreateMemberAsync(string marker, string role)
    {
        var id = Guid.NewGuid();
        var email = $"{marker}-{role}@e2e.queenzone.local";
        var displayName = $"E2E {role} {marker}";
        await using var db = RealDataDb.CreateContext();
        db.MemberAccounts.Add(new MemberAccount
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return new MemberContext(id, displayName, email);
    }

    private async Task<IBrowserContext> CreateMemberContextAsync(MemberContext member) =>
        await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = HeadersFor(member),
        });

    private static Dictionary<string, string> HeadersFor(MemberContext member) => new()
    {
        ["X-Test-Member-Id"] = member.Id.ToString(),
        ["X-Test-Member-Name"] = member.DisplayName,
        ["X-Test-Member-Email"] = member.Email,
    };

    private static async Task FillEditorAsync(IPage page, string body)
    {
        var editor = page.Locator("[data-testid='rich-text-editor']");
        await editor.ClickAsync();
        await page.Keyboard.InsertTextAsync(body);
    }

    private static async Task<string> FindWritableCategoryPathAsync(IPage page)
    {
        await page.GotoAsync("/forum");
        var categoryHref = await page.Locator("a.qz-card[href^='/forum/']").First.GetAttributeAsync("href");
        Assert.That(categoryHref, Is.Not.Null.And.Not.Empty, "Expected a forum category.");
        await page.GotoAsync(categoryHref!);
        var link = page.GetByRole(AriaRole.Link, new() { Name = "New thread" }).First;
        var href = await link.GetAttributeAsync("href");
        Assert.That(href, Is.Not.Null.And.Not.Empty, "Expected a writable forum category.");
        return href![..href.LastIndexOf("/new-thread", StringComparison.Ordinal)];
    }

    protected override async Task CleanupCreatedRowsAsync(IReadOnlyList<string> markers)
    {
        await using var db = RealDataDb.CreateContext();
        foreach (var marker in markers)
        {
            var threads = await db.ModernForumThreads.Where(t => t.Title.Contains(marker)).ToListAsync();
            foreach (var thread in threads)
            {
                var id = thread.LegacyTopicId;
                await db.Database.ExecuteSqlRawAsync(
                    "IF OBJECT_ID(N'dbo.ModernForumThreadReadStats', N'U') IS NOT NULL DELETE FROM dbo.ModernForumThreadReadStats WHERE LegacyTopicId = {0};",
                    id);
                await db.ModernForumPosts.Where(p => p.ThreadId == thread.Id).ExecuteDeleteAsync();
                await db.ModernForumThreads.Where(t => t.Id == thread.Id).ExecuteDeleteAsync();
                await SearchDocumentTeardown.DeleteBySourceKeysAsync(db, [SearchDocumentSourceKey.ForForumThread(id)]);
            }
        }

        foreach (var marker in markers)
        {
            var memberIds = await db.MemberAccounts.Where(m => m.Email.Contains(marker))
                .Select(m => m.Id).ToListAsync();
            await db.MemberMessageBlocks.Where(b => memberIds.Contains(b.BlockerMemberId)
                || memberIds.Contains(b.BlockedMemberId)).ExecuteDeleteAsync();
            await db.MemberAccounts.Where(m => memberIds.Contains(m.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed record MemberContext(Guid Id, string DisplayName, string Email);
}
