using System.Net;
using System.Net.Http.Json;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class NewsArticleDiscussionPreviewTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private const int SeedTopicId = 1002;

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory discussionHost;

    public NewsArticleDiscussionPreviewTests(
        QueenZoneWebApplicationFactory factory,
        WebHostVariantCache variants)
    {
        this.factory = factory;
        discussionHost = variants.Get(WebHostVariants.IsolatedNewsDiscussion);
    }

    public Task InitializeAsync() => discussionHost.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Detail_WithReplies_RendersPreviewAndJoinInvite()
    {
        var postedAt = new DateTime(2026, 8, 1, 11, 30, 0, DateTimeKind.Utc);
        var html = await GetDetailHtmlAsync(
            Article(6101, "Article with replies", topicId: SeedTopicId),
            replyCount: 3,
            [
                new NewsDiscussionPreview("Alice", postedAt.AddHours(-1), "First preview excerpt"),
                new NewsDiscussionPreview("Bob", postedAt, "Latest preview excerpt"),
            ]);

        Assert.Contains("Join the discussion", html);
        Assert.DoesNotContain("Start the discussion", html);
        Assert.Contains("Alice", html);
        Assert.Contains("Bob", html);
        Assert.Contains("First preview excerpt", html);
        Assert.Contains("Latest preview excerpt", html);
        Assert.Contains("01 August 2026 11:30", html);
        Assert.Contains($"/forum/topic/{SeedTopicId}/", html);

        var bodyIndex = html.IndexOf("Published discussion body.", StringComparison.Ordinal);
        var inviteIndex = html.IndexOf("Join the discussion", StringComparison.Ordinal);
        var shareIndex = html.IndexOf(">Share<", StringComparison.Ordinal);
        Assert.True(bodyIndex >= 0 && inviteIndex > bodyIndex, "Discussion invite should follow the article body.");
        Assert.True(shareIndex > inviteIndex, "Discussion invite should precede the share chrome.");
    }

    [Fact]
    public async Task Detail_WithTopicAndNoReplies_RendersStartInvite()
    {
        var html = await GetDetailHtmlAsync(
            Article(6102, "Article awaiting replies", topicId: SeedTopicId),
            replyCount: 0,
            []);

        Assert.Contains("Start the discussion", html);
        Assert.DoesNotContain("Join the discussion", html);
        Assert.DoesNotContain("Alice", html);
        Assert.Contains($"/forum/topic/{SeedTopicId}/", html);
    }

    [Fact]
    public async Task Detail_WithoutTopicId_OmitsDiscussionBlock()
    {
        var html = await GetDetailHtmlAsync(
            Article(6103, "Legacy article without topic", topicId: null),
            replyCount: 0,
            [
                new NewsDiscussionPreview("Should not render", DateTime.UtcNow, "Hidden excerpt"),
            ]);

        Assert.DoesNotContain("Start the discussion", html);
        Assert.DoesNotContain("Join the discussion", html);
        Assert.DoesNotContain("Should not render", html);
        Assert.DoesNotContain("/forum/topic/", html);
    }

    [Fact]
    public async Task SeededArticleWithoutTopic_OmitsDiscussionInvite()
    {
        var html = await factory.CreateAnonymousClient().GetStringAsync("/news/1003/queenzone-modernisation-begins");

        Assert.Contains("QueenZone modernisation begins", html);
        Assert.DoesNotContain("Start the discussion", html);
        Assert.DoesNotContain("Join the discussion", html);
    }

    [Fact]
    public async Task SignedOutReader_CanReadPreviewAndOpenRealTopic()
    {
        var html = await GetDetailHtmlAsync(
            Article(6104, "Signed out discussion article", topicId: SeedTopicId),
            replyCount: 1,
            [
                new NewsDiscussionPreview("Only", new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc), "Sole reply excerpt"),
            ]);

        Assert.Contains("Only", html);
        Assert.Contains("Sole reply excerpt", html);
        Assert.Contains("Join the discussion", html);

        var topicHref = ExtractTopicHref(html);
        Assert.StartsWith($"/forum/topic/{SeedTopicId}/", topicHref, StringComparison.Ordinal);

        using var client = factory.CreateAnonymousClient();
        var topicPage = await client.GetStringAsync(topicHref);
        Assert.Contains("Ranking every studio album", topicPage);
        Assert.Contains("Sign in to reply", topicPage);
    }

    [Fact]
    public async Task DetailAndApi_WhenDiscussionTopicIsMissing_Return200WithoutPreview()
    {
        var item = Article(6106, "Orphan forum topic article", topicId: 1175833020);
        discussionHost.SeedableNews!.Seed(item);

        using var client = discussionHost.CreateAnonymousClient();
        using var htmlResponse = await client.GetAsync(NewsRoutes.GetNewsDetailPath(item));
        using var apiResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/news/{item.Id}");

        Assert.Equal(HttpStatusCode.OK, htmlResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("Orphan forum topic article", html);
        Assert.DoesNotContain("Start the discussion", html);
        Assert.DoesNotContain("Join the discussion", html);
        Assert.DoesNotContain("/forum/topic/", html);

        var detail = await apiResponse.Content.ReadFromJsonAsync<NewsDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(item.Id, detail!.Id);
        Assert.Null(detail.TopicId);
        Assert.Null(detail.DiscussionReplyCount);
        Assert.Null(detail.DiscussionPreview);
    }

    [Fact]
    public async Task DetailAndApi_WhenDiscussionLookupThrows_Return200WithoutPreview()
    {
        var item = Article(6107, "Discussion lookup timeout article", topicId: 1175833020);
        discussionHost.SeedableNews!.Seed(item);
        discussionHost.SeedableDiscussion!.GetDiscussionException =
            new TimeoutException("command timeout");

        using var client = discussionHost.CreateAnonymousClient();
        using var htmlResponse = await client.GetAsync(NewsRoutes.GetNewsDetailPath(item));
        using var apiResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/news/{item.Id}");

        Assert.Equal(HttpStatusCode.OK, htmlResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("Discussion lookup timeout article", html);
        Assert.DoesNotContain("Start the discussion", html);
        Assert.DoesNotContain("Join the discussion", html);
        Assert.DoesNotContain("/forum/topic/", html);

        var detail = await apiResponse.Content.ReadFromJsonAsync<NewsDetailDto>();
        Assert.NotNull(detail);
        Assert.Null(detail!.TopicId);
        Assert.Null(detail.DiscussionReplyCount);
        Assert.Null(detail.DiscussionPreview);
    }

    [Fact]
    public async Task HomeAndNewsList_DoNotRenderDiscussionInvite()
    {
        var item = Article(6105, "List teaser must stay off", topicId: SeedTopicId);
        SeedDiscussion(item, replyCount: 2, [
            new NewsDiscussionPreview("List", DateTime.UtcNow, "Must not appear on cards"),
        ]);
        using var client = discussionHost.CreateAnonymousClient();

        var home = await client.GetStringAsync("/");
        var list = await client.GetStringAsync("/news");

        Assert.Contains("List teaser must stay off", home);
        Assert.Contains("List teaser must stay off", list);
        Assert.DoesNotContain("Start the discussion", home);
        Assert.DoesNotContain("Join the discussion", home);
        Assert.DoesNotContain("Start the discussion", list);
        Assert.DoesNotContain("Join the discussion", list);
        Assert.DoesNotContain("Must not appear on cards", home);
        Assert.DoesNotContain("Must not appear on cards", list);
    }

    private async Task<string> GetDetailHtmlAsync(
        NewsItem item,
        int replyCount,
        IReadOnlyList<NewsDiscussionPreview> preview)
    {
        SeedDiscussion(item, replyCount, preview);
        using var client = discussionHost.CreateAnonymousClient();
        return await client.GetStringAsync(NewsRoutes.GetNewsDetailPath(item));
    }

    private void SeedDiscussion(
        NewsItem item,
        int replyCount,
        IReadOnlyList<NewsDiscussionPreview> preview)
    {
        discussionHost.SeedableNews!.Seed(item);
        discussionHost.SeedableDiscussion!.Seed(item.ForumTopicId, replyCount, preview);
    }

    private static NewsItem Article(int id, string title, int? topicId) =>
        new(
            id,
            title,
            "Discussion excerpt.",
            "Published discussion body.",
            new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            null,
            true,
            ForumTopicId: topicId);

    private static string ExtractTopicHref(string html)
    {
        const string marker = ">Join the discussion</a>";
        var end = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(end >= 0, "Expected a Join the discussion link.");
        var hrefStart = html.LastIndexOf("href=\"", end, StringComparison.Ordinal);
        Assert.True(hrefStart >= 0, "Expected href on the discussion invite.");
        hrefStart += "href=\"".Length;
        var hrefEnd = html.IndexOf('"', hrefStart);
        return html[hrefStart..hrefEnd];
    }
}
