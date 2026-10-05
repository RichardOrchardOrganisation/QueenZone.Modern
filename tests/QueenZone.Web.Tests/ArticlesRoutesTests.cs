using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ArticlesRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>, IClassFixture<WebHostVariantCache>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly WebHostVariantCache variants;

    public ArticlesRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
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
        Assert.DoesNotContain("Community articles", pageOne);
        Assert.DoesNotContain(">Archive<", pageOne);
        Assert.Contains("class=\"qz-news-row\"", pageOne);
        Assert.Contains("class=\"qz-news-row\"", pageTwo);
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
        var client = variants.Get(WebHostVariants.EmptyArticles).CreateClient();

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
        var client = variants.Get(WebHostVariants.OverlayImageArticle).CreateClient();

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
        var client = variants.Get(WebHostVariants.SourceLinkArticles).CreateClient();

        var linkedBody = await client.GetStringAsync("/articles/5001/article-with-source-link");
        var attributedBody = await client.GetStringAsync("/articles/5002/article-with-attribution");

        Assert.Contains("href=\"https://example.com/original-story\"", linkedBody);
        Assert.Contains("Queen Magazine", attributedBody);
        Assert.DoesNotContain("href=\"Queen Magazine\"", attributedBody);
    }

    [Fact]
    public async Task ArticleDetailSanitizesUnsafeLegacyHtmlInBody()
    {
        var client = variants.Get(WebHostVariants.UnsafeHtmlArticle).CreateClient();

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
        var client = variants.Get(WebHostVariants.DateOrderedArticles).CreateClient();

        var body = await client.GetStringAsync("/articles");
        var dates = Regex.Matches(body, "<time datetime=\"(\\d{4}-\\d{2}-\\d{2})\">")
            .Select(match => DateOnly.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Take(3)
            .ToList();

        Assert.Equal(
            new[] { new DateOnly(2024, 6, 1), new DateOnly(2022, 3, 15), new DateOnly(2020, 1, 1) },
            dates);
    }

    [Fact]
    public async Task ArticlesArchive_RendersArchiveStyleForCommunityAndArchiveItems()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.DoesNotContain("Community articles", body);
        Assert.DoesNotContain(">Archive<", body);
        Assert.Contains("class=\"qz-news-row\"", body);
        Assert.Contains(">Recording<", body);
        Assert.Contains("href=\"/articles/101/inside-the-making-of-bohemian-rhapsody\"", body);
    }
}
