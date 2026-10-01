using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using QueenZone.Data;
using QueenZone.Web;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class CachingSiteSearchServiceTests : IDisposable
{
    private readonly List<ServiceProvider> providers = [];

    public void Dispose()
    {
        foreach (var provider in providers)
        {
            provider.Dispose();
        }
    }

    [Fact]
    public async Task SearchAsync_IndexChanges_InvalidatesCachedResults()
    {
        var services = new ServiceCollection();
        services.AddQueenZoneInMemoryData();
        services.AddSiteSearchResultCache();
        await using var provider = services.BuildServiceProvider();
        var index = provider.GetRequiredService<ISearchIndexService>();
        var search = provider.GetRequiredService<ISiteSearchService>();
        var document = new QueenZone.Data.Entities.SearchDocumentEntity
        {
            SourceKey = "news:777",
            ContentType = SiteSearchContentType.News,
            Title = "Unusual performance test",
            Body = "Unusual performance test",
            Url = "/news/777",
        };
        Assert.Empty((await search.SearchAsync("Unusual", null, 1, 20)).Results);
        await index.UpsertAsync(document);
        Assert.Single((await search.SearchAsync("Unusual", null, 1, 20)).Results);
        await index.RemoveAsync(document.SourceKey);
        Assert.Empty((await search.SearchAsync("Unusual", null, 1, 20)).Results);
        await index.ReplaceContentTypeAsync(SiteSearchContentType.News, [document]);
        Assert.Single((await search.SearchAsync("Unusual", null, 1, 20)).Results);
        await index.ReplaceContentTypeAsync(SiteSearchContentType.News, []);
        Assert.Empty((await search.SearchAsync("Unusual", null, 1, 20)).Results);
    }

    [Fact]
    public async Task SearchAsync_WriteDuringInflightRead_DoesNotReuseOldGeneration()
    {
        var revision = new SearchIndexRevision();
        using var cache = new SiteSearchResultCache(revision);
        var inner = new CountingSiteSearchService { HoldFirstCall = true };
        var sut = Create(inner, anonymous: true, cache: cache);
        var stale = sut.SearchAsync("Queen", null, 1, 20);
        await inner.Entered.Task;
        revision.Advance();
        var fresh = await sut.SearchAsync("Queen", null, 1, 20);
        inner.Release.SetResult();
        await stale;
        Assert.Same(fresh, await sut.SearchAsync("Queen", null, 1, 20));
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Anonymous_hit_reuses_cached_page()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        var first = await sut.SearchAsync("Queen", null, 1, 20);
        var second = await sut.SearchAsync("  QUEEN ", null, 1, 20);

        Assert.Same(first, second);
        Assert.Equal(1, inner.Calls);
        Assert.Equal("queen", inner.LastQuery);
        Assert.Null(inner.LastContentType);
    }

    [Fact]
    public async Task Null_and_blank_content_types_share_one_normalized_call()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        await sut.SearchAsync("Queen", null, 1, 20);
        await sut.SearchAsync("Queen", string.Empty, 1, 20);
        await sut.SearchAsync("Queen", "   ", 0, 20);

        Assert.Equal(1, inner.Calls);
        Assert.Null(inner.LastContentType);
        Assert.Equal(1, inner.LastPage);
    }

    [Fact]
    public async Task Miss_calls_inner_once_per_distinct_key()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        await sut.SearchAsync("Queen", null, 1, 20);
        await sut.SearchAsync("Queen", SiteSearchContentType.News, 1, 20);
        Assert.Equal(SiteSearchContentType.News, inner.LastContentType);
        await sut.SearchAsync("Queen", null, 2, 20);

        Assert.Equal(3, inner.Calls);
        Assert.Null(inner.LastContentType);
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

        Assert.Equal("queen", recovered.Results[0].Title);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Cancellation_is_not_cached()
    {
        var inner = new CountingSiteSearchService { CancelOnCall = 1 };
        var sut = Create(inner, anonymous: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.SearchAsync("Queen", null, 1, 20));
        var recovered = await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("queen", recovered.Results[0].Title);
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
    public async Task Beyond_max_page_is_not_cached()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        await sut.SearchAsync("Queen", null, SiteSearchLimits.MaxPage + 1, 20);
        await sut.SearchAsync("Queen", null, SiteSearchLimits.MaxPage + 1, 20);

        Assert.Equal(2, inner.Calls);
        Assert.Equal(11, inner.LastPage);
    }

    [Fact]
    public async Task Successful_empty_page_for_a_real_query_is_cached()
    {
        var inner = new CountingSiteSearchService { EmptyResults = true };
        var sut = Create(inner, anonymous: true);

        var first = await sut.SearchAsync("zzzz", null, 1, 20);
        var second = await sut.SearchAsync("ZZZZ", null, 1, 20);

        Assert.Empty(first.Results);
        Assert.Same(first, second);
        Assert.Equal(1, inner.Calls);
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
    public void AddSiteSearchResultCache_registers_the_concrete_inner_separately()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SharedSearchGate());
        services.AddScoped<GatedSearchContext>();
        services.AddScoped<ISiteSearchService, GatedSiteSearchService>();
        services.AddSiteSearchResultCache();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(GatedSiteSearchService)
                && descriptor.ImplementationType == typeof(GatedSiteSearchService));
        Assert.DoesNotContain(
            services.Where(descriptor => descriptor.ServiceType == typeof(ISiteSearchService)),
            descriptor => descriptor.ImplementationType == typeof(GatedSiteSearchService));
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
        var cache = new SiteSearchResultCache();
        var firstSut = Create(inner, anonymous: true, cache: cache);
        var secondSut = Create(inner, anonymous: true, cache: cache);

        var first = firstSut.SearchAsync("Queen", null, 1, 20);
        await inner.Entered.Task;
        var second = secondSut.SearchAsync("Queen", null, 1, 20);
        inner.Release.SetResult();

        var pages = await Task.WhenAll(first, second);

        Assert.Equal(1, inner.Calls);
        Assert.Same(pages[0], pages[1]);
        await WaitForInflightEmpty(cache);
    }

    [Fact]
    public async Task Inflight_lives_on_the_cache_instance_not_a_static()
    {
        var inner = new CountingSiteSearchService { HoldFirstCall = true };
        var first = Create(inner, anonymous: true, cache: new SiteSearchResultCache());
        var second = Create(inner, anonymous: true, cache: new SiteSearchResultCache());

        var held = first.SearchAsync("Queen", null, 1, 20);
        await inner.Entered.Task;
        var other = second.SearchAsync("Queen", null, 1, 20);
        inner.Release.SetResult();

        await Task.WhenAll(held, other);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Factory_rechecks_cache_before_sql()
    {
        var inner = new CountingSiteSearchService();
        var sut = Create(inner, anonymous: true);

        await sut.SearchAsync("Queen", null, 1, 20);
        sut.BypassOuterCacheLookup = true;
        await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Leader_cancel_does_not_cancel_followers()
    {
        var inner = new CountingSiteSearchService { HoldFirstCall = true };
        var sut = Create(inner, anonymous: true);
        using var leaderCts = new CancellationTokenSource();

        var leader = sut.SearchAsync("Queen", null, 1, 20, leaderCts.Token);
        await inner.Entered.Task;
        var follower = sut.SearchAsync("Queen", null, 1, 20);
        leaderCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);
        inner.Release.SetResult();
        var page = await follower;

        Assert.Equal(1, inner.Calls);
        Assert.Equal("queen", page.Results[0].Title);
    }

    [Fact]
    public async Task Follower_cancel_does_not_cancel_the_shared_fetch()
    {
        var inner = new CountingSiteSearchService { HoldFirstCall = true };
        var sut = Create(inner, anonymous: true);
        using var followerCts = new CancellationTokenSource();

        var leader = sut.SearchAsync("Queen", null, 1, 20);
        await inner.Entered.Task;
        var follower = sut.SearchAsync("Queen", null, 1, 20, followerCts.Token);
        followerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follower);
        inner.Release.SetResult();
        var page = await leader;

        Assert.Equal(1, inner.Calls);
        Assert.Equal("queen", page.Results[0].Title);
    }

    [Fact]
    public async Task All_waiters_cancel_then_inflight_is_removed_and_next_call_reexecutes()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero));
        var inner = new CountingSiteSearchService { HoldFirstCall = true };
        var cache = new SiteSearchResultCache();
        var sut = Create(inner, anonymous: true, timeProvider: clock, cache: cache);
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();

        var first = sut.SearchAsync("Queen", null, 1, 20, firstCts.Token);
        await inner.Entered.Task;
        var second = sut.SearchAsync("Queen", null, 1, 20, secondCts.Token);
        firstCts.Cancel();
        secondCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(cache.Inflight.IsEmpty);

        inner.Release.SetResult();
        await WaitForInflightEmpty(cache);

        var cached = await sut.SearchAsync("Queen", null, 1, 20);
        Assert.Equal(1, inner.Calls);
        Assert.Equal("queen", cached.Results[0].Title);

        clock.Advance(SiteSearchResultCache.AbsoluteExpiration);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var replay = await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("queen", replay.Results[0].Title);
        Assert.True(cache.Inflight.IsEmpty);
    }

    [Fact]
    public async Task Faulted_shared_task_is_not_replayed()
    {
        var inner = new CountingSiteSearchService { HoldFirstCall = true, ThrowOnCall = 1 };
        var cache = new SiteSearchResultCache();
        var sut = Create(inner, anonymous: true, cache: cache);

        var first = sut.SearchAsync("Queen", null, 1, 20);
        await inner.Entered.Task;
        var second = sut.SearchAsync("Queen", null, 1, 20);
        inner.Release.SetResult();

        await Assert.ThrowsAsync<SiteSearchTimeoutException>(() => first);
        await Assert.ThrowsAsync<SiteSearchTimeoutException>(() => second);
        await WaitForInflightEmpty(cache);

        var recovered = await sut.SearchAsync("Queen", null, 1, 20);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("queen", recovered.Results[0].Title);
        Assert.True(cache.Inflight.IsEmpty);
    }

    [Fact]
    public async Task Leader_request_scope_dispose_does_not_fail_follower()
    {
        var gate = new SharedSearchGate();
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddSingleton<IHttpContextAccessor>(AnonymousAccessor());
        services.AddScoped<GatedSearchContext>();
        services.AddScoped<ISiteSearchService, GatedSiteSearchService>();
        services.AddSiteSearchResultCache();
        await using var root = services.BuildServiceProvider();

        var leaderScope = root.CreateAsyncScope();
        var leader = leaderScope.ServiceProvider
            .GetRequiredService<ISiteSearchService>()
            .SearchAsync("Queen", null, 1, 20);
        await gate.Entered.Task;

        await using var followerScope = root.CreateAsyncScope();
        var follower = followerScope.ServiceProvider
            .GetRequiredService<ISiteSearchService>()
            .SearchAsync("Queen", null, 1, 20);

        await leaderScope.DisposeAsync();
        gate.Release.SetResult();

        var page = await follower;
        var leaderPage = await leader;

        Assert.Equal("queen", page.Results[0].Title);
        Assert.Equal("queen", leaderPage.Results[0].Title);
        Assert.True(gate.InnersCreated >= 2);
        Assert.True(gate.ContextsCreated >= 2);
        Assert.True(gate.ContextDisposes >= 1);
        Assert.DoesNotContain(typeof(CachingSiteSearchService), gate.InnerTypes);
    }

    private CachingSiteSearchService Create(
        ISiteSearchService inner,
        bool anonymous,
        TimeProvider? timeProvider = null,
        SiteSearchResultCache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(inner);
        services.AddSingleton<ISiteSearchService>(inner);
        services.AddSingleton<IHttpContextAccessor>(anonymous ? AnonymousAccessor() : AuthenticatedAccessor());
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        if (cache is not null)
        {
            services.AddSingleton(cache);
        }

        services.AddSiteSearchResultCache();
        var provider = services.BuildServiceProvider();
        providers.Add(provider);
        return Assert.IsType<CachingSiteSearchService>(provider.GetRequiredService<ISiteSearchService>());
    }

    private static HttpContextAccessor AnonymousAccessor() =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity()),
            },
        };

    private static HttpContextAccessor AuthenticatedAccessor() =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "member")],
                    authenticationType: "test")),
            },
        };

    private static async Task WaitForInflightEmpty(SiteSearchResultCache cache)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!cache.Inflight.IsEmpty)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Inflight entry was not removed.");
            }

            await Task.Yield();
        }
    }

    private sealed class SharedSearchGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ContextsCreated;

        public int ContextDisposes;

        public int InnersCreated;

        public List<Type> InnerTypes { get; } = [];
    }

    /// <summary>
    /// Stand-in for a request-scoped <c>DbContext</c>. The shared fetch must resolve
    /// this from its own scope so disposing the leader request does not close it.
    /// </summary>
    private sealed class GatedSearchContext : IDisposable
    {
        private readonly SharedSearchGate gate;
        private int disposed;

        public GatedSearchContext(SharedSearchGate gate)
        {
            this.gate = gate;
            Interlocked.Increment(ref gate.ContextsCreated);
        }

        public void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);

        public void Dispose()
        {
            Volatile.Write(ref disposed, 1);
            Interlocked.Increment(ref gate.ContextDisposes);
        }
    }

    private sealed class GatedSiteSearchService : ISiteSearchService
    {
        private readonly SharedSearchGate gate;
        private readonly GatedSearchContext context;

        public GatedSiteSearchService(SharedSearchGate gate, GatedSearchContext context)
        {
            this.gate = gate;
            this.context = context;
            Interlocked.Increment(ref gate.InnersCreated);
            lock (gate.InnerTypes)
            {
                gate.InnerTypes.Add(GetType());
            }
        }

        public async Task<SiteSearchPage> SearchAsync(
            string query,
            string? contentType,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            context.ThrowIfDisposed();
            gate.Entered.TrySetResult();
            await gate.Release.Task;
            context.ThrowIfDisposed();
            return new SiteSearchPage(
                [new SiteSearchResult(
                    SiteSearchContentType.News,
                    SearchDocumentSourceKey.ForNews(1),
                    query,
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
    }

    private sealed class CountingSiteSearchService : ISiteSearchService
    {
        public int Calls { get; private set; }

        public string? LastQuery { get; private set; }

        public string? LastContentType { get; private set; }

        public int LastPage { get; private set; }

        public int LastPageSize { get; private set; }

        public int ThrowOnCall { get; init; }

        public int CancelOnCall { get; init; }

        public bool HoldFirstCall { get; init; }

        public bool EmptyResults { get; init; }

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
            LastQuery = query;
            LastContentType = contentType;
            LastPage = page;
            LastPageSize = pageSize;

            if (CancelOnCall == call)
            {
                throw new OperationCanceledException();
            }

            if (HoldFirstCall && call == 1)
            {
                Entered.SetResult();
                await Release.Task;
            }

            if (ThrowOnCall == call)
            {
                throw new SiteSearchTimeoutException(query, TimeSpan.FromSeconds(30), new InvalidOperationException("timeout"));
            }

            if (EmptyResults)
            {
                return new SiteSearchPage([], 0, page, pageSize);
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
