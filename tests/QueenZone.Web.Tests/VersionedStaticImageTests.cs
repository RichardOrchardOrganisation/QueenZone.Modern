using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class VersionedStaticImageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public VersionedStaticImageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HomePageVersionsDesignSystemImagesForLongLivedCaching()
    {
        var body = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("img-portrait.webp?v=", body);
        Assert.Contains("img-portrait.jpg?v=", body);
        Assert.Contains("img-crowd.webp?v=", body);
        Assert.Contains("img-studio.webp?v=", body);
        Assert.Contains("crest-white.png?v=", body);
        Assert.Contains("crest-white.webp?v=", body);
    }

    [Fact]
    public async Task HomePageRendersVersionedEraMontageImagesOutsideJsonConfig()
    {
        var body = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("id=\"qz-era-config\"", body);
        Assert.Contains("/assets/eras/queenzone-1999.png?v=", body);
        Assert.Contains("/assets/eras/queenzone-2020.png?v=", body);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(
            body,
            "class=\"qz-hero-archive__screenshot").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            body,
            "class=\"qz-hero-archive__screenshot qz-hero-archive__screenshot--active\""));

        var configMatch = System.Text.RegularExpressions.Regex.Match(
            body,
            "<script type=\"application/json\" id=\"qz-era-config\">(?<json>.*?)</script>");
        Assert.True(configMatch.Success);
        Assert.DoesNotContain("\"img\"", configMatch.Groups["json"].Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArticleDetailPageVersionsHeroImage()
    {
        var body = await factory.CreateClient().GetStringAsync("/articles/101/inside-the-making-of-bohemian-rhapsody");

        Assert.Contains("/design-system/assets/img-hero.jpg?v=", body);
        Assert.Contains("img-hero.webp?v=", body);
        Assert.Contains("type=\"image/webp\"", body);
        Assert.Contains("width=\"2000\"", body);
        Assert.Contains("height=\"1100\"", body);
        Assert.Contains("fetchpriority=\"high\"", body);
    }

    [Fact]
    public async Task HomePageEmitsDimensionsOnDesignSystemImages()
    {
        var body = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("width=\"447\"", body);
        Assert.Contains("height=\"447\"", body);
        Assert.Contains("width=\"1000\"", body);
        Assert.Contains("height=\"1000\"", body);
        Assert.Contains("width=\"787\"", body);
        Assert.Contains("height=\"518\"", body);
        Assert.Contains("fetchpriority=\"high\"", body);
    }
}

public sealed class HomeEraMontageTests
{
    [Fact]
    public void GetSlides_versionsEachEraScreenshotPath()
    {
        var provider = new TestFileVersionProvider();

        var slides = HomeEraMontage.GetSlides(provider, PathString.Empty);

        Assert.Equal(5, slides.Count);
        Assert.All(slides, slide => Assert.Contains("?v=", slide.Img, StringComparison.Ordinal));
        Assert.Equal("/assets/eras/queenzone-1999.png?v=test-hash", slides[0].Img);
    }

    private sealed class TestFileVersionProvider : IFileVersionProvider
    {
        public string AddFileVersionToPath(PathString requestPathBase, string path) => $"{path}?v=test-hash";
    }
}
