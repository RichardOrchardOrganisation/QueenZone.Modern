using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class ForumTopicPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumTopicPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ForumTopicPageRendersSeedPosts()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/forum/topic/1002/ranking-every-studio-album");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ranking every studio album", body);
        Assert.DoesNotContain("No posts are available in this thread yet.", body);
        Assert.Contains("brightonrock", body);
        Assert.Contains("A Night at the Opera", body);
        Assert.Contains("<strong>26</strong> posts", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/forum/topic/1002/ranking-every-studio-album"), body);
        Assert.DoesNotContain("Private conversations with other members", body);
    }

    [Fact]
    public async Task ForumTopicPage_MetaDescriptionUsesFirstPostPlainText()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");

        Assert.Contains(
            "meta name=\"description\" content=\"Where would you put A Night at the Opera in the ranking?\"",
            body);
        Assert.DoesNotContain(
            "meta name=\"description\" content=\"Read-only Queenzone forum archive thread in",
            body);
        // Markup from the post body must not appear in the meta attribute.
        Assert.DoesNotContain(
            "meta name=\"description\" content=\"Where would you put <strong>",
            body);
    }

    [Fact]
    public async Task ForumTopicPages_HaveUniqueMetaDescriptionsAcrossThreads()
    {
        var client = factory.CreateClient();

        var ranking = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var guidelines = await client.GetStringAsync("/forum/topic/1001/forum-guidelines");

        var rankingDesc = ExtractMetaDescription(ranking);
        var guidelinesDesc = ExtractMetaDescription(guidelines);

        Assert.False(string.IsNullOrWhiteSpace(rankingDesc));
        Assert.False(string.IsNullOrWhiteSpace(guidelinesDesc));
        Assert.NotEqual(rankingDesc, guidelinesDesc);
        Assert.Contains("A Night at the Opera", rankingDesc);
        Assert.Contains("civil and on-topic", guidelinesDesc);
    }

    private static string ExtractMetaDescription(string html)
    {
        const string marker = "name=\"description\" content=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "meta description not found");
        start += marker.Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start);
        return html[start..end];
    }

    [Fact]
    public async Task ForumTopicPageTwoIncludesPagination()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album/page/2");

        Assert.Contains("Page 2 of 2", body);
        Assert.Contains("Archive reply 1125", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/forum/topic/1002/ranking-every-studio-album/page/2"), body);
    }

    [Fact]
    public async Task ForumTopicPageRedirectsPageOneToCanonicalTopicUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/forum/topic/1002/ranking-every-studio-album/page/1");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/forum/topic/1002/ranking-every-studio-album", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ForumTopicPageReturnsNotFoundForMissingTopic()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/forum/topic/9999/missing-thread");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForumTopicPageRendersAttachmentLink()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");

        Assert.Contains("/forum/attachment/legacy/1002", body);
        Assert.Contains("anoto-setlist-scan.jpg", body);
        Assert.Contains("JPG", body);
        Assert.Contains("278.0 KB", body);
        Assert.DoesNotContain("cdn.queenzone.org/attachments/", body);
    }
}
