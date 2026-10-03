using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumLegacyPostRedirectPageTests(QueenZoneWebApplicationFactory factory)
    : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Fact]
    public async Task Topic_id_redirects_to_canonical_topic_page()
    {
        var response = await Client().GetAsync("/forum/goto/1002");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forum/topic/1002/ranking-every-studio-album", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Reply_id_redirects_to_its_page_and_anchor()
    {
        var sixteenth = SampleForumData.CreateSeedPosts(1002).OrderBy(post => post.PostedAt).ElementAt(15);

        var response = await Client().GetAsync($"/forum/goto/{sixteenth.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"/forum/topic/1002/ranking-every-studio-album/page/2#post-{sixteenth.Id}",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unknown_id_returns_not_found()
    {
        var response = await Client().GetAsync("/forum/goto/987654321");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Following_the_redirect_lands_on_the_reply()
    {
        var body = await factory.CreateClient().GetStringAsync("/forum/goto/1101");

        Assert.Contains("id=\"post-1101\"", body);
    }

    private HttpClient Client() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
