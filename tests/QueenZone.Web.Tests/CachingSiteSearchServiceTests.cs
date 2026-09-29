using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;
using QueenZone.Web;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class CachingSiteSearchServiceTests
{
    [Fact]
    public async Task Anonymous_hit_reuses_cached_page()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        var first = await sut.SearchAsync("Queen", null, 1, 20);
        var second = await sut.SearchAsync("  QUEEN ", null, 1, 20);

        Assert.Same(first, second);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Miss_calls_inner_once_per_distinct_key()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        await sut.SearchAsync("Queen", null, 1, 20);
        await sut.SearchAsync("Queen", SiteSearchContentType.News, 1, 20);
        await sut.SearchAsync("Queen", null, 2, 20);

        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Ttl_expiry_calls_inner_again()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero));
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true, timeProvider: clock);

        await sut.SearchAsync("Queen", null, 1, 20);
        clock.Advance(SiteSearchResultCache.AbsoluteExpiration);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var inner = new CountingSiteSearchService { ThrowOnCall = 1 };
        var sut = Create(inner, anonymous: true);

        await Assert.ThrowsAsync<SiteSearchTimeoutException>(() => sut.SearchAsync("Queen", null, 1, 20));
        var recovered = await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal("Queen", recovered.Results[0].Title);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Cancellation_is_not_cached()
    {
        var inner = new CountingSiteSearchService { CancelOnCall = 1 };
        var sut = Create(inner, anonymous: true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.SearchAsync("Queen", null, 1, 20, cancelled.Token));
        var recovered = await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("Queen", recovered.Results[0].Title);
    }

    [Fact]
    public async Task Short_query_bypasses_cache()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        await sut.SearchAsync("a", null, 1, 20);
        await sut.SearchAsync("a", null, 1, 20);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Authenticated_requests_bypass_cache()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: false);

        await sut.SearchAsync("Queen", null, 1, 20);
        await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void AddSiteSearchResultCache_requires_an_inner_registration()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(services.AddSiteSearchResultCache);

        Assert.Contains("ISiteSearchService", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddSiteSearchResultCache_wraps_instance_and_factory_registrations()
    {
        var instanceInner = new CountingSiteSearchService();
        var instanceServices = new ServiceCollection();
        instanceServices.AddSingleton<ISiteSearchService>(instanceInner);
        instanceServices.AddSiteSearchResultCache();
        await using var instanceProvider = instanceServices.BuildServiceProvider();
        var fromInstance = instanceProvider.GetRequiredService<ISiteSearchService>();
        Assert.IsType<CachingSiteSearchService>(fromInstance);
        await fromInstance.SearchAsync("Queen", null, 1, 20);
        Assert.Equal(1, instanceInner.Calls);

        var factoryInner = new CountingSiteSearchService();
        var factoryServices = new ServiceCollection();
        factoryServices.AddSingleton<ISiteSearchService>(_ => factoryInner);
        factoryServices.AddSiteSearchResultCache();
        await using var factoryProvider = factoryServices.BuildServiceProvider();
        var fromFactory = factoryProvider.GetRequiredService<ISiteSearchService>();
        Assert.IsType<CachingSiteSearchService>(fromFactory);
        await fromFactory.SearchAsync("Queen", null, 1, 20);
        Assert.Equal(1, factoryInner.Calls);
    }

    [Fact]
    public async Task Concurrent_misses_share_one_inner_call()
    {
        var inner = new CountingSiteSearchService { HoldFirstCall = true };
        var sut = Create(inner, anonymous: true);

        var first = sut.SearchAsync("Queen", null, 1, 20);
        await inner.Entered.Task;
        var second = sut.SearchAsync("Queen", null, 1, 20);
        inner.Release.SetResult();

        var pages = await Task.WhenAll(first, second);

        Assert.Equal(1, inner.Calls);
        Assert.Same(pages[0], pages[1]);
    }

    private static CachingSiteSearchService Create(
        ISiteSearchService inner,
        bool anonymous,
        TimeProvider? timeProvider = null)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = anonymous
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "member")],
                        authenticationType: "test")),
            },
        };

        return new CachingSiteSearchService(inner, new SiteSearchResultCache(), accessor, timeProvider);
    }

    private sealed class CountingSiteSearchService : ISiteSearchService
    {
        public int Calls { get; private set; }

        public int ThrowOnCall { get; init; }

        public int CancelOnCall { get; init; }

        public bool HoldFirstCall { get; init; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SiteSearchPage> SearchAsync(
            string query,
            string? contentType,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref calls);
            Calls = call;

            if (CancelOnCall == call)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (ThrowOnCall == call)
            {
                throw new SiteSearchTimeoutException(query, TimeSpan.FromSeconds(30), new InvalidOperationException("timeout"));
            }

            if (HoldFirstCall && call == 1)
            {
                Entered.SetResult();
                await Release.Task;
            }

            return new SiteSearchPage(
                [new SiteSearchResult(
                    SiteSearchContentType.News,
                    SearchDocumentSourceKey.ForNews(1),
                    query.Trim(),
                    "summary",
                    "/news/1",
                    null,
                    null,
                    null,
                    null)],
                1,
                page,
                pageSize);
        }

        private int calls;
    }
}
