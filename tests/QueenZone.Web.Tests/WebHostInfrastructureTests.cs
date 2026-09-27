using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class WebHostInfrastructureTests
{
    [Fact]
    public void TestIds_For_appends_a_unique_suffix()
    {
        var first = TestIds.For("NewsDetail");
        var second = TestIds.For("NewsDetail");

        Assert.StartsWith("NewsDetail-", first, StringComparison.Ordinal);
        Assert.StartsWith("NewsDetail-", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(32, first.Split('-')[^1].Length);
    }

    [Fact]
    public void TestIds_For_rejects_blank_names()
    {
        Assert.Throws<ArgumentException>(() => TestIds.For(" "));
    }

    [Fact]
    public void SeededRandomOrder_keeps_discovery_order_when_seed_is_unset()
    {
        var items = new[] { "a", "b", "c" };

        var ordered = SeededRandomOrder.Apply(items, null, out var printed);

        Assert.Null(printed);
        Assert.Equal(items, ordered);
    }

    [Fact]
    public void SeededRandomOrder_shuffles_deterministically_for_a_seed()
    {
        var items = Enumerable.Range(0, 12).ToArray();

        var first = SeededRandomOrder.Apply(items, "1811", out var printed);
        var second = SeededRandomOrder.Apply(items, "1811", out _);

        Assert.Equal("1811", printed);
        Assert.Equal(first, second);
        Assert.NotEqual(items, first);
        Assert.Equal(items.Order(), first.Order());
    }

    [Fact]
    public void SeededRandomOrder_rejects_a_non_integer_seed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SeededRandomOrder.Apply(new[] { 1 }, "abc", out _));
        Assert.Contains(SeededRandomTestCaseOrderer.EnvironmentVariableName, ex.Message);
    }

    [Fact]
    public async Task MutableLegacyMemberLookup_seeds_and_resets_to_sample_data()
    {
        var lookup = new MutableLegacyMemberLookupRepository();
        lookup.Seed("claim@example.com", [new LegacyMemberMatch(42, "ArchiveFan")]);

        Assert.Equal(42, (await lookup.FindByEmailAsync("claim@example.com"))?.UserId);
        Assert.Equal("Mike Ryde", (await lookup.FindByEmailAsync("legacy.fan@queenzone.org"))?.Username);

        lookup.Reset();

        Assert.Null(await lookup.FindByEmailAsync("claim@example.com"));
        Assert.Equal(35418, (await lookup.FindByUserIdAsync(35418))?.UserId);
    }

    [Fact]
    public async Task TrackingNewsRepository_counts_and_resets()
    {
        var tracking = new TrackingNewsRepository(new FixedNewsRepository(WebHostVariants.TrackingPromotedNewsItems()));

        await tracking.GetByIdAsync(1002);
        await tracking.GetByIdsAsync([1002, 1003]);

        Assert.Equal(1, tracking.GetByIdCallCount);
        Assert.Equal(1, tracking.GetByIdsCallCount);
        Assert.Equal([1002, 1003], tracking.LastRequestedIds);

        tracking.Reset();

        Assert.Equal(0, tracking.GetByIdCallCount);
        Assert.Equal(0, tracking.GetByIdsCallCount);
        Assert.Empty(tracking.LastRequestedIds);
    }

    [Fact]
    public async Task ThrowingSearchIndexService_fails_closed_on_every_operation()
    {
        var index = new ThrowingSearchIndexService();
        var document = new QueenZone.Data.Entities.SearchDocumentEntity { SourceKey = "news:1" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => index.UpsertAsync(document));
        await Assert.ThrowsAsync<InvalidOperationException>(() => index.RemoveAsync("news:1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => index.ReplaceContentTypeAsync("news", [document]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => index.GetContentTypeCountsAsync());
    }

    [Fact]
    public async Task WebHostVariantCache_reuses_a_host_and_rejects_duplicate_names()
    {
        await using var cache = new WebHostVariantCache();
        var first = cache.Get(WebHostVariants.EmptyNews);
        var second = cache.Get(WebHostVariants.EmptyNews);
        Assert.Same(first, second);

        using var client = first.CreateAnonymousClient();
        var news = first.Services.GetRequiredService<INewsRepository>();
        Assert.Equal(0, await news.GetPublishedCountAsync());

        var colliding = WebHostVariants.EmptyNews with
        {
            Settings = ImmutableSortedDictionary.CreateRange(
            [
                KeyValuePair.Create<string, string?>("Site:PublicBaseUrl", "https://collision.test"),
            ]),
        };
        var ex = Assert.Throws<InvalidOperationException>(() => cache.Get(colliding));
        Assert.Contains("EmptyNews", ex.Message);
    }

    [Fact]
    public async Task WebHostVariantCache_builds_declared_profiles()
    {
        await using var cache = new WebHostVariantCache();

        var empty = cache.Get(WebHostVariants.EmptyNews);
        Assert.Equal(0, await empty.Services.GetRequiredService<INewsRepository>().GetPublishedCountAsync());

        var memberNews = cache.Get(WebHostVariants.MemberSubmittedNews);
        var submitted = await memberNews.Services.GetRequiredService<INewsRepository>().GetByIdAsync(5100);
        Assert.Equal(WebHostVariants.MemberSubmittedNewsSubmitterId, submitted?.SubmitterMemberId);

        var inspectable = cache.Get(WebHostVariants.ExternalCookieInspectableBlob);
        using var inspectableClient = inspectable.CreateAnonymousClient();
        Assert.NotNull(inspectable.Services.GetService<IBlobUploadService>());
        inspectable.LegacyLookup.Seed("seeded@example.com", [new LegacyMemberMatch(9, "Seeded")]);
        Assert.Equal("Seeded", (await inspectable.LegacyLookup.FindByEmailAsync("seeded@example.com"))?.Username);

        await inspectable.ResetAsync();
        Assert.Null(await inspectable.LegacyLookup.FindByEmailAsync("seeded@example.com"));

        var trackingHost = cache.Get(WebHostVariants.TrackingPromotedNews);
        using var trackingClient = trackingHost.CreateAnonymousClient();
        Assert.NotNull(trackingHost.TrackingNews);
        await trackingHost.Services.GetRequiredService<INewsRepository>().GetByIdsAsync([1002]);
        Assert.Equal(1, trackingHost.TrackingNews!.GetByIdsCallCount);
        await trackingHost.ResetAsync();
        Assert.Equal(0, trackingHost.TrackingNews.GetByIdsCallCount);

        var throwing = cache.Get(WebHostVariants.ExternalCookieThrowingSearchIndex);
        var index = throwing.Services.GetRequiredService<ISearchIndexService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => index.RemoveAsync("news:1"));

        var schemes = throwing.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync(MemberAuthenticationSchemes.ExternalCookie));
    }

    [Fact]
    public async Task InspectableBlobWebApplicationFactory_exposes_resettable_blob_backend()
    {
        await using var factory = new InspectableBlobWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        await factory.BlobBackend.UploadAsync("ugc-avatars", "probe.bin", new MemoryStream([1, 2, 3]), "application/octet-stream");
        Assert.True(factory.BlobBackend.Exists("ugc-avatars", "probe.bin"));

        await factory.ResetAsync();
        Assert.False(factory.BlobBackend.Exists("ugc-avatars", "probe.bin"));
    }

    [Fact]
    public void HostServiceContext_and_variant_record_use_value_equality()
    {
        var left = WebHostVariants.Testing;
        var right = new WebHostVariant(
            WebHostVariants.Testing.Name,
            WebHostVariants.Testing.Environment,
            WebHostVariants.Testing.Settings,
            WebHostVariants.Testing.Services);
        Assert.Equal(left, right);
        Assert.NotEqual(WebHostVariants.Testing, WebHostVariants.ExternalCookie);
    }

    [Fact]
    public void Apply_unknown_profile_throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WebHostVariants.Apply((HostServiceProfile)int.MaxValue, services, null));
    }

    [Fact]
    public async Task InMemoryEditorialAndSearchStores_clear()
    {
        var editorial = new InMemoryEditorialArticleRepository();
        await editorial.SaveDraftAsync(
            new EditorialArticleDraft(
                null,
                null,
                null,
                "Clear me",
                "clear-me",
                "Excerpt",
                "<p>Body</p>",
                "Editor",
                "Features",
                null,
                null,
                null,
                DateTimeOffset.UtcNow),
            "admin@test.local");
        Assert.NotEmpty(await editorial.GetAllAsync());
        editorial.Clear();
        Assert.Empty(await editorial.GetAllAsync());

        var search = new SharedSearchIndexStore();
        search.Upsert(new QueenZone.Data.Entities.SearchDocumentEntity { SourceKey = "news:1", Title = "One" });
        Assert.Contains(search.GetAll(), document => document.SourceKey == "news:1");
        search.Clear();
        Assert.Empty(search.GetAll());
    }

    [Fact]
    public async Task CountingArticlesRepository_counts_and_resets()
    {
        var repository = new CountingArticlesRepository();

        Assert.Equal(0, repository.ArchivePageCallCount);
        Assert.Equal(0, repository.PublishedCountCallCount);

        Assert.Equal(1, await repository.GetPublishedCountAsync());
        Assert.Single(await repository.GetArchivePageAsync(1, 10));
        Assert.Equal("Cached archive article", (await repository.GetByIdAsync(7801))?.Title);
        Assert.Null(await repository.GetByIdAsync(1));
        Assert.Single(await repository.GetLatestAsync(1));
        Assert.Single(await repository.GetPublishedSitemapEntriesAsync());

        Assert.Equal(1, repository.ArchivePageCallCount);
        Assert.Equal(1, repository.PublishedCountCallCount);

        repository.Reset();

        Assert.Equal(0, repository.ArchivePageCallCount);
        Assert.Equal(0, repository.PublishedCountCallCount);
    }

    [Fact]
    public void Apply_counting_articles_profile_requires_context()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(
            () => WebHostVariants.Apply(HostServiceProfile.CountingArticles, services, null));
        Assert.Contains("requires a fixture context", ex.Message);
    }

    [Fact]
    public void Apply_counting_articles_registers_the_same_repository()
    {
        var services = new ServiceCollection();
        var context = new HostServiceContext();

        WebHostVariants.Apply(HostServiceProfile.CountingArticles, services, context);
        WebHostVariants.Apply(HostServiceProfile.CountingArticles, services, context);

        using var provider = services.BuildServiceProvider();
        Assert.Same(context.CountingArticles, provider.GetRequiredService<IArticlesRepository>());
        context.CountingArticles!.Reset();
        Assert.Equal(0, context.CountingArticles.ArchivePageCallCount);
    }

    [Fact]
    public void ProductionHostSettings_include_fail_closed_stubs()
    {
        Assert.Equal(string.Empty, ProductionHostSettings.Values["ConnectionStrings:QueenZoneLegacy"]);
        Assert.Equal(ProductionHostSettings.MobileAuthSigningKey, ProductionHostSettings.Values["MobileAuth:SigningKey"]);
        Assert.Equal("localhost;127.0.0.1", ProductionHostSettings.Values["QueenZoneHostFiltering:AllowedHosts"]);
        Assert.Equal(ProductionHostCollection.Name, "Production host");
    }

    [Fact]
    public void Apply_inspectable_profile_requires_context()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(
            () => WebHostVariants.Apply(HostServiceProfile.ExternalCookieInspectableBlob, services, null));
        Assert.Contains("requires a fixture context", ex.Message);
    }
}
