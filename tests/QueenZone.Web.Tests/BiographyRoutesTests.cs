using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class BiographyRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public BiographyRoutesTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task BiographyIndexRendersChaptersInDisplaySequenceDescendingOrder()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("Biography", body);
        Assert.Contains("/biography/5/1992", body);
        Assert.Contains("/biography/1/1946-1969", body);
        Assert.Contains("5 chapters", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/biography"), body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone biography");

        var newestIndex = body.IndexOf("/biography/5/1992", StringComparison.Ordinal);
        var oldestIndex = body.IndexOf("/biography/1/1946-1969", StringComparison.Ordinal);
        Assert.True(newestIndex >= 0);
        Assert.True(oldestIndex > newestIndex);
    }

    [Fact]
    public async Task BiographyIndexFallsBackToBodyExcerptWhenSummaryIsEmpty()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("When the band Smile lost its singer", body);
    }

    [Fact]
    public async Task BiographyIndexStripsHtmlFromLegacySummary()
    {
        var chapters = new[]
        {
            new BiographyChapterItem(
                8001,
                "1946 - 1969",
                "<p>A founding chapter <strong>summary</strong> with HTML.</p>",
                "<p>Body text.</p>",
                1,
                new DateTime(1969, 12, 31, 0, 0, 0, DateTimeKind.Utc))
        };

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IBiographyRepository>(new InMemoryBiographyRepository(chapters));
            })).CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("A founding chapter summary with HTML.", body);
        Assert.DoesNotContain("&lt;p&gt;", body);
        Assert.DoesNotContain("&lt;strong&gt;", body);
    }

    [Fact]
    public async Task BiographyDetailRendersChapterBodyAndNavigation()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/2/1970");

        Assert.Contains("1970", body);
        Assert.Contains("Killer Queen", body);
        Assert.Contains("Previous Chapter", body);
        Assert.Contains("Next Chapter", body);
        Assert.Contains("/biography/1/1946-1969", body);
        Assert.Contains("/biography/3/1975", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/biography/2/1970"), body);
        TestHtmlAssertions.AssertPageTitle(body, "1970 | QueenZone biography");
    }

    [Fact]
    public async Task BiographyDetailReusesOneCachedListForNavigationAcrossRequests()
    {
        var chapters = new[]
        {
            new BiographyChapterItem(1, "First", "First summary", "First body", 1, DateTime.UtcNow),
            new BiographyChapterItem(2, "Second", "Second summary", "Second body", 2, DateTime.UtcNow),
        };
        var repository = new CountingBiographyRepository(chapters);
        using var isolated = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<IBiographyRepository>();
            services.AddSingleton<IBiographyRepository>(repository);
        });
        using var client = isolated.CreateAnonymousClient();

        var first = await client.GetStringAsync("/biography/2/second");
        var listCallsAfterFirst = repository.ListCallCount;
        var second = await client.GetStringAsync("/biography/2/second");

        Assert.Contains("Previous Chapter", first);
        Assert.Contains("Previous Chapter", second);
        Assert.InRange(listCallsAfterFirst, 1, 2);
        Assert.Equal(listCallsAfterFirst, repository.ListCallCount);
        Assert.True(repository.DetailCallCount >= 2);
        Assert.Equal(0, repository.AdjacentCallCount);
    }

    [Fact]
    public async Task FirstChapterHidesPreviousNavigation()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/1/1946-1969");

        Assert.DoesNotContain("Previous Chapter", body);
        Assert.Contains("Next Chapter", body);
    }

    [Fact]
    public async Task LastChapterHidesNextNavigation()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/5/1992");

        Assert.Contains("Previous Chapter", body);
        Assert.DoesNotContain("Next Chapter", body);
    }

    [Fact]
    public async Task MissingBiographyChapterReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/biography/999999/does-not-exist");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WrongBiographySlugRedirectsToCanonicalSlug()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/biography/2/not-the-right-slug");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/biography/2/1970", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task BiographyDetailSanitizesUnsafeLegacyHtmlInBody()
    {
        var chapters = new[]
        {
            new BiographyChapterItem(
                7001,
                "2026",
                string.Empty,
                "<script>alert('xss')</script><p>Safe <strong>legacy</strong> paragraph</p>",
                1,
                new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))
        };

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IBiographyRepository>(new InMemoryBiographyRepository(chapters));
            })).CreateClient();

        var body = await client.GetStringAsync("/biography/7001/2026");

        var articleStart = body.IndexOf("<article class=\"article-body\">", StringComparison.Ordinal);
        Assert.True(articleStart >= 0);
        var articleBody = body[articleStart..];
        Assert.DoesNotContain("alert", articleBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Safe <strong>legacy</strong> paragraph</p>", articleBody);
    }

    [Fact]
    public async Task EmptyBiographyShowsMessage()
    {
        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IBiographyRepository>(new InMemoryBiographyRepository([]));
            })).CreateClient();

        var body = await client.GetStringAsync("/biography");

        Assert.Contains("No biography chapters are available yet.", body);
    }

    [Fact]
    public async Task HomePageIncludesBiographyArchiveCard()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("href=\"/biography\"", body);
        Assert.Contains("The Queen story", body);
        Assert.Contains("Five ways into Queenzone", body);
    }

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(10, "X")]
    public void GetChapterNumeral_ReturnsExpectedRomanNumerals(int index, string expected)
    {
        Assert.Equal(expected, BiographyRoutes.GetChapterNumeral(index - 1));
    }

    [Theory]
    [InlineData("1946 - 1969", "1946–1969")]
    [InlineData("1946 – 1969", "1946–1969")]
    [InlineData("1970", "1970")]
    [InlineData("1992", "1992")]
    public void GetYearMarker_ParsesLegacyTitleYears(string title, string expected)
    {
        Assert.Equal(expected, BiographyTitle.GetYearMarker(title));
    }

    private sealed class CountingBiographyRepository(IReadOnlyList<BiographyChapterItem> chapters) : IBiographyRepository
    {
        public int ListCallCount { get; private set; }
        public int DetailCallCount { get; private set; }
        public int AdjacentCallCount { get; private set; }

        public Task<IReadOnlyList<BiographyChapterItem>> GetChaptersAsync(CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return Task.FromResult(chapters);
        }

        public Task<BiographyChapterItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            DetailCallCount++;
            return Task.FromResult(chapters.SingleOrDefault(chapter => chapter.Id == id));
        }

        public Task<BiographyChapterNav> GetAdjacentChaptersAsync(int id, CancellationToken cancellationToken = default)
        {
            AdjacentCallCount++;
            return Task.FromResult(new BiographyChapterNav(null, null));
        }

        public Task<int> CreateAsync(AdminBiographyDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(int id, AdminBiographyDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
