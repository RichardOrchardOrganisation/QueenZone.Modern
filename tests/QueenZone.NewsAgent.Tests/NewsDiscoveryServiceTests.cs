using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsDiscoveryServiceTests
{
    [Fact]
    public async Task RunFetchAsync_creates_candidates_idempotently_from_rss_fixture()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var feedUrl = "https://www.queenonline.com/feed/";
        var httpClient = new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
        {
            [feedUrl] = NewsAgentTestSupport.ReadFixture("sample-rss.xml")
        });
        var service = NewsAgentTestSupport.CreateDiscoveryService(repository, httpClient);
        var sourceId = await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "queen-online",
            "Queen Online",
            "https://www.queenonline.com/",
            feedUrl,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var firstRun = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));
        var secondRun = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));

        Assert.Equal(1, firstRun.SourcesChecked);
        Assert.Equal(2, firstRun.ItemsFetched);
        Assert.Equal(2, firstRun.CandidatesCreated);
        Assert.Equal(0, firstRun.DuplicatesSkipped);
        Assert.Equal(2, secondRun.DuplicatesSkipped);
        Assert.Equal(0, secondRun.CandidatesCreated);

        var candidates = await repository.GetCandidatesAsync(sourceId: sourceId);
        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.CanonicalUrl == "https://www.queenonline.com/news/tour-2026");
    }

    [Fact]
    public async Task RunFetchAsync_filters_secondary_source_items_without_keyword_match()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var feedUrl = "https://www.nme.com/feed";
        var httpClient = new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
        {
            [feedUrl] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0"><channel>
                  <item>
                    <title>Generic guitar pedal review</title>
                    <link>https://www.nme.com/music/pedal-review</link>
                    <description>No band mentioned.</description>
                  </item>
                  <item>
                    <title>Queen documentary announced</title>
                    <link>https://www.nme.com/music/queen-documentary</link>
                    <description>Queen feature.</description>
                  </item>
                </channel></rss>
                """
        });
        var service = NewsAgentTestSupport.CreateDiscoveryService(repository, httpClient);
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "nme-music",
            "NME Music",
            "https://www.nme.com/music",
            feedUrl,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Secondary,
            60,
            true,
            "queen,freddie mercury"));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));

        Assert.Equal(2, result.ItemsFetched);
        Assert.Equal(1, result.KeywordFiltered);
        Assert.Equal(1, result.CandidatesCreated);
    }

    [Fact]
    public async Task RunFetchAsync_skips_sources_before_poll_interval_unless_forced()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var feedUrl = "https://www.queenonline.com/feed/";
        var httpClient = new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
        {
            [feedUrl] = NewsAgentTestSupport.ReadFixture("sample-rss.xml")
        });
        var service = NewsAgentTestSupport.CreateDiscoveryService(repository, httpClient);
        var sourceId = await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "queen-online",
            "Queen Online",
            "https://www.queenonline.com/",
            feedUrl,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));
        await repository.MarkSourceFetchedAsync(sourceId, DateTime.UtcNow);

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions());

        Assert.Equal(0, result.SourcesChecked);
        Assert.Equal(1, result.SourcesSkipped);
    }

    [Fact]
    public void SeedSources_produces_structurally_valid_registry()
    {
        var sources = NewsDiscoverySourceSeeder.LoadSeedSources();

        Assert.NotEmpty(sources);
        Assert.Equal(sources.Count, sources.Select(source => source.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var source in sources)
        {
            Assert.False(string.IsNullOrWhiteSpace(source.Key), "Source key must be non-empty.");
            Assert.False(string.IsNullOrWhiteSpace(source.DisplayName), $"Display name for '{source.Key}' must be non-empty.");
            Assert.True(
                Uri.TryCreate(source.HomepageUrl, UriKind.Absolute, out var homepage) &&
                (homepage.Scheme == Uri.UriSchemeHttp || homepage.Scheme == Uri.UriSchemeHttps),
                $"HomepageUrl for '{source.Key}' must be an absolute http(s) URL.");
            Assert.True(
                Enum.IsDefined(source.SourceType),
                $"SourceType for '{source.Key}' must be a defined enum value.");
            Assert.True(
                Enum.IsDefined(source.TrustTier),
                $"TrustTier for '{source.Key}' must be a defined enum value.");
            Assert.True(
                source.PollIntervalMinutes > 0,
                $"PollIntervalMinutes for '{source.Key}' must be positive.");

            if (!string.IsNullOrWhiteSpace(source.FeedOrSiteUrl))
            {
                Assert.True(
                    Uri.TryCreate(source.FeedOrSiteUrl, UriKind.Absolute, out var feedOrSite) &&
                    (feedOrSite.Scheme == Uri.UriSchemeHttp || feedOrSite.Scheme == Uri.UriSchemeHttps),
                    $"FeedOrSiteUrl for '{source.Key}' must be an absolute http(s) URL when present.");
            }
        }
    }

    /// <summary>
    /// Data-coupled regression guard for known registry entries.
    /// Update deliberately when source keys, types, or URLs change in news-discovery-sources.json.
    /// </summary>
    [Fact]
    public void SeedSources_data_regression_contains_expected_known_keys()
    {
        var sources = NewsDiscoverySourceSeeder.LoadSeedSources();

        Assert.Contains(sources, source => source.Key == "queen-online" && source.SourceType == NewsDiscoverySourceType.AllowlistedPage);
        Assert.Contains(sources, source => source.Key == "roger-taylor" && source.SourceType == NewsDiscoverySourceType.Rss);
        Assert.Contains(sources, source => source.Key == "brian-may" && source.FeedOrSiteUrl == "https://brianmay.com/feed/");
        Assert.Contains(sources, source => source.Key == "freddie-mercury-official" && source.SourceType == NewsDiscoverySourceType.AllowlistedPage && source.TrustTier == NewsDiscoveryTrustTier.Primary);
        Assert.Contains(sources, source => source.Key == "mercury-phoenix-trust" && source.FeedOrSiteUrl == "https://www.mercuryphoenixtrust.org/site/news");
        Assert.Contains(sources, source => source.Key == "queen-extravaganza" && source.SourceType == NewsDiscoverySourceType.AllowlistedPage);
        Assert.Contains(sources, source => source.Key == "queen-fan-club" && source.SourceType == NewsDiscoverySourceType.AllowlistedPage);
        Assert.Contains(sources, source => source.Key == "google-news-freddie-mercury" && source.SourceType == NewsDiscoverySourceType.Rss);
        Assert.Contains(sources, source => source.Key == "google-news-queen-adam-lambert" && source.SourceType == NewsDiscoverySourceType.Rss);
        Assert.Contains(sources, source => source.Key == "bbc-entertainment" && source.FeedOrSiteUrl == "https://feeds.bbci.co.uk/news/entertainment_and_arts/rss.xml");
        Assert.Contains(sources, source => source.Key == "nme-queen" && source.SourceType == NewsDiscoverySourceType.AllowlistedPage);
        Assert.Contains(sources, source => source.Key == "udiscover-music" && source.TrustTier == NewsDiscoveryTrustTier.Secondary);
        Assert.Contains(sources, source => source.Key == "official-charts" && source.FeedOrSiteUrl == "https://www.officialcharts.com/chart-news/");
        Assert.Contains(sources, source => source.Key == "nme-music" && source.TrustTier == NewsDiscoveryTrustTier.Secondary);
        Assert.Contains(sources, source => source.Key == "musicradar" && source.SourceType == NewsDiscoverySourceType.Rss && source.FeedOrSiteUrl == "https://www.musicradar.com/feeds.xml");
        Assert.Contains(sources, source => source.Key == "gold-radio" && !source.Enabled);
        Assert.Contains(
            sources,
            source => source.Key == "planet-rock"
                && source.SourceType == NewsDiscoverySourceType.AllowlistedPage
                && source.TrustTier == NewsDiscoveryTrustTier.Secondary
                && source.Enabled
                && source.HomepageUrl == "https://www.hellorayo.co.uk/planet-rock"
                && source.FeedOrSiteUrl == "https://www.hellorayo.co.uk/planet-rock/news/rock-news");
        Assert.Contains(sources, source => source.Key == "music-news" && !source.Enabled);
        Assert.Contains(sources, source => source.Key == "youtube-queen-official" && !source.Enabled);
    }

    [Fact]
    public async Task SeedAsync_upserts_embedded_registry_sources()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var expectedSourceCount = NewsDiscoverySourceSeeder.LoadSeedSources().Count;

        var seeded = await NewsDiscoverySourceSeeder.SeedAsync(repository);
        var storedSources = await repository.GetSourcesAsync();

        Assert.Equal(expectedSourceCount, seeded);
        Assert.Equal(expectedSourceCount, storedSources.Count);
        Assert.NotNull(await repository.GetSourceByKeyAsync("queen-online"));
    }

    [Fact]
    public async Task RunFetchAsync_with_seed_sources_upserts_registry_before_fetch()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var expectedCheckedSources = NewsDiscoverySourceSeeder.LoadSeedSources().Count(source => source.Enabled);
        var service = NewsAgentTestSupport.CreateDiscoveryService(
            repository,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>()));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(SeedSources: true, Force: true));

        Assert.NotNull(await repository.GetSourceByKeyAsync("queen-online"));
        Assert.Equal(expectedCheckedSources, result.SourcesChecked);
        Assert.Equal(expectedCheckedSources, result.Failures);
        Assert.Equal(expectedCheckedSources, result.Errors.Count);
    }

    [Fact]
    public async Task RunFetchAsync_dry_run_counts_candidates_without_persisting()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var feedUrl = "https://www.queenonline.com/feed/";
        var service = NewsAgentTestSupport.CreateDiscoveryService(
            repository,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
            {
                [feedUrl] = NewsAgentTestSupport.ReadFixture("sample-rss.xml")
            }));
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "queen-online",
            "Queen Online",
            "https://www.queenonline.com/",
            feedUrl,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(DryRun: true, Force: true));

        Assert.Equal(2, result.CandidatesCreated);
        Assert.Empty(await repository.GetCandidatesAsync());
    }

    [Fact]
    public async Task RunFetchAsync_skips_duplicates_by_content_hash()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var feedUrl = "https://www.queenonline.com/feed/";
        var service = NewsAgentTestSupport.CreateDiscoveryService(
            repository,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
            {
                [feedUrl] = """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <rss version="2.0"><channel>
                      <item>
                        <title>Queen announce 2026 tour</title>
                        <link>https://www.queenonline.com/news/tour-2026</link>
                        <description>Official dates announced for 2026.</description>
                      </item>
                      <item>
                        <title>Queen announce 2026 tour</title>
                        <link>https://www.queenonline.com/news/tour-2026-copy?utm_source=test</link>
                        <description>Official dates announced for 2026.</description>
                      </item>
                    </channel></rss>
                    """
            }));
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "queen-online",
            "Queen Online",
            "https://www.queenonline.com/",
            feedUrl,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));

        Assert.Equal(2, result.ItemsFetched);
        Assert.Equal(1, result.CandidatesCreated);
        Assert.Equal(1, result.DuplicatesSkipped);
    }

    [Fact]
    public async Task RunFetchAsync_records_source_failures_without_stopping_other_sources()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var failingFeed = "https://www.queenonline.com/feed/";
        var workingFeed = "https://www.nme.com/feed";
        var service = NewsAgentTestSupport.CreateDiscoveryService(
            repository,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
            {
                [workingFeed] = NewsAgentTestSupport.ReadFixture("sample-rss.xml")
            }));
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "queen-online",
            "Queen Online",
            "https://www.queenonline.com/",
            failingFeed,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "nme-music",
            "NME Music",
            "https://www.nme.com/music",
            workingFeed,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Secondary,
            60,
            true,
            "queen"));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));

        Assert.Equal(1, result.Failures);
        Assert.Single(result.Errors);
        Assert.Contains("queen-online", result.Errors[0], StringComparison.Ordinal);
        Assert.Equal(1, result.CandidatesCreated);
    }

    [Theory]
    [InlineData(NewsDiscoverySourceType.Rss, "https://www.queenonline.com/feed/", "sample-rss.xml", 2)]
    [InlineData(NewsDiscoverySourceType.Sitemap, "https://www.queenonline.com/sitemap.xml", "sample-sitemap.xml", 2)]
    [InlineData(NewsDiscoverySourceType.AllowlistedPage, "https://www.rogertaylorofficial.com/news", "sample-page.html", 2)]
    public async Task RunFetchAsync_ingests_each_source_type(
        NewsDiscoverySourceType sourceType,
        string feedOrSiteUrl,
        string fixtureName,
        int expectedCandidates)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var service = NewsAgentTestSupport.CreateDiscoveryService(
            repository,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
            {
                [feedOrSiteUrl] = NewsAgentTestSupport.ReadFixture(fixtureName)
            }));
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "source-" + sourceType.ToString().ToLowerInvariant(),
            sourceType.ToString(),
            "https://example.com/",
            feedOrSiteUrl,
            sourceType,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));

        Assert.Equal(1, result.SourcesChecked);
        Assert.Equal(expectedCandidates, result.CandidatesCreated);
    }

    [Fact]
    public async Task RunFetchAsync_skips_static_asset_links_from_allowlisted_page_source()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var pageUrl = "https://www.rogertaylorofficial.com/news";
        var service = NewsAgentTestSupport.CreateDiscoveryService(
            repository,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
            {
                [pageUrl] = """
                    <!DOCTYPE html>
                    <html>
                      <body>
                        <a href="/news/tour-update">Tour update</a>
                        <a href="/wp-content/themes/site/style.css">Stylesheet</a>
                        <a href="/wp-content/uploads/hero.jpg">Hero image</a>
                      </body>
                    </html>
                    """
            }));
        await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "roger-taylor",
            "Roger Taylor",
            "https://www.rogertaylorofficial.com/",
            pageUrl,
            NewsDiscoverySourceType.AllowlistedPage,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var result = await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));

        Assert.Equal(1, result.ItemsFetched);
        Assert.Equal(1, result.CandidatesCreated);
        var candidates = await repository.GetCandidatesAsync();
        Assert.Single(candidates);
        Assert.Equal("https://www.rogertaylorofficial.com/news/tour-update", candidates[0].SourceUrl);
    }
}
