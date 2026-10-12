using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumTopicSlugTests(QueenZoneWebApplicationFactory factory)
    : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Theory]
    [InlineData("كيف تختار شركة جرافيك في السعودية؟")]
    [InlineData("中文")]
    [InlineData("<b>中文</b>")]
    [InlineData("!!!")]
    public async Task Non_latin_topics_render_from_generated_api_paths_and_redirect_without_loops(string title)
    {
        var (_, created) = await CreateThreadAsync(title);
        var path = $"/forum/topic/{created.TopicId}/topic";
        using var client = Client();
        var dto = await client.GetFromJsonAsync<ForumTopicDetailDto>($"/api/v1/forum/topics/{created.TopicId}");
        Assert.Equal(path, dto!.DetailPath);
        foreach (var suffix in new[] { "", "/", "/wrong", "/wrong/", "/wrong/page/1", "/topic/page/1" })
        {
            var redirect = await client.GetAsync($"/forum/topic/{created.TopicId}{suffix}");
            Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
            Assert.Equal(path, redirect.Headers.Location?.OriginalString);
        }

        foreach (var suffix in new[] { "", "/" })
        {
            var response = await client.GetAsync(path + suffix);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Contains("Slug regression post", body);
            Assert.Contains(TestSiteConfiguration.CanonicalLink(path), body);
        }
    }

    [Fact]
    public async Task Non_latin_pagination_uses_the_same_slug_and_rejects_invalid_pages()
    {
        var (author, created) = await CreateThreadAsync("中文");
        var writes = factory.Services.GetRequiredService<IForumWriteRepository>();
        for (var i = 0; i < 15; i++)
        {
            await writes.CreatePostAsync(new NewForumPost(created.TopicId, author, "Slug test", $"Page reply {i}", DateTimeOffset.UtcNow.AddSeconds(i)));
        }

        using var client = Client();
        var path = $"/forum/topic/{created.TopicId}/topic/page/2";
        var redirect = await client.GetAsync($"/forum/topic/{created.TopicId}/wrong/page/2");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal(path, redirect.Headers.Location?.OriginalString);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page 2 of 2", html);
        Assert.Contains(TestSiteConfiguration.CanonicalLink(path), html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/forum/topic/{created.TopicId}/topic/page/3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/forum/topic/{created.TopicId}/topic/page/0")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task Existing_ascii_topics_keep_their_canonical_url(string suffix)
    {
        using var client = Client();
        var response = await client.GetAsync("/forum/topic/1002" + suffix);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/forum/topic/1002/ranking-every-studio-album", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/topic")]
    [InlineData("/topic/page/1")]
    [InlineData("/wrong/page/1")]
    public async Task Missing_and_hidden_topics_return_404_before_redirecting(string suffix)
    {
        var (author, created) = await CreateThreadAsync("中文");
        await factory.Services.GetRequiredService<IForumWriteRepository>()
            .HideAuthorForumContentAsync(author, "Slug test");
        using var client = Client();
        foreach (var id in new[] { created.TopicId, int.MaxValue })
        {
            var response = await client.GetAsync($"/forum/topic/{id}{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/forum/topics/{id}")).StatusCode);
        }
    }

    private async Task<(Guid Author, ForumThreadCreateResult Created)> CreateThreadAsync(string title)
    {
        var author = Guid.NewGuid();
        var created = await factory.Services.GetRequiredService<IForumWriteRepository>().CreateThreadAsync(
            new NewForumThread(1, author, "Slug test", title, "<p>Slug regression post</p>", DateTimeOffset.UtcNow));
        return (author, created);
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
