using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumPromotionRoutesTests : IClassFixture<ForumPromotionWebApplicationFactory>
{
    private readonly ForumPromotionWebApplicationFactory factory;

    public ForumPromotionRoutesTests(ForumPromotionWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/v1/forum/recent-threads?count=2")]
    public async Task LatestTopicPromotion_ExcludesWebsiteBoardAndKeepsGenuineTestTitle(string path)
    {
        using var client = factory.CreateAnonymousClient();
        var body = await client.GetStringAsync(path);

        Assert.DoesNotContain("Reviewer Test Message", body);
        Assert.Contains("Test pressings of Queen II", body);
    }

    [Fact]
    public async Task ForumIndex_ExcludesWebsiteTopicsFromRecentTableButKeepsBoardCard()
    {
        using var client = factory.CreateAnonymousClient();
        var body = await client.GetStringAsync("/forum");
        var recent = TestHtmlAssertions.SingleElement(body, "table").TextContent;

        Assert.DoesNotContain("Reviewer Test Message", recent);
        Assert.Contains("Test pressings of Queen II", recent);
        Assert.Contains(TestHtmlAssertions.Select(body, "a"), link => link.GetAttribute("href") == "/forum/7/queenzone-com");
    }

    [Fact]
    public async Task WebsiteBoardAndTopic_RemainAccessible()
    {
        using var client = factory.CreateAnonymousClient();
        var board = await client.GetStringAsync("/forum/7/queenzone-com");
        Assert.Contains("Reviewer Test Message", board);
        using var response = await client.GetAsync($"/forum/topic/{factory.WebsiteTopicId}/reviewer-test-message");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("App check", await response.Content.ReadAsStringAsync());
    }
}

public sealed class ForumPromotionWebApplicationFactory : QueenZoneWebApplicationFactory
{
    private readonly InMemoryForumRepository repository;

    public ForumPromotionWebApplicationFactory()
    {
        var categories = SampleForumData.CreateSeedCategories().Append(
            new ForumCategoryItem(7, "Queenzone.com", "Website support", 0, null, null, 70)).ToArray();
        var writes = new InMemoryForumWriteRepository();
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        WebsiteTopicId = writes.CreateThreadAsync(new NewForumThread(
            7, Guid.NewGuid(), "Member", "Reviewer Test Message", "App check", now)).GetAwaiter().GetResult().TopicId;
        writes.CreateThreadAsync(new NewForumThread(
            1, Guid.NewGuid(), "Member", "Test pressings of Queen II", "A genuine discussion", now.AddHours(-1))).GetAwaiter().GetResult();
        repository = new InMemoryForumRepository(categories, SampleForumData.CreateSeedStats(), writes);
    }

    public int WebsiteTopicId { get; }

    protected override void ConfigureTestServices(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IForumRepository>(repository)));
}
