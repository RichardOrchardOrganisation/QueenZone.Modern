using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Sitemap;
using MemberActionModel = QueenZone.Web.Pages.Admin.Members.ActionModel;

namespace QueenZone.Web.Tests;

public sealed class ForumSitemapInvalidationTests(QueenZoneWebApplicationFactory factory)
    : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Theory]
    [InlineData("hide")]
    [InlineData("unhide")]
    [InlineData("reinstate")]
    [InlineData("author-hide")]
    [InlineData("author-unhide")]
    [InlineData("post-author-hide")]
    [InlineData("auto-suspend")]
    public async Task Visibility_write_invalidates_sitemap_after_content_changes(string action)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var members = services.GetRequiredService<IMemberAccountRepository>();
        var member = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = $"Author {Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
        });
        var forum = new InMemoryForumWriteRepository();
        var thread = await forum.CreateThreadAsync(new NewForumThread(
            1, member.Id, member.DisplayName, "Sitemap visibility", "Body", DateTimeOffset.UtcNow));
        var restore = action is "unhide" or "reinstate" or "author-unhide";
        if (restore)
        {
            await forum.HideAuthorForumContentAsync(member.Id, member.DisplayName);
            await members.SuspendAsync(member.Id, "Spam", "admin@test.local", DateTime.UtcNow);
        }

        var sitemap = new RecordingSitemapService(async () =>
        {
            var summary = await forum.GetAuthorForumContentSummaryAsync(member.Id, member.DisplayName);
            Assert.Equal(!restore, summary.IsHidden);
        });
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers["X-Test-User-Email"] = "admin@test.local";

        if (action == "auto-suspend")
        {
            var moderation = ActivatorUtilities.CreateInstance<ForumPostModerationService>(services, forum, sitemap);
            await moderation.FlagIfLikelySpamAsync(member.Id, member.CreatedAt, member.Email,
                DateTimeOffset.UtcNow, "https://spam.example", CancellationToken.None);
            Assert.True((await members.FindByIdAsync(member.Id))!.IsSuspended);
        }
        else
        {
            PageModel model;
            Task<IActionResult> result;
            if (action.StartsWith("author-", StringComparison.Ordinal))
            {
                var author = new Pages.Admin.Members.ForumAuthorActionModel(forum, sitemap)
                {
                    DisplayName = member.DisplayName,
                };
                model = author;
                SetPageContext(model, http);
                result = restore ? author.OnPostUnhideAsync(CancellationToken.None) : author.OnPostHideAsync(CancellationToken.None);
            }
            else if (action == "post-author-hide")
            {
                var hide = ActivatorUtilities.CreateInstance<Pages.Forum.HideAuthorModel>(services, forum, sitemap);
                model = hide;
                SetPageContext(model, http);
                result = hide.OnPostAsync(thread.StarterPostId, CancellationToken.None);
            }
            else
            {
                var memberAction = ActivatorUtilities.CreateInstance<MemberActionModel>(services, forum, sitemap);
                model = memberAction;
                SetPageContext(model, http);
                result = action switch
                {
                    "hide" => memberAction.OnPostHideForumContentAsync(member.Id, CancellationToken.None),
                    "unhide" => memberAction.OnPostUnhideForumContentAsync(member.Id, CancellationToken.None),
                    _ => memberAction.OnPostReinstateAsync(member.Id, CancellationToken.None),
                };
            }

            Assert.IsType<RedirectResult>(await result);
        }

        Assert.Equal(1, sitemap.Invalidations);
    }

    private static void SetPageContext(PageModel model, HttpContext http)
    {
        model.PageContext = new PageContext { HttpContext = http };
        model.TempData = http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
    }

    private sealed class RecordingSitemapService(Func<Task> assertVisibility) : ICoreSitemapService
    {
        public int Invalidations { get; private set; }

        public async Task InvalidateAsync(CancellationToken cancellationToken = default)
        {
            await assertVisibility();
            Invalidations++;
        }
    }
}
