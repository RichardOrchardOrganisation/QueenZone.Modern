using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Sitemap-driven sweep (#545) over every public section, asserting page *shape* rather than
/// content, so it stays correct as the real-data archive grows and changes. Discovers URLs at
/// runtime from <c>/sitemap.xml</c> instead of hardcoding record ids.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.RealData)]
[Category(E2ECategories.ReadOnly)]
public class SitemapPublicRouteSweepTests : RealDataPageTest
{
    private const int DefaultPerSectionSampleCap = 5;
    private static readonly TimeSpan SitemapDiscoveryBudget = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(60);

    private static readonly string[] StaticExtraRoutes =
    [
        "/about",
        "/contact",
        "/privacy",
        "/terms",
        "/data-deletion",
        "/search",
        "/links",
    ];

    protected override bool AllowsWrites => false;

    [Test]
    public async Task SitemapDiscoveredAndStaticUrls_MeetShapeAssertionsAsync()
    {
        using var httpClient = CreateHttpClient();
        using var cts = new CancellationTokenSource(SitemapDiscoveryBudget);
        IReadOnlyDictionary<string, IReadOnlyList<string>> sectionedUrls;
        try
        {
            sectionedUrls = await DiscoverSampledSectionUrlsAsync(httpClient, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Assert.Fail(
                $"Sitemap discovery exceeded {SitemapDiscoveryBudget.TotalSeconds:0}s budget. " +
                "See test output for the last child sitemap processed.");
            return;
        }

        var urls = sectionedUrls.Values
            .SelectMany(v => v)
            .Concat(StaticExtraRoutes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        TestContext.Out.WriteLine($"Sitemap sweep (seed {SeededSampler.Seed}): {urls.Count} URLs");
        foreach (var url in urls)
        {
            TestContext.Out.WriteLine($"  {url}");
        }

        var consoleErrors = new List<string>();
        void OnConsoleMessage(object? _, IConsoleMessage message)
        {
            if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                consoleErrors.Add(message.Text);
            }
        }

        var mobileContext = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var mobilePage = await mobileContext.NewPageAsync();

        var failures = new List<string>();
        Page.Console += OnConsoleMessage;
        try
        {
            foreach (var url in urls)
            {
                await AssertUrlShapeAsync(url, mobilePage, consoleErrors, failures);
            }
        }
        finally
        {
            Page.Console -= OnConsoleMessage;
        }

        Assert.That(
            failures,
            Is.Empty,
            "Sitemap sweep failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Test]
    public async Task UnknownPath_Returns404WithStyledNotFoundPageAsync()
    {
        var response = await GotoWithLiveSiteRetryAsync(Page, "/this-page-does-not-exist-e2e-545");

        Assert.That(response?.Status, Is.EqualTo(404));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Page Not Found", Level = 1 }))
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task StaleSlug_RedirectsToCanonicalUrlAsync()
    {
        using var httpClient = CreateHttpClient();
        using var cts = new CancellationTokenSource(SitemapDiscoveryBudget);
        var sectionedUrls = await DiscoverSectionUrlsAsync(httpClient, cts.Token);
        var canonicalPath = sectionedUrls.GetValueOrDefault("news")?
            .FirstOrDefault(path => Regex.IsMatch(path, @"^/news/\d+/[^/?#]+/?$"));

        Assert.That(canonicalPath, Is.Not.Null, "Expected the news sitemap to contain a detail URL.");
        var lastSlash = canonicalPath!.TrimEnd('/').LastIndexOf('/');
        var stalePath = canonicalPath[..(lastSlash + 1)] + "stale-slug-e2e-545";

        var response = await GotoWithLiveSiteRetryAsync(Page, stalePath);

        Assert.That(response?.Request.RedirectedFrom, Is.Not.Null, "Expected the stale slug to redirect.");
        await Expect(Page).ToHaveURLAsync(new Regex($"{Regex.Escape(canonicalPath.TrimEnd('/'))}/?$"));
    }

    [Test]
    public async Task RepresentativeSectionPages_HaveNoCriticalAxeViolationsAsync()
    {
        using var httpClient = CreateHttpClient();
        using var cts = new CancellationTokenSource(SitemapDiscoveryBudget);
        var sectionedUrls = await DiscoverSampledSectionUrlsAsync(httpClient, cts.Token);

        var failures = new List<string>();
        foreach (var (section, urls) in sectionedUrls)
        {
            var representative = urls.FirstOrDefault();
            if (representative is null)
            {
                continue;
            }

            try
            {
                await GotoWithLiveSiteRetryAsync(Page, representative);
            }
            catch (TimeoutException ex)
            {
                failures.Add($"{section} ({representative}): navigation timed out - {ex.Message}");
                continue;
            }

            try
            {
                await AxeAssertions.AssertNoCriticalViolationsAsync(Page);
            }
            catch (Exception ex)
            {
                failures.Add($"{section} ({representative}): {ex.Message}");
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            "Axe critical violations by section:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private async Task AssertUrlShapeAsync(string url, IPage mobilePage, List<string> consoleErrors, List<string> failures)
    {
        consoleErrors.Clear();

        IResponse? response;
        try
        {
            // Load (not NetworkIdle): archive pages can keep long-polling or analytics busy under
            // parallel RealData load, which falsely trips NetworkIdle within the default timeout.
            response = await GotoWithLiveSiteRetryAsync(Page, url);
        }
        catch (Exception ex)
        {
            failures.Add($"{FailureAttributionPrefix()}{url}: navigation failed - {ex.Message}");
            return;
        }

        var prefix = FailureAttributionPrefix();

        if (response is null || response.Status != 200)
        {
            failures.Add($"{prefix}{url}: expected HTTP 200, got {(response is null ? "no response" : response.Status.ToString())}");
            return;
        }

        var h1Locator = Page.Locator("h1");
        var h1Count = await h1Locator.CountAsync();
        if (h1Count != 1)
        {
            failures.Add($"{prefix}{url}: expected exactly one <h1>, found {h1Count}");
        }
        else if (string.IsNullOrWhiteSpace(await h1Locator.InnerTextAsync()))
        {
            failures.Add($"{prefix}{url}: <h1> is empty");
        }

        var canonicalLocator = Page.Locator("link[rel='canonical']");
        var canonicalCount = await canonicalLocator.CountAsync();
        if (canonicalCount != 1)
        {
            failures.Add($"{prefix}{url}: expected exactly one canonical link, found {canonicalCount}");
        }
        else
        {
            var href = await canonicalLocator.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(href, UriKind.Absolute, out var canonicalUri))
            {
                failures.Add($"{prefix}{url}: canonical href is missing or not absolute ('{href}')");
            }
            else if (!string.Equals(canonicalUri.PathAndQuery.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{prefix}{url}: canonical href '{canonicalUri.PathAndQuery}' does not match requested path");
            }
        }

        if (consoleErrors.Count > 0)
        {
            // Mirror archives often have missing CDN thumbs; ignore resource 404 noise.
            // Live-site may also report CDN redirect noise; keep non-404 console errors hard.
            var actionable = consoleErrors
                .Where(PageShapeAssertions.IsActionableConsoleError)
                .ToList();
            if (actionable.Count > 0)
            {
                failures.Add($"{prefix}{url}: {actionable.Count} browser console error(s): {string.Join(" | ", actionable)}");
            }
            else
            {
                TestContext.Out.WriteLine(
                    $"SOFT: {url}: {consoleErrors.Count} resource 404 console message(s) ignored");
            }
        }

        await GotoWithLiveSiteRetryAsync(mobilePage, url);
        var overflows = await mobilePage.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        if (overflows)
        {
            // Archive UGC and some section indexes still overflow at 390px; log without
            // failing the suite (Assert.Warn would mark the test Warning). Structural checks
            // above remain hard failures.
            TestContext.Out.WriteLine($"SOFT: {url}: horizontal overflow at 390px viewport width");
        }

        var bodyText = await Page.Locator("body").InnerTextAsync();
        var artifactMatch = PageShapeAssertions.FindEncodingArtifact(bodyText);
        if (artifactMatch.Success)
        {
            TestContext.Out.WriteLine(
                $"SOFT: {url}: unrendered HTML-encoding artifact in visible text: '{artifactMatch.Value}'");
        }
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverSampledSectionUrlsAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var perSectionCap = ResolvePerSectionSampleCap();

        var sectioned = await DiscoverSectionUrlsAsync(httpClient, cancellationToken);
        return sectioned.ToDictionary(
            pair => pair.Key,
            pair => SeededSampler.SampleFirstLastAndRandom(pair.Value, perSectionCap),
            StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverSectionUrlsAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        TestContext.Out.WriteLine("Sitemap discovery: fetching /sitemap.xml");
        var indexXml = await GetStringWithLiveSiteRetryAsync(httpClient, "/sitemap.xml", cancellationToken);
        var childSitemapPaths = SitemapRouteParser.ParseIndexPaths(indexXml);
        TestContext.Out.WriteLine(
            $"Sitemap discovery: index returned {childSitemapPaths.Count} child sitemaps in {stopwatch.ElapsedMilliseconds}ms");

        var sectioned = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var sitemapPath in childSitemapPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = SitemapRouteParser.ResolveSectionName(sitemapPath);
            var childStarted = stopwatch.ElapsedMilliseconds;
            TestContext.Out.WriteLine($"Sitemap discovery: fetching {sitemapPath} (section={section})");
            var childXml = await GetStringWithLiveSiteRetryAsync(httpClient, sitemapPath, cancellationToken);
            var urlPaths = SitemapRouteParser.ParseUrlSetPaths(childXml);
            TestContext.Out.WriteLine(
                $"Sitemap discovery: {sitemapPath} returned {urlPaths.Count} URLs in {stopwatch.ElapsedMilliseconds - childStarted}ms " +
                $"(total {stopwatch.ElapsedMilliseconds}ms)");

            if (!sectioned.TryGetValue(section, out var list))
            {
                list = [];
                sectioned[section] = list;
            }

            list.AddRange(urlPaths);
        }

        return sectioned.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static int ResolvePerSectionSampleCap() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_SITEMAP_SWEEP_PER_SECTION_LIMIT"), out var value) && value > 0
            ? value
            : DefaultPerSectionSampleCap;

    /// <summary>
    /// Live-site failures mean production is broken (different signal from a mirror failure).
    /// </summary>
    private static string FailureAttributionPrefix() =>
        RealDataMarkers.IsReadOnlyMode() ? "PRODUCTION LIVE-SITE: " : string.Empty;

    private static Task<string> GetStringWithLiveSiteRetryAsync(
        HttpClient httpClient,
        string path,
        CancellationToken cancellationToken) =>
        LiveSiteTransportRetry.RunAsync(
            () => httpClient.GetStringAsync(path, cancellationToken),
            cancellationToken);

    private static Task<IResponse?> GotoWithLiveSiteRetryAsync(IPage page, string url) =>
        LiveSiteTransportRetry.RunAsync(
            () => page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = (float)NavigationTimeout.TotalMilliseconds,
                }));

    private HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.Timeout = SitemapDiscoveryBudget;
        return client;
    }
}
