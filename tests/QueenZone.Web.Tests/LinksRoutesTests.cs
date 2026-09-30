using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class LinksRoutesTests : IClassFixture<WebHostVariantCache>
{
    private readonly WebHostVariantCache variants;

    public LinksRoutesTests(WebHostVariantCache variants)
    {
        this.variants = variants;
    }

    [Fact]
    public async Task LinksPageRendersAvailableLinksByCategory()
    {
        var client = variants.Get(WebHostVariants.OfficialQueenOnlineLinks).CreateClient();

        var body = await client.GetStringAsync("/links");

        Assert.Contains("Queen Links", body);
        Assert.Contains("Official", body);
        Assert.Contains("Queen Online", body);
        Assert.Contains("href=\"https://www.queenonline.com/\"", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/links"), body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone links");
    }

    [Fact]
    public async Task LinksPageHidesUnavailableLinksAndEmptyCategories()
    {
        var client = variants.Get(WebHostVariants.HiddenUnavailableLinks).CreateClient();

        var body = await client.GetStringAsync("/links");

        Assert.Contains("Queen Online", body);
        Assert.DoesNotContain("Missing Site", body);
        Assert.DoesNotContain("Dead Category", body);
        Assert.DoesNotContain("Dead Only", body);
    }

    [Fact]
    public async Task LinksPageShowsEmptyMessageWhenNoLinksSurviveAvailabilityCheck()
    {
        var client = variants.Get(WebHostVariants.DeadOnlyLinks).CreateClient();

        var body = await client.GetStringAsync("/links");

        Assert.Contains("No checked Queen links are available yet.", body);
        Assert.DoesNotContain("Dead Only", body);
    }

    [Fact]
    public async Task LinksPageNormalizesBareLegacyUrlsAndDisplaysHost()
    {
        var client = variants.Get(WebHostVariants.BareLegacyUrlLinks).CreateClient();

        var body = await client.GetStringAsync("/links");

        Assert.Contains("href=\"https://www.queenonline.com/\"", body);
        Assert.Contains("www.queenonline.com", body);
    }

    [Fact]
    public async Task LinksPageSkipsMalformedLegacyUrls()
    {
        var client = variants.Get(WebHostVariants.MalformedMailtoLinks).CreateClient();

        var body = await client.GetStringAsync("/links");

        Assert.Contains("No checked Queen links are available yet.", body);
        Assert.DoesNotContain("Malformed", body);
    }
}
