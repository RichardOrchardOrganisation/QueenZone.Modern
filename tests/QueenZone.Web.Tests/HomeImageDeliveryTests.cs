using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class HomeImageDeliveryTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public HomeImageDeliveryTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HomePageServesWebpVariantsForBelowTheFoldImages()
    {
        var client = factory.CreateClient();
        var body = await client.GetStringAsync("/");

        Assert.Contains("type=\"image/webp\"", body);
        Assert.Contains("img-portrait.webp?v=", body);
        Assert.Contains("img-crowd.webp?v=", body);
        Assert.Contains("img-studio.webp?v=", body);
        Assert.Contains("loading=\"lazy\"", body);
        Assert.Contains("fetchpriority=\"high\"", body);
    }

    [Fact]
    public async Task HomePageKeepsJpgFallbackForBelowTheFoldImages()
    {
        var client = factory.CreateClient();
        var body = await client.GetStringAsync("/");

        Assert.Contains("img-portrait.jpg?v=", body);
        Assert.Contains("img-crowd.jpg?v=", body);
        Assert.Contains("img-studio.jpg?v=", body);
    }

    [Fact]
    public async Task HomePagePreloadsLocalFontsWithoutGoogleFontDependency()
    {
        var client = factory.CreateClient();
        var body = await client.GetStringAsync("/");

        Assert.Contains("/design-system/fonts/inter-latin-400-700.woff2", body);
        Assert.Contains("/design-system/fonts/cormorant-garamond-latin-300-700.woff2", body);
        Assert.DoesNotContain("fonts.googleapis.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FontTokensUseLocalWoff2Sources()
    {
        var client = factory.CreateClient();
        var css = await client.GetStringAsync("/design-system/tokens/fonts.css");

        Assert.Contains("@font-face", css);
        Assert.Contains("font-display: swap", css);
        Assert.Contains("/design-system/fonts/inter-latin-400-700.woff2", css);
        Assert.Contains("/design-system/fonts/cinzel-latin-400-700.woff2", css);
        Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonDevelopmentPagesUseOneDesignTokenStylesheet()
    {
        var client = factory.CreateClient();
        var body = await client.GetStringAsync("/");

        Assert.Contains("/design-system/tokens/tokens.min.css", body);
        Assert.DoesNotContain("/design-system/tokens/fonts.min.css", body);
        Assert.DoesNotContain("/design-system/tokens/colors.min.css", body);
        Assert.DoesNotContain("/design-system/tokens/typography.min.css", body);
        Assert.DoesNotContain("/design-system/tokens/spacing.min.css", body);
        Assert.DoesNotContain("/design-system/tokens/base.min.css", body);
    }
}
