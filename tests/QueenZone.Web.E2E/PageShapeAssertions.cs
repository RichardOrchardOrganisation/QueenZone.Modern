using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Structural page checks (unrendered HTML-encoding artifacts, 390px overflow,
/// actionable browser console errors) shared between the deterministic PR-gating
/// smoke suite (<see cref="SmokeTests"/>, <see cref="CuratedPageLayoutSmokeTests"/>)
/// and the nightly real-data sitemap sweep (<see cref="SitemapPublicRouteSweepTests"/>).
/// Encoding and overflow are hard on curated PR-gate pages and soft on the
/// sampled live-site sweep (#1597).
/// </summary>
internal static class PageShapeAssertions
{
    public const string HorizontalOverflowScript =
        "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1";

    private static readonly Regex EncodingArtifactPattern = new(
        @"&(amp|lt|gt|quot|apos|\#39|nbsp);",
        RegexOptions.Compiled);

    public static Match FindEncodingArtifact(string bodyText) => EncodingArtifactPattern.Match(bodyText);

    // Mirror archives and CDN thumbs commonly 404 without indicating a real regression.
    public static bool IsActionableConsoleError(string message) =>
        !message.Contains("status of 404", StringComparison.OrdinalIgnoreCase);

    public static Task<bool> HasHorizontalOverflowAsync(IPage page) =>
        page.EvaluateAsync<bool>(HorizontalOverflowScript);

    public static async Task AssertNoHorizontalOverflowAsync(IPage page, string context)
    {
        var overflows = await HasHorizontalOverflowAsync(page);
        Assert.That(
            overflows,
            Is.False,
            $"{context} should not require horizontal scrolling on a {CuratedLayoutPages.PhoneWidth}px viewport.");
    }

    public static async Task AssertNoEncodingArtifactsAsync(IPage page)
    {
        var bodyText = await page.Locator("body").InnerTextAsync();
        var match = FindEncodingArtifact(bodyText);
        Assert.That(
            match.Success,
            Is.False,
            $"Unrendered HTML-encoding artifact in visible text: '{match.Value}'");
    }
}
