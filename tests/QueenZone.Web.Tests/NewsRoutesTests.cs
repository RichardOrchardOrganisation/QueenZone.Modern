using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class NewsRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>, IClassFixture<WebHostVariantCache>
{
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly WebHostVariantCache variants;

    public NewsRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
    }

    [Fact]
    public async Task HomePageRendersLatestNews()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("Latest news", body);
        Assert.Contains("QueenZone modernisation begins", body);
    }

    [Fact]
    public async Task NewsArchiveRendersPublishedNews()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news");

        Assert.Contains("News archive", body);
        Assert.Contains("/news/1003/queenzone-modernisation-begins", body);
    }

    [Fact]
    public async Task NewsArchivePageOneIncludesCanonicalNewsUrl()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news");

        Assert.Contains(TestSiteConfiguration.CanonicalLink("/news"), body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone news");
        Assert.Contains("Page 1 of 2", body);
        Assert.DoesNotContain("QueenZone news – Page 1", body);
    }

    [Fact]
    public async Task NewsArchivePageTwoRendersNextBatchWithoutRepeatingPageOneItems()
    {
        var client = factory.CreateClient();

        var pageOne = await client.GetStringAsync("/news");
        var pageTwo = await client.GetStringAsync("/news/page/2");

        Assert.Contains("/news/1003/queenzone-modernisation-begins", pageOne);
        Assert.DoesNotContain("/news/1003/queenzone-modernisation-begins", pageTwo);
        Assert.Contains("/news/1005/archive-sample-article-1005", pageTwo);
        Assert.DoesNotContain("/news/1005/archive-sample-article-1005", pageOne);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/news/page/2"), pageTwo);
        TestHtmlAssertions.AssertPageTitle(pageTwo, "QueenZone news – Page 2");
        Assert.Contains(
            "meta name=\"description\" content=\"The latest Queen news and stories from QueenZone - page 2.\"",
            pageTwo);
        Assert.Contains(
            "meta name=\"description\" content=\"The latest Queen news and stories from QueenZone.\"",
            pageOne);
        Assert.Contains(TestSiteConfiguration.NextLink("/news/page/2"), pageOne);
        Assert.DoesNotContain(TestSiteConfiguration.PrevLink("/news"), pageOne);
        Assert.Contains(TestSiteConfiguration.PrevLink("/news"), pageTwo);
        Assert.DoesNotContain(TestSiteConfiguration.NextLink("/news/page/3"), pageTwo);
    }

    [Fact]
    public async Task NewsArchivePageOneRedirectsFromPagedRoute()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/news/page/1");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/news", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OutOfRangeArchivePageReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/news/page/99");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EmptyArchiveShowsMessageAndRejectsLaterPages()
    {
        var client = variants.Get(WebHostVariants.EmptyNews).CreateClient();

        var body = await client.GetStringAsync("/news");
        var response = await client.GetAsync("/news/page/2");

        Assert.Contains("No published news is available yet.", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/news"), body);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HiddenNewsRecordsAreExcludedFromArchive()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news");

        Assert.DoesNotContain("Hidden moderation draft", body);
        Assert.DoesNotContain("/news/9001/", body);
    }

    [Fact]
    public async Task NewsDetailRendersCompletePublishedArticle()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news/1003/queenzone-modernisation-begins");

        Assert.Contains("The first local vertical slice", body);
        Assert.Contains("<strong>ASP.NET Core</strong>", body);
        Assert.Contains("src=\"/ugc/news/sample-crest.jpg\"", body);
        Assert.Contains("alt=\"QueenZone crest\"", body);
        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains(">News<", body);
        Assert.Contains("aria-current=\"page\">QueenZone modernisation begins</span>", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
        Assert.Contains("<time datetime=\"2026-06-11\">", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/news/1003/queenzone-modernisation-begins"), body);
        Assert.Contains("<meta name=\"description\" content=\"The first local vertical slice", body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone modernisation begins | QueenZone news");
    }

    [Fact]
    public async Task NewsArchiveAndDetail_LinkVerifiedSubmitterProfile()
    {
        var submitterMemberId = WebHostVariants.MemberSubmittedNewsSubmitterId;
        var client = variants.Get(WebHostVariants.MemberSubmittedNews).CreateClient();

        var archiveBody = await client.GetStringAsync("/news");
        var detailBody = await client.GetStringAsync("/news/5100/member-submitted-news");

        Assert.Contains($"Submitted by <a class=\"qz-attribution-link\" href=\"/members/{submitterMemberId}\">News Contributor</a>", archiveBody);
        Assert.Contains($"Submitted by <a class=\"qz-attribution-link\" href=\"/members/{submitterMemberId}\">News Contributor</a>", detailBody);
    }

    [Fact]
    public async Task LegacyNews_DoesNotShowUnverifiedSubmitterAttribution()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news/1003/queenzone-modernisation-begins");

        Assert.DoesNotContain("Submitted by", body);
    }

    [Fact]
    public async Task InMemoryNewsRepository_AddsPromotedSuggestionAttribution()
    {
        var memberId = Guid.NewGuid();
        var member = new QueenZone.Data.Entities.MemberAccount
        {
            Id = memberId,
            Email = "in-memory-news@example.com",
            DisplayName = "In-memory Contributor",
        };
        var suggestions = new InMemoryNewsSuggestionRepository(id => id == memberId ? member : null);
        var suggestion = await suggestions.CreateAsync(new NewsSuggestion(
            Guid.NewGuid(),
            memberId,
            "https://example.com/in-memory-news",
            NewsCandidateDedupe.ComputeUrlHash("https://example.com/in-memory-news"),
            "In-memory news",
            null,
            NewsSuggestionStatus.Pending,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
        await suggestions.PromoteAsync(suggestion.Id, 5200, "admin@test.local", null);
        var publishedAt = new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                5200,
                "In-memory news",
                "in-memory-news",
                "Excerpt",
                "Body",
                publishedAt,
                null,
                true,
                publishedAt,
                publishedAt,
                "admin@test.local"),
        ]);

        var repository = new InMemoryNewsRepository(store, suggestions);
        var item = await repository.GetByIdAsync(5200);
        var batched = await repository.GetByIdsAsync([5200, 404]);
        Assert.Empty(await repository.GetByIdsAsync([]));

        Assert.Equal(memberId, item!.SubmitterMemberId);
        Assert.Equal("In-memory Contributor", item.SubmitterDisplayName);
        var batchedItem = Assert.Single(batched);
        Assert.Equal(memberId, batchedItem.SubmitterMemberId);
        Assert.Equal("In-memory Contributor", batchedItem.SubmitterDisplayName);
    }

    [Fact]
    public async Task MissingNewsArticleReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/news/999999/does-not-exist");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HiddenNewsArticleReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/news/9001/hidden-moderation-draft");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NewsDetailRendersSafeSourceLinkAndRejectsUnsafeUrls()
    {
        var client = variants.Get(WebHostVariants.SourceLinkNews).CreateClient();

        var safeBody = await client.GetStringAsync("/news/5001/article-with-source");
        var unsafeBody = await client.GetStringAsync("/news/5002/article-with-unsafe-source");

        Assert.Contains("href=\"https://example.com/original-story\"", safeBody);
        Assert.Contains("rel=\"noopener noreferrer\"", safeBody);
        Assert.Contains(">https://example.com/original-story</a>", safeBody);
        Assert.Contains("Source: <a", safeBody);
        Assert.DoesNotContain("javascript:", unsafeBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"article-source\"", unsafeBody);
    }

    [Fact]
    public async Task NewsDetailSanitizesUnsafeLegacyHtmlInBody()
    {
        var client = variants.Get(WebHostVariants.UnsafeHtmlNews).CreateClient();

        var body = await client.GetStringAsync("/news/5003/unsafe-html-article");

        Assert.DoesNotContain("alert", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Safe <strong>legacy</strong> paragraph</p>", body);
    }

    [Fact]
    public async Task DuplicateLegacyRowsResolveToLatestPublishedDetailWithoutError()
    {
        var client = variants.Get(WebHostVariants.DuplicateLegacyNews).CreateClient();

        var body = await client.GetStringAsync("/news/4242/latest-duplicate-title");

        Assert.Contains("Latest duplicate body", body);
        Assert.DoesNotContain("Older duplicate body", body);
        Assert.DoesNotContain("Older duplicate title", body);
    }

    [Fact]
    public async Task WrongNewsSlugRedirectsToCanonicalSlug()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/news/1003/not-the-right-slug");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/news/1003/queenzone-modernisation-begins", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OldNewsUrlsAreNotSpecialCased()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/process/news_view.aspx?news_id=1003");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NewsArchiveOrdersByCreatedDateDescending()
    {
        var client = variants.Get(WebHostVariants.DateOrderedNews).CreateClient();

        var body = await client.GetStringAsync("/news");
        var dates = Regex.Matches(body, "<time datetime=\"(\\d{4}-\\d{2}-\\d{2})\">")
            .Select(match => DateOnly.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Take(3)
            .ToList();

        Assert.Equal(
            new[] { new DateOnly(2024, 6, 1), new DateOnly(2022, 3, 15), new DateOnly(2020, 1, 1) },
            dates);
        Assert.Contains("Newest article", body);
        Assert.Contains("Middle article", body);
        Assert.Contains("Oldest article", body);
    }

    [Fact]
    public async Task DuplicateLegacyRowsAreDeduplicatedBeforePaging()
    {
        var client = variants.Get(WebHostVariants.DeduplicatedPagingNews).CreateClient();

        var pageOne = await client.GetStringAsync("/news");
        var pageTwo = await client.GetStringAsync("/news/page/2");

        Assert.Contains("Published article 25", pageOne);
        Assert.DoesNotContain("Published article 5", pageOne);
        Assert.Contains("Published article 5", pageTwo);
        Assert.DoesNotContain("Duplicate copy of article 5", pageOne);
        Assert.DoesNotContain("Duplicate copy of article 5", pageTwo);
        Assert.DoesNotContain("Hidden duplicate candidate", pageOne);
        Assert.DoesNotContain("Hidden duplicate candidate", pageTwo);
    }

    [Fact]
    public async Task NewsArchiveRendersPlaceholderThumbnailsWhenNoImage()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news");
        var pageTwo = await client.GetStringAsync("/news/page/2");

        Assert.Contains("qz-news-row__thumb", body);
        Assert.Contains($"src=\"{NewsArticleImage.PlaceholderPath}\"", body);
        Assert.Contains("loading=\"lazy\"", body);
        Assert.Contains("width=\"240\"", body);
        Assert.Contains("height=\"160\"", body);
        Assert.Contains("qz-news-row__thumb", pageTwo);
        Assert.DoesNotContain("blob.core.windows.net", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/ugc/photos/", body);
    }

    [Fact]
    public async Task NewsArchiveRendersUgcThumbnailsThroughArticlesProxy()
    {
        var client = variants.Get(WebHostVariants.UgcThumbnailNews).CreateClient();

        var body = await client.GetStringAsync("/news");

        Assert.Contains("src=\"/ugc/articles/editors/me/hero.webp?size=thumb\"", body);
        Assert.Contains($"src=\"{NewsArticleImage.PlaceholderPath}\"", body);
        Assert.Contains("Article with uploaded image", body);
        Assert.Contains("Article with gallery pick", body);
        Assert.DoesNotContain("/ugc/photos/", body);
        Assert.DoesNotContain("cdn.queenzone.org", body);
        Assert.DoesNotContain("blob.core.windows.net", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("img-hero.jpg", body);
    }

    [Fact]
    public async Task HomePageLatestNewsDoesNotRenderArchiveListingThumbs()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("Latest news", body);
        Assert.DoesNotContain("qz-news-row__thumb", body);
        Assert.DoesNotContain(NewsArticleImage.PlaceholderPath, body);
    }

    [Fact]
    public async Task NewsDetailRendersArticleImageOrPlaceholder()
    {
        var client = variants.Get(WebHostVariants.DetailImageNews).CreateClient();

        var withImageBody = await client.GetStringAsync("/news/6200/detail-with-image");
        var sampleBody = await factory.CreateClient().GetStringAsync("/news/1003/queenzone-modernisation-begins");

        Assert.Contains("src=\"/ugc/articles/editors/me/hero.webp\"", withImageBody);
        Assert.DoesNotContain("?size=thumb", withImageBody);
        Assert.DoesNotContain("img-hero.jpg", withImageBody);
        Assert.DoesNotContain("blob.core.windows.net", withImageBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"src=\"{NewsArticleImage.PlaceholderPath}\"", sampleBody);
        Assert.DoesNotContain("img-hero.jpg", sampleBody);
    }

}
