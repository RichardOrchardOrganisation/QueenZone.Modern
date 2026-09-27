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
        @"<h1\b[^>]*>\s*Twenty-five years of the Queen internet zone\s*</h1>",
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
        Assert.Contains("The sixty-second Queen quiz", html, StringComparison.Ordinal);
        Assert.Contains("No scores yet today.", html, StringComparison.Ordinal);
    }
}
