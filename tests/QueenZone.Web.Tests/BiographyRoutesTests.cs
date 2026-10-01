using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class BiographyRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>, IClassFixture<WebHostVariantCache>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly WebHostVariantCache variants;

    public BiographyRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
    }

    [Fact]
    public async Task BiographyIndexRendersChaptersInDisplaySequenceDescendingOrder()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("Biography", body);
        Assert.Contains("/biography/5/1992", body);
        Assert.Contains("/biography/1/1946-1969", body);
        Assert.Contains("5 chapters", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/biography"), body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone biography");

        var newestIndex = body.IndexOf("/biography/5/1992", StringComparison.Ordinal);
        var oldestIndex = body.IndexOf("/biography/1/1946-1969", StringComparison.Ordinal);
        Assert.True(newestIndex >= 0);
        Assert.True(oldestIndex > newestIndex);
    }

    [Fact]
    public async Task BiographyIndexFallsBackToBodyExcerptWhenSummaryIsEmpty()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("When the band Smile lost its singer", body);
    }

    [Fact]
    public async Task BiographyIndexStripsHtmlFromLegacySummary()
    {
        var client = variants.Get(WebHostVariants.HtmlSummaryBiography).CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("A founding chapter summary with HTML.", body);
        Assert.DoesNotContain("&lt;p&gt;", body);
        Assert.DoesNotContain("&lt;strong&gt;", body);
    }

    [Fact]
    public async Task BiographyDetailRendersChapterBodyAndNavigation()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/2/1970");

        Assert.Contains("1970", body);
        Assert.Contains("Killer Queen", body);
        Assert.Contains("Previous Chapter", body);
        Assert.Contains("Next Chapter", body);
        Assert.Contains("/biography/1/1946-1969", body);
        Assert.Contains("/biography/3/1975", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/biography/2/1970"), body);
        TestHtmlAssertions.AssertPageTitle(body, "1970 | QueenZone biography");
    }

    [Fact]
    public async Task BiographyDetailReusesOneCachedListForNavigationAcrossRequests()
    {
        var isolated = variants.Get(WebHostVariants.CountingBiography);
        await isolated.ResetAsync();
        var repository = isolated.CountingBiography!;
        using var client = isolated.CreateAnonymousClient();

        var first = await client.GetStringAsync("/biography/2/second");
        var listCallsAfterFirst = repository.ListCallCount;
        var second = await client.GetStringAsync("/biography/2/second");

        Assert.Contains("Previous Chapter", first);
        Assert.Contains("Previous Chapter", second);
        Assert.InRange(listCallsAfterFirst, 1, 2);
        Assert.Equal(listCallsAfterFirst, repository.ListCallCount);
        Assert.True(repository.DetailCallCount >= 2);
        Assert.Equal(0, repository.AdjacentCallCount);
    }

    [Fact]
    public async Task FirstChapterHidesPreviousNavigation()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/1/1946-1969");

        Assert.DoesNotContain("Previous Chapter", body);
        Assert.Contains("Next Chapter", body);
    }

    [Fact]
    public async Task LastChapterHidesNextNavigation()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/5/1992");

        Assert.Contains("Previous Chapter", body);
        Assert.DoesNotContain("Next Chapter", body);
    }

    [Fact]
    public async Task MissingBiographyChapterReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/biography/999999/does-not-exist");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WrongBiographySlugRedirectsToCanonicalSlug()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/biography/2/not-the-right-slug");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/biography/2/1970", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task BiographyDetailSanitizesUnsafeLegacyHtmlInBody()
    {
        var client = variants.Get(WebHostVariants.UnsafeHtmlBiography).CreateClient();

        var body = await client.GetStringAsync("/biography/7001/2026");

        var articleStart = body.IndexOf("<article class=\"article-body\">", StringComparison.Ordinal);
        Assert.True(articleStart >= 0);
        var articleBody = body[articleStart..];
        Assert.DoesNotContain("alert", articleBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Safe <strong>legacy</strong> paragraph</p>", articleBody);
    }

    [Fact]
    public async Task EmptyBiographyShowsMessage()
    {
        var client = variants.Get(WebHostVariants.EmptyBiography).CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("No biography chapters are available yet.", body);
    }

    [Fact]
    public async Task HomePageIncludesBiographyArchiveCard()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("aria-label=\"Explore the archive\"", body);
        Assert.Contains("<a href=\"/biography\">Biography</a>", body);
    }

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(10, "X")]
    public void GetChapterNumeral_ReturnsExpectedRomanNumerals(int index, string expected)
    {
        Assert.Equal(expected, BiographyRoutes.GetChapterNumeral(index - 1));
    }

    [Theory]
    [InlineData("1946 - 1969", "1946–1969")]
    [InlineData("1946 – 1969", "1946–1969")]
    [InlineData("1970", "1970")]
    [InlineData("1992", "1992")]
    public void GetYearMarker_ParsesLegacyTitleYears(string title, string expected)
    {
        Assert.Equal(expected, BiographyTitle.GetYearMarker(title));
    }

}
