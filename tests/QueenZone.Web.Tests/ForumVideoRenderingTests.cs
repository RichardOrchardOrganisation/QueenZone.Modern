using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumVideoRenderingTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ForumVideoRenderingTests(QueenZoneWebApplicationFactory factory) => this.factory = factory;

    [Theory]
    [InlineData("/forum/topic/1030/archive-sample-thread-1030")]
    [InlineData("/forum/topic/1030/archive-sample-thread-1030/page/2")]
    public async Task Archive_pages_insert_only_eligible_cards_keep_links_and_allow_exact_frame_origin(string path)
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(path);
        var document = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, document.QuerySelectorAll("[data-qz-forum-video]").Length);
        Assert.Single(document.QuerySelectorAll("[data-testid='forum-video-privacy']"));
        Assert.All(document.QuerySelectorAll("[data-qz-forum-video]"), card => Assert.False(string.IsNullOrEmpty(card.GetAttribute("data-qz-player-origin"))));
        Assert.Empty(document.QuerySelectorAll("iframe"));
        Assert.Empty(document.QuerySelectorAll("img[src^='https://'][src*='youtube'],script[src^='https://'][src*='youtube'],link[href^='https://'][href*='youtube']"));
        Assert.Equal(2, document.QuerySelectorAll("[data-video-load][hidden]").Length);
        Assert.Equal(2, document.QuerySelectorAll("[data-qz-forum-video] a").Count(anchor => anchor.TextContent == "Watch on YouTube"));
        Assert.Single(document.QuerySelectorAll("blockquote a"));
        Assert.Equal("Duplicate link", document.QuerySelector("a[href*='youtu.be/M7lc1UVf-VE']")!.TextContent);
        Assert.Contains("Before the shared video.", document.Body!.TextContent);
        Assert.Contains("After the shared video.", document.Body.TextContent);
        Assert.Empty(document.QuerySelectorAll("footer [data-qz-forum-video]"));
        var csp = response.Headers.GetValues("Content-Security-Policy").Single().Split(';');
        Assert.Equal("frame-src 'self' https://www.googletagmanager.com https://www.youtube-nocookie.com", csp.Single(part => part.TrimStart().StartsWith("frame-src", StringComparison.Ordinal)).Trim());
        Assert.All(csp.Where(part => !part.TrimStart().StartsWith("frame-src", StringComparison.Ordinal)), part => Assert.DoesNotContain("youtube", part));
        Assert.Contains("frame-ancestors 'none'", csp.Select(part => part.Trim()));
        Assert.Contains("object-src 'none'", csp.Select(part => part.Trim()));
    }

    [Fact]
    public void Player_security_headers_pin_frame_script_ancestor_object_and_referrer_policies()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        SecurityHeaders.Apply(context);
        var directives = context.Response.Headers["Content-Security-Policy"].ToString()
            .Split(';', StringSplitOptions.TrimEntries)
            .ToDictionary(part => part.Split(' ')[0]);
        Assert.Equal("frame-src 'self' https://www.googletagmanager.com https://www.youtube-nocookie.com", directives["frame-src"]);
        Assert.Equal("frame-ancestors 'none'", directives["frame-ancestors"]);
        Assert.Equal("object-src 'none'", directives["object-src"]);
        Assert.Equal($"script-src 'self' 'nonce-{CspNonce.Get(context)}' https://www.googletagmanager.com https://www.google-analytics.com", directives["script-src"]);
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task Long_thread_has_thirty_cards_and_one_notice()
    {
        using var client = factory.CreateAnonymousClient();
        var document = new HtmlParser().ParseDocument(await client.GetStringAsync("/forum/topic/1029/archive-sample-thread-1029"));
        Assert.Equal(30, document.QuerySelectorAll("[data-qz-forum-video]").Length);
        Assert.Single(document.QuerySelectorAll("[data-testid='forum-video-privacy']"));
        var scripts = document.QuerySelectorAll("script[src]")
            .Select(script => script.GetAttribute("src") ?? "")
            .Where(src => src.Contains("youtube-", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(scripts, src => src.Contains("youtube-scheduler", StringComparison.Ordinal));
        Assert.Contains(scripts, src => src.Contains("youtube-video", StringComparison.Ordinal));
        Assert.True(
            scripts.FindIndex(src => src.Contains("youtube-scheduler", StringComparison.Ordinal))
            < scripts.FindIndex(src => src.Contains("youtube-video", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Modern_paginated_posts_quote_source_and_edit_content_never_include_cards()
    {
        const string body = """<p>Prose</p><p><strong><a href="https://youtu.be/M7lc1UVf-VE">Video</a></strong></p><iframe src="https://evil.example"></iframe><p>End</p>""";
        var memberId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var created = await repository.CreateThreadAsync(new(1, memberId, "Card Fan", "Modern video cards", body, DateTimeOffset.UtcNow));
        for (var index = 0; index < 15; index++)
        {
            await repository.CreatePostAsync(new(created.TopicId, memberId, "Card Fan", body, DateTimeOffset.UtcNow.AddSeconds(index + 1)));
        }

        using var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Card Fan");
        var path = ForumRoutes.GetTopicCanonicalPath(created.TopicId, "Modern video cards");
        foreach (var page in new[] { path, path + "/page/2" })
        {
            var document = new HtmlParser().ParseDocument(await client.GetStringAsync(page));
            Assert.NotEmpty(document.QuerySelectorAll("[data-qz-forum-video]"));
            foreach (var template in document.QuerySelectorAll("template[data-qz-quote-source]"))
            {
                var content = Assert.IsAssignableFrom<IHtmlTemplateElement>(template).Content;
                Assert.Equal("https://youtu.be/M7lc1UVf-VE", content.QuerySelector("a")!.GetAttribute("href"));
                Assert.Empty(content.QuerySelectorAll("[data-qz-forum-video],iframe"));
            }
        }

        var api = await client.GetFromJsonAsync<ApiPagedResponse<ForumPostDto>>($"/api/v1/forum/topics/{created.TopicId}/posts");
        Assert.All(api!.Items, post => Assert.DoesNotContain("data-qz-forum-video", post.Body));
        Assert.All(api.Items, post => Assert.Single(post.YoutubeVideos!));
        Assert.Equal(body, (await repository.GetPostAsync(created.StarterPostId))!.Body);
    }

    [Fact]
    public void Split_preserves_nested_markup_and_original_anchor_without_accepting_user_markers()
    {
        using var scope = factory.Services.CreateScope();
        var ugc = scope.ServiceProvider.GetRequiredService<UgcHtml>();
        var projection = ForumVideoContent.Project("<p>Before</p><p><span><a href='https://youtu.be/M7lc1UVf-VE'><em>My video</em></a></span></p><p>After</p>", ugc);
        var parts = ForumVideoRendering.Split(projection);
        var card = Assert.Single(parts, part => part.Card is not null).Card!;
        Assert.Contains("<em>My video</em>", card.OriginalLink);
        var restored = string.Concat(parts.Select(part => part.Html + part.Card?.OriginalLink));
        Assert.Equal(new HtmlParser().ParseDocument(projection.Body).Body!.InnerHtml, restored);
        Assert.Equal("Plain content", Assert.Single(ForumVideoRendering.Split(new("Plain content", []))).Html);
    }
}
