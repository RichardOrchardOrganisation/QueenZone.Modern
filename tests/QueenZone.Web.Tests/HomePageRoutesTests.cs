using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed class HomePageRoutesTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory throwingSprint;

    public HomePageRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        throwingSprint = variants.Get(WebHostVariants.ThrowingSprintBoardQuiz);
    }

    private static readonly Regex HomepageHeading = new(
        @"<h1\b[^>]*>\s*QueenZone: Queen news, community and archive\s*</h1>",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    [Fact]
    public async Task Home_returns_200_with_a_single_homepage_heading()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        using var home = await client.GetAsync("/");
        var homeHtml = await home.Content.ReadAsStringAsync();
        using var about = await client.GetAsync("/about");
        using var quizzes = await client.GetAsync("/quizzes");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Single(Regex.Matches(homeHtml, @"<h1\b", RegexOptions.IgnoreCase));
        Assert.Matches(HomepageHeading, homeHtml);

        Assert.Equal(HttpStatusCode.OK, about.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, quizzes.StatusCode);
        Assert.Equal("/quizzes/sprint", quizzes.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Home_leads_with_live_content_and_keeps_the_archive_and_apps()
    {
        using var client = factory.CreateAnonymousClient();

        var html = await client.GetStringAsync("/");

        // New content first: live strip, news, forum, gallery, articles, sprint.
        Assert.Contains("data-home-ticker", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Latest news</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Forum now", html, StringComparison.Ordinal);
        Assert.Contains("Just added to the gallery", html, StringComparison.Ordinal);
        Assert.Contains("Live from the forum", html, StringComparison.Ordinal);
        Assert.Contains("Articles &amp; features", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/quizzes/sprint\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Weekly quiz", html, StringComparison.OrdinalIgnoreCase);

        // The front page opens with the newest photos and a quiz callout, not a lead news story.
        var gallery = html.IndexOf("Just added to the gallery", StringComparison.Ordinal);
        var quiz = html.IndexOf("id=\"quiz-callout\"", StringComparison.Ordinal);
        var latestNews = html.IndexOf("<h2>Latest news</h2>", StringComparison.Ordinal);
        Assert.True(gallery > 0 && gallery < quiz && quiz < latestNews);

        // The apps call to action sits at the top, straight under the live strip and above the front page.
        var apps = html.IndexOf("Explore the mobile apps", StringComparison.Ordinal);
        Assert.InRange(apps, html.IndexOf("data-home-ticker", StringComparison.Ordinal), html.IndexOf("id=\"news\"", StringComparison.Ordinal));

        // One Queen quote sits in the right column under Forum now, inside the front page.
        var quotes = html.IndexOf("id=\"home-quotes-heading\"", StringComparison.Ordinal);
        Assert.InRange(quotes, html.IndexOf("Forum now", StringComparison.Ordinal), html.IndexOf("Live from the forum", StringComparison.Ordinal));
        Assert.Single(Regex.Matches(html, "class=\"qz-home-quotes__item\""));

        // The Queenzone history montage moves below the fold as a section heading.
        Assert.Matches(
            new Regex(@"<h2\b[^>]*>\s*Twenty-five years of the Queen internet zone\s*</h2>"),
            html);
        Assert.True(html.IndexOf("id=\"qz-hero-archive\"", StringComparison.Ordinal) > html.IndexOf("id=\"articles\"", StringComparison.Ordinal));

        // On This Day follows the front page, ahead of the forum band; the quiz has one callout only.
        var onThisDay = html.IndexOf("id=\"home-onthisday-heading\"", StringComparison.Ordinal);
        Assert.InRange(onThisDay, latestNews, html.IndexOf("Live from the forum", StringComparison.Ordinal));
        Assert.DoesNotContain("The sixty-second Queen quiz", html, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/js/home-live-ticker.js?v=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_offers_public_downloads_for_both_mobile_platforms()
    {
        using var client = factory.CreateAnonymousClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("href=\"https://apps.apple.com/au/app/queenzone-org/id6803889011\"", html);
        Assert.Contains("href=\"https://play.google.com/store/apps/details?id=org.queenzone.mobile\"", html);
        Assert.Contains("Download QueenZone for iPhone on the App Store", html);
        Assert.Contains("Download QueenZone for Android on Google Play", html);
        Assert.Contains("href=\"/mobile-apps\"", html);
        Assert.DoesNotContain("Android testing", html);
        Assert.DoesNotContain("play.google.com/apps/testing", html);
        Assert.DoesNotContain("groups.google.com/g/queenzone-mobile", html);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(20, "20 min ago")]
    [InlineData(180, "3 hr ago")]
    [InlineData(60 * 24, "1 day ago")]
    [InlineData(60 * 48, "2 days ago")]
    [InlineData(60 * 24 * 10, "22 Sep 2026")]
    [InlineData(-30, "just now")]
    public void Relative_time_matches_the_mobile_home_wording(int minutesAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, HomeRelativeTime.Format(now.UtcDateTime.AddMinutes(-minutesAgo), now));
    }

    [Fact]
    public void Forum_activity_inside_fifteen_minutes_counts_as_live()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.True(HomeRelativeTime.IsLive(now.UtcDateTime.AddMinutes(-14), now));
        Assert.False(HomeRelativeTime.IsLive(now.UtcDateTime.AddMinutes(-15), now));
    }

    [Fact]
    public void Home_and_quizzes_index_use_distinct_page_models()
    {
        _ = factory.CreateAnonymousClient();

        var pages = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<CompiledPageActionDescriptor>())
            .Where(descriptor => descriptor is not null)
            .Select(descriptor => descriptor!)
            .ToList();

        var homeModel = Assert.Single(
            pages
                .Where(page => page.RelativePath == "/Pages/Index.cshtml")
                .Select(page => page.ModelTypeInfo!.AsType())
                .Distinct());
        var quizzesModel = Assert.Single(
            pages
                .Where(page => page.RelativePath == "/Pages/Quizzes/Index.cshtml")
                .Select(page => page.ModelTypeInfo!.AsType())
                .Distinct());

        Assert.Equal(typeof(QueenZone.Web.Pages.IndexModel), homeModel);
        Assert.Equal(typeof(QueenZone.Web.Pages.Quizzes.QuizzesIndexModel), quizzesModel);
        Assert.NotEqual(homeModel, quizzesModel);
    }

    [Fact]
    public async Task Home_still_returns_200_when_the_sprint_board_fails()
    {
        using var client = throwingSprint.CreateAnonymousClient();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Regex.Matches(html, @"<h1\b", RegexOptions.IgnoreCase));
        Assert.Matches(HomepageHeading, html);
        Assert.Contains("How well do you know Queen?", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/quizzes/sprint\"", html, StringComparison.Ordinal);
    }
}
