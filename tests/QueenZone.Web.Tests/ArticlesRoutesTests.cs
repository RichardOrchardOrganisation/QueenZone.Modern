using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ArticlesRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ArticlesRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ArticlesArchiveRendersPublishedArticles()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.Contains("Articles", body);
        Assert.Contains("/articles/101/inside-the-making-of-bohemian-rhapsody", body);
    }

    [Fact]
    public async Task ArticlesArchivePageOneIncludesCanonicalArticlesUrl()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.Contains(TestSiteConfiguration.CanonicalLink("/articles"), body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone articles");
        Assert.Contains("Page 1 of 2", body);
    }

    [Fact]
    public async Task ArticlesArchivePageTwoRendersNextBatchWithoutRepeatingPageOneItems()
    {
        var client = factory.CreateClient();

        var pageOne = await client.GetStringAsync("/articles");
        var pageTwo = await client.GetStringAsync("/articles/page/2");

        Assert.Contains("/articles/101/inside-the-making-of-bohemian-rhapsody", pageOne);
        Assert.DoesNotContain("/articles/101/inside-the-making-of-bohemian-rhapsody", pageTwo);
        Assert.Contains("/articles/121/archive-sample-article-121", pageTwo);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/articles/page/2"), pageTwo);
        Assert.Contains(TestSiteConfiguration.PrevLink("/articles"), pageTwo);
        Assert.Contains(
            "meta name=\"description\" content=\"In-depth Queen articles and interviews from the Queenzone.com archive.\"",
            pageOne);
        Assert.Contains(
            "meta name=\"description\" content=\"In-depth Queen articles and interviews from the Queenzone.com archive - page 2.\"",
            pageTwo);
    }

    [Fact]
    public async Task ArticlesArchivePageOneRedirectsFromPagedRoute()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/articles/page/1");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/articles", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OutOfRangeArchivePageReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/articles/page/99");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EmptyArchiveShowsMessageAndRejectsLaterPages()
    {
        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticlesRepository>(new InMemoryArticlesRepository([]));
            })).CreateClient();

        var body = await client.GetStringAsync("/articles");
        var response = await client.GetAsync("/articles/page/2");

        Assert.Contains("No published articles are available yet.", body);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HiddenArticleRecordsAreExcludedFromArchive()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.DoesNotContain("Hidden moderation draft", body);
        Assert.DoesNotContain("/articles/9001/", body);
    }

    [Fact]
    public async Task ArticleDetailRendersOverlayImageAuthorAndTags()
    {
        var editorial = new InMemoryEditorialArticleRepository();
        var articles = new QueenZone.Data.InMemoryArticlesRepository(
            [
                new ArticleItem(
                    5004,
                    "Legacy archive title",
                    "Legacy excerpt.",
                    "<p>Legacy body.</p>",
                    new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc),
                    null,
                    "Features",
                    true),
            ],
            editorial);
        var draft = await editorial.SaveDraftAsync(
            new EditorialArticleDraft(
                null,
                5004,
                null,
                "Overlay archive title",
                null,
                "Overlay excerpt.",
                "<p>Overlay body.</p>",
                "Overlay Author",
                "Features",
                "overlay,tags",
                null,
                "editors/admin/overlay.webp",
                DateTimeOffset.Parse("2026-05-04T09:00:00Z")),
            "admin");
        await editorial.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticlesRepository>(articles);
            })).CreateClient();

        var body = await client.GetStringAsync("/articles/5004/overlay-archive-title");

        Assert.Contains("Overlay archive title", body);
        Assert.Contains("Overlay Author", body);
        Assert.Contains("overlay", body);
        Assert.Contains("tags", body);
        Assert.Contains(NewsArticleImage.ResolveImageUrl("editors/admin/overlay.webp", null)!, body);
        Assert.DoesNotContain("/design-system/assets/img-hero.jpg", body);
    }

    [Fact]
    public async Task ArticleDetailRendersCompletePublishedArticle()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles/101/inside-the-making-of-bohemian-rhapsody");

        Assert.Contains("Six weeks, three studios", body);
        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains("href=\"/articles\"", body);
        Assert.Contains(">Articles<", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
        Assert.Contains("Recording", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/articles/101/inside-the-making-of-bohemian-rhapsody"), body);
        TestHtmlAssertions.AssertPageTitle(body, "Inside the Making of Bohemian Rhapsody | QueenZone articles");
    }

    [Fact]
    public async Task MissingArticleReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/articles/999999/does-not-exist");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HiddenArticleReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/articles/9001/hidden-moderation-draft");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArticleDetailRendersSafeSourceLinkAndPlainTextAttribution()
    {
        var items = new[]
        {
            new ArticleItem(
                5001,
                "Article with source link",
                "Excerpt with source.",
                "<p>Published body.</p>",
                new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
                "https://example.com/original-story",
                "Features",
                true),
            new ArticleItem(
                5002,
                "Article with attribution",
                "Attribution excerpt.",
                "<p>Published body.</p>",
                new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc),
                "Queen Magazine",
                "Features",
                true)
        };

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticlesRepository>(new InMemoryArticlesRepository(items));
            })).CreateClient();

        var linkedBody = await client.GetStringAsync("/articles/5001/article-with-source-link");
        var attributedBody = await client.GetStringAsync("/articles/5002/article-with-attribution");

        Assert.Contains("href=\"https://example.com/original-story\"", linkedBody);
        Assert.Contains("Queen Magazine", attributedBody);
        Assert.DoesNotContain("href=\"Queen Magazine\"", attributedBody);
    }

    [Fact]
    public async Task ArticleDetailSanitizesUnsafeLegacyHtmlInBody()
    {
        var items = new[]
        {
            new ArticleItem(
                5003,
                "Unsafe HTML article",
                "Unsafe excerpt.",
                "<script>alert('xss')</script><p>Safe <strong>legacy</strong> paragraph</p>",
                new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc),
                null,
                null,
                true)
        };

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticlesRepository>(new InMemoryArticlesRepository(items));
            })).CreateClient();

        var body = await client.GetStringAsync("/articles/5003/unsafe-html-article");

        Assert.DoesNotContain("alert", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Safe <strong>legacy</strong> paragraph</p>", body);
    }

    [Fact]
    public async Task WrongArticleSlugRedirectsToCanonicalSlug()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/articles/101/not-the-right-slug");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/articles/101/inside-the-making-of-bohemian-rhapsody", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OldArticleUrlsAreNotSpecialCased()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/process/article_show.aspx?q=101");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArticlesArchiveOrdersByCreatedDateDescending()
    {
        var items = new[]
        {
            new ArticleItem(3001, "Oldest article", "Oldest excerpt.", "<p>Oldest body.</p>", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, null, true),
            new ArticleItem(3002, "Newest article", "Newest excerpt.", "<p>Newest body.</p>", new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), null, null, true),
            new ArticleItem(3003, "Middle article", "Middle excerpt.", "<p>Middle body.</p>", new DateTime(2022, 3, 15, 0, 0, 0, DateTimeKind.Utc), null, null, true)
        };

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticlesRepository>(new InMemoryArticlesRepository(items));
            })).CreateClient();

        var body = await client.GetStringAsync("/articles");
        var dates = Regex.Matches(body, "<time datetime=\"(\\d{4}-\\d{2}-\\d{2})\">")
            .Select(match => DateOnly.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Take(3)
            .ToList();

        Assert.Equal(
            new[] { new DateOnly(2024, 6, 1), new DateOnly(2022, 3, 15), new DateOnly(2020, 1, 1) },
            dates);
    }
}
