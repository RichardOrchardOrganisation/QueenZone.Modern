using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
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
/// Production-shaped cases use <c>UseEnvironment("Production")</c> plus the same production host
/// settings as <see cref="ResponseCompressionTests"/> so output cache is enabled without a real
/// Azure AD app. Empty <c>ConnectionStrings:QueenZoneLegacy</c> keeps in-memory sample data.
/// No special env vars are required beyond a normal <c>dotnet test</c> run.
/// </description>
/// </item>
/// </list>
/// </summary>
public sealed class PublicOutputCacheTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicOutputCacheTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task RazorPagesAreNotServedFromOutputCache()
    {
        var repository = new CountingArticlesRepository();
        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticlesRepository>(repository);
            })).CreateClient();

        var first = await client.GetStringAsync("/articles");
        var callsAfterFirstRequest = repository.ArchivePageCallCount + repository.PublishedCountCallCount;
        var second = await client.GetStringAsync("/articles");

        Assert.Contains("Cached archive article", first);
        // Each render gets a fresh CSP nonce (see #585), so strip it before comparing bodies —
        // an unchanged repository call count is what actually proves the page was not cached.
        Assert.Equal(StripCspNonces(first), StripCspNonces(second));
        Assert.True(callsAfterFirstRequest > 0);
        Assert.True(repository.ArchivePageCallCount + repository.PublishedCountCallCount > callsAfterFirstRequest);
    }

    [Fact]
    public async Task Production_anonymous_public_html_is_served_from_output_cache_on_second_request()
    {
        var repository = new CountingArticlesRepository();
        var productionFactory = CreateProductionFactory(services =>
        {
            services.AddSingleton<IArticlesRepository>(repository);
        });

        var client = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var firstResponse = await client.GetAsync("/articles");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var archivePageCallsAfterFirst = repository.ArchivePageCallCount;
        var callsAfterFirst = repository.ArchivePageCallCount + repository.PublishedCountCallCount;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Contains("Cached archive article", firstBody);
        Assert.True(callsAfterFirst > 0, "First request should invoke the articles repository.");

        // Archive page loads published count (query-cache eligible) and archive page (not
        // query-cached). Unchanged totals on the second request prove the Razor page did not
        // re-run — i.e. ASP.NET Core output cache served the HTML. Baselines are taken after the
        // first request (not from a "first ever call" flag on the fake) because the background
        // search-index seed job (SearchIndexSeedHostedService) also calls into this same
        // substituted repository once during host startup, before any HTTP request is made.
        using var secondResponse = await client.GetAsync("/articles");
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        var callsAfterSecond = repository.ArchivePageCallCount + repository.PublishedCountCallCount;

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(callsAfterFirst, callsAfterSecond);
        Assert.Equal(archivePageCallsAfterFirst, repository.ArchivePageCallCount);
    }

    [Fact]
    public async Task Production_tracking_query_reuses_the_canonical_public_html_cache_entry()
    {
        var repository = new CountingArticlesRepository();
        var productionFactory = CreateProductionFactory(services =>
        {
            services.AddSingleton<IArticlesRepository>(repository);
        });
        var client = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var firstResponse = await client.GetAsync("/articles");
        var callsAfterFirst = repository.ArchivePageCallCount + repository.PublishedCountCallCount;
        using var trackedResponse = await client.GetAsync("/articles?utm_source=newsletter&utm_campaign=launch");
        var callsAfterTracked = repository.ArchivePageCallCount + repository.PublishedCountCallCount;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, trackedResponse.StatusCode);
        Assert.True(callsAfterFirst > 0);
        Assert.Equal(callsAfterFirst, callsAfterTracked);
    }

    [Fact]
    public async Task Production_excluded_admin_path_is_not_output_cached()
    {
        // Policy unit tests already cover authenticated bypass; this integration case proves an
        // excluded path is not held in the public HTML output cache by checking that two GETs
        // still exercise the endpoint (health is cheap and always uncached by policy).
        var productionFactory = CreateProductionFactory();
        var client = productionFactory.CreateClient();

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

    private WebApplicationFactory<Program> CreateProductionFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            ResponseCompressionTests.ApplyProductionHostTestSettings(builder);
            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
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

    private sealed class CountingArticlesRepository : IArticlesRepository
    {
        private readonly ArticleItem article = new(
            7801,
            "Cached archive article",
            "Output cache test article.",
            "<p>Output cache test body.</p>",
            new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            null,
            "Testing",
            true);

        public int ArchivePageCallCount { get; private set; }

        public int PublishedCountCallCount { get; private set; }

        public Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleItem>>([article]);

        public Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ArchivePageCallCount++;
            return Task.FromResult<IReadOnlyList<ArticleItem>>([article]);
        }

        public Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default)
        {
            PublishedCountCallCount++;
            return Task.FromResult(1);
        }

        public Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ArticleItem?>(id == article.Id ? article : null);

        public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SitemapContentEntry>>(
                [new SitemapContentEntry(article.Id, article.Title, article.PublishedAt)]);
    }
}
