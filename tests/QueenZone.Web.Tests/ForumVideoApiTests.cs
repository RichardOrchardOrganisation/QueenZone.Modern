using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumVideoApiTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ForumVideoApiTests(QueenZoneWebApplicationFactory factory) => this.factory = factory;

    [Fact]
    public async Task Metadata_is_additive_sanitized_and_described_in_openapi_without_storage_changes()
    {
        const string body = """<p>Before</p><p><a href="http://www.youtube.com/watch?v=M7lc1UVf-VE&amp;t=1m30s">Video</a></p><iframe src="https://evil.example"></iframe><script>alert(1)</script><p>After</p>""";
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var created = await repository.CreateThreadAsync(new NewForumThread(1, Guid.NewGuid(), "Video Fan", "Video contract", body, DateTimeOffset.UtcNow));
        using var client = factory.CreateAnonymousClient();
        var payload = await client.GetFromJsonAsync<ApiPagedResponse<ForumPostDto>>($"/api/v1/forum/topics/{created.TopicId}/posts");
        var post = Assert.Single(payload!.Items);
        Assert.Equal(scope.ServiceProvider.GetRequiredService<UgcHtml>().FormatForDisplay(body), post.Body);
        Assert.DoesNotContain("iframe", post.Body);
        Assert.DoesNotContain("script", post.Body);
        var video = Assert.Single(post.YoutubeVideos!);
        Assert.Equal(90, video.StartSeconds);
        Assert.Equal("Video", new HtmlParser().ParseDocument(post.Body).QuerySelectorAll("a")[video.AnchorIndex].TextContent);
        Assert.Equal(body, (await repository.GetPostAsync(created.StarterPostId))!.Body);

        // A consumer that only reads old fields still sees the original readable link.
        var oldClient = await client.GetFromJsonAsync<JsonElement>($"/api/v1/forum/topics/{created.TopicId}/posts");
        Assert.Contains("www.youtube.com/watch", oldClient.GetProperty("items")[0].GetProperty("body").GetString());
        var archive = await client.GetFromJsonAsync<ApiPagedResponse<ForumPostDto>>("/api/v1/forum/topics/1002/posts");
        Assert.All(archive!.Items, item => Assert.Empty(item.YoutubeVideos!));

        var openApi = await client.GetFromJsonAsync<JsonElement>(ApiV1.OpenApiPath);
        var schemas = openApi.GetProperty("components").GetProperty("schemas");
        var postSchema = schemas.GetProperty(nameof(ForumPostDto));
        Assert.True(postSchema.GetProperty("properties").TryGetProperty("youtubeVideos", out _));
        var descriptor = schemas.GetProperty(nameof(ForumYoutubeVideo)).GetProperty("properties");
        foreach (var field in new[] { "provider", "videoId", "watchUrl", "startSeconds", "anchorIndex" })
        {
            Assert.True(descriptor.TryGetProperty(field, out _));
        }
    }
}
