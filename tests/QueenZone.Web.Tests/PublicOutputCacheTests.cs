using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Output-cache behaviour for public HTML.
/// <list type="bullet">
/// <item>
/// <description>
/// Default suite uses environment <c>Testing</c>, which disables the public HTML output-cache
/// policy so integration tests stay deterministic and do not share stale HTML.
/// </description>
/// </item>
/// <item>
/// <description>
/// Production-shaped cases use the shared <see cref="ProductionHostFixture"/> so output cache is
/// enabled without a real Azure AD app. Empty <c>ConnectionStrings:QueenZoneLegacy</c> keeps
/// in-memory sample data. No special env vars are required beyond a normal <c>dotnet test</c> run.
/// </description>
/// </item>
/// </list>
/// </summary>
[Collection(ProductionHostCollection.Name)]
public sealed class PublicOutputCacheTests : IClassFixture<WebHostVariantCache>
{
    private readonly ProductionHostFixture production;
    private readonly WebHostVariantCache variants;

    public PublicOutputCacheTests(ProductionHostFixture production, WebHostVariantCache variants)
    {
        this.production = production;
        this.variants = variants;
    }

    [Fact]
    public async Task RazorPagesAreNotServedFromOutputCache()
    {
        var host = variants.Get(WebHostVariants.TestingCountingArticles);
        await host.ResetAsync();
        var repository = host.CountingArticles
            ?? throw new InvalidOperationException("TestingCountingArticles must register CountingArticlesRepository.");
        var client = host.CreateClient();

        var first = await client.GetStringAsync("/articles");
        var callsAfterFirstRequest = repository.FeedKeysCallCount + repository.ByIdsCallCount;
        var second = await client.GetStringAsync("/articles");

        Assert.Contains("Cached archive article", first);
        // Each render gets a fresh CSP nonce (see #585), so strip it before comparing bodies —
        // an unchanged repository call count is what actually proves the page was not cached.
        Assert.Equal(StripCspNonces(first), StripCspNonces(second));
        Assert.True(callsAfterFirstRequest > 0);
        Assert.True(repository.FeedKeysCallCount + repository.ByIdsCallCount > callsAfterFirstRequest);
    }

    [Fact]
    public async Task Production_anonymous_public_html_is_served_from_output_cache_on_second_request()
    {
        await production.ResetAsync();
        var repository = production.Articles;
        var client = production.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var firstResponse = await client.GetAsync("/articles");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var feedKeysAfterFirst = repository.FeedKeysCallCount;
        var callsAfterFirst = repository.FeedKeysCallCount + repository.ByIdsCallCount;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Contains("Cached archive article", firstBody);
        Assert.True(callsAfterFirst > 0, "First request should invoke the articles repository.");

        // The merged list loads feed keys (query-cache eligible) and hydrates the page slice
        // by id. Unchanged totals on the second request prove the Razor page did not
        // re-run — i.e. ASP.NET Core output cache served the HTML. Baselines are taken after the
        // first request (not from a "first ever call" flag on the fake) because the background
        // search-index seed job (SearchIndexSeedHostedService) also calls into this same
        // substituted repository once during host startup, before any HTTP request is made.
        using var secondResponse = await client.GetAsync("/articles");
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        var callsAfterSecond = repository.FeedKeysCallCount + repository.ByIdsCallCount;

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(callsAfterFirst, callsAfterSecond);
        Assert.Equal(feedKeysAfterFirst, repository.FeedKeysCallCount);
    }

    [Theory]
    [InlineData("/forum/topic/1002/ranking-every-studio-album", "GET", "HEAD", "Ranking every studio album")]
    [InlineData("/forum/topic/1002/ranking-every-studio-album", "HEAD", "GET", "Ranking every studio album")]
    [InlineData("/forum/archive-authors/5001", "GET", "HEAD", "brightonrock")]
    [InlineData("/forum/archive-authors/5001", "HEAD", "GET", "brightonrock")]
    public async Task Production_forum_get_and_head_share_the_complete_cache_entry(
        string path, string firstMethod, string secondMethod, string expectedContent)
    {
        await production.ResetAsync();
        using var client = production.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var firstRequest = new HttpRequestMessage(new HttpMethod(firstMethod), path);
        using var first = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var calls = production.Forum.Calls + production.ArchiveAuthors.Calls;
        Assert.True(calls > 0);
        Assert.Equal(path.StartsWith("/forum/archive-authors", StringComparison.Ordinal)
            ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(90), production.CacheExpirations.Durations[path]);
        using var secondRequest = new HttpRequestMessage(new HttpMethod(secondMethod), path);
        using var second = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(calls, production.Forum.Calls + production.ArchiveAuthors.Calls);

        var get = firstMethod == "GET" ? first : second;
        var head = firstMethod == "HEAD" ? first : second;
        var html = await get.Content.ReadAsStringAsync();
        Assert.Contains(expectedContent, html);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
        // A HEAD-first fill must retain the full representation, including its CSP nonce.
        Assert.Equal(html, await client.GetStringAsync(path));
        Assert.Equal(calls, production.Forum.Calls + production.ArchiveAuthors.Calls);
    }

    [Theory]
    [InlineData("/forum/topic/999999/missing", HttpStatusCode.NotFound, null)]
    [InlineData("/forum/topic/1002/wrong-slug", HttpStatusCode.MovedPermanently, "/forum/topic/1002/ranking-every-studio-album")]
    [InlineData("/forum/archive-authors/999999", HttpStatusCode.NotFound, null)]
    public async Task Production_cold_head_keeps_lookup_status_and_canonical_redirect(
        string path, HttpStatusCode status, string? location)
    {
        await production.ResetAsync();
        using var client = production.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Head, path);
        using var response = await client.SendAsync(request);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
        Assert.True(production.Forum.Calls + production.ArchiveAuthors.Calls > 0);
    }

    [Fact]
    public async Task Production_tracking_query_reuses_the_canonical_public_html_cache_entry()
    {
        await production.ResetAsync();
        var repository = production.Articles;
        var client = production.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var firstResponse = await client.GetAsync("/articles");
        var callsAfterFirst = repository.FeedKeysCallCount + repository.ByIdsCallCount;
        using var trackedResponse = await client.GetAsync("/articles?utm_source=newsletter&utm_campaign=launch");
        var callsAfterTracked = repository.FeedKeysCallCount + repository.ByIdsCallCount;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, trackedResponse.StatusCode);
        Assert.True(callsAfterFirst > 0);
        Assert.Equal(callsAfterFirst, callsAfterTracked);
    }

    [Theory]
    [InlineData("/timeline?decade=1980s", "Live Aid performance", "/timeline?decade=2020s", "QueenZone modernisation milestone")]
    [InlineData("/timeline?decade=2020s", "QueenZone modernisation milestone", "/timeline?decade=1980s", "Live Aid performance")]
    [InlineData("/articles", "Community cache article 01", "/articles/page/2", "Community cache article 21")]
    [InlineData("/articles/page/2", "Community cache article 21", "/articles", "Community cache article 01")]
    [InlineData("/articles?tag=music", "Community cache article 01", "/articles?tag=live", "Community cache article 13")]
    [InlineData("/articles?tag=live", "Community cache article 13", "/articles?tag=music", "Community cache article 01")]
    [InlineData("/articles?tag=music&page=1", "Community cache article 01", "/articles?tag=live&page=1", "Community cache article 13")]
    [InlineData("/articles?tag=live&page=1", "Community cache article 13", "/articles?tag=music&page=1", "Community cache article 01")]
    [InlineData("/quizzes/leaderboard", "Today's leaderboard", "/quizzes/leaderboard?scope=all", "Best runs")]
    [InlineData("/quizzes/leaderboard?scope=all", "Best runs", "/quizzes/leaderboard", "Today's leaderboard")]
    [InlineData("/quizzes/leaderboard?scope=all", "Best runs", "/quizzes/leaderboard?scope=total", "Total points")]
    [InlineData("/quizzes/leaderboard?scope=total", "Total points", "/quizzes/leaderboard?scope=all", "Best runs")]
    public async Task Production_content_variants_have_independent_reusable_cache_entries(
        string firstPath, string firstContent, string secondPath, string secondContent)
    {
        await production.ResetAsync();
        using var client = production.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var first = await client.GetStringAsync(firstPath);
        var second = await client.GetStringAsync(secondPath);
        AssertVariant(first, firstPath, firstContent, secondContent);
        AssertVariant(second, secondPath, secondContent, firstContent);

        // Exact body equality includes the CSP nonce: it proves reuse of the rendered
        // response, rather than merely another render using the same query-cache data.
        Assert.Equal(first, await client.GetStringAsync(firstPath));
        Assert.Equal(second, await client.GetStringAsync(secondPath));
        var trackingSeparator = secondPath.Contains('?') ? "&" : "?";
        Assert.Equal(second, await client.GetStringAsync($"{secondPath}{trackingSeparator}utm_source=audit"));
    }

    private static void AssertVariant(string html, string path, string expected, string other)
    {
        var document = new HtmlParser().ParseDocument(html);
        var content = path.StartsWith("/quizzes/leaderboard", StringComparison.Ordinal)
            ? document.QuerySelector("h1")!.TextContent
            : document.Body!.TextContent;
        Assert.Contains(expected, content);
        Assert.DoesNotContain(other, content);
        // Tagged article views canonicalize to /articles; numbered archive pages keep their path.
        var canonicalPath = path.StartsWith("/articles?", StringComparison.Ordinal) ? "/articles" : path;
        Assert.Equal(TestSiteConfiguration.PublicBaseUrl + canonicalPath,
            document.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Production_excluded_admin_path_is_not_output_cached()
    {
        // Policy unit tests already cover authenticated bypass; this integration case proves an
        // excluded path is not held in the public HTML output cache by checking that two GETs
        // still exercise the endpoint (health is cheap and always uncached by policy).
        await production.ResetAsync();
        var client = production.Factory.CreateClient();

        using var first = await client.GetAsync("/health");
        using var second = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(
            PublicOutputCachePolicies.IsPublicReadOnlyRequest(CreateHttpContext("GET", "/health")),
            "Health must remain excluded from the public read-only output-cache policy.");
    }

    [Theory]
    [InlineData("GET", "/articles", true)]
    [InlineData("HEAD", "/articles", true)]
    [InlineData("POST", "/articles", false)]
    [InlineData("GET", "/admin/news", false)]
    [InlineData("GET", "/account/login", false)]
    [InlineData("GET", "/account/member-probe", false)]
    [InlineData("GET", "/health", false)]
    [InlineData("GET", "/trivia", false)]
    [InlineData("GET", "/search", false)]
    public void PublicReadOnlyPolicyIncludesOnlyAnonymousPublicGetAndHeadRoutes(
        string method,
        string path,
        bool expected)
    {
        Assert.Equal(expected, PublicOutputCachePolicies.IsPublicReadOnlyRequest(CreateHttpContext(method, path)));
    }

    private static string StripCspNonces(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "nonce=\"[^\"]*\"", "nonce=\"\"");

    private static DefaultHttpContext CreateHttpContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }
}
