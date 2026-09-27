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

    [Fact]
    public async Task IsolatedQuizzes_reset_clears_quiz_and_submission_stores()
    {
        await using var cache = new WebHostVariantCache();
        var host = cache.Get(WebHostVariants.IsolatedQuizzes);
        using var client = host.CreateAnonymousClient();
        var quizzes = host.Services.GetRequiredService<IQuizRepository>();
        var submissions = host.Services.GetRequiredService<IQuizQuestionSubmissionRepository>();

        await quizzes.CreateAsync(
            new AdminQuizDraft(
                "Reset probe",
                null,
                [new QuizQuestionDraft("Q?", 1, [new QuizOptionDraft("A", true), new QuizOptionDraft("B", false)])]),
            Guid.NewGuid());
        await submissions.CreateAsync(new NewQuizQuestionSubmission(
            Guid.NewGuid(),
            "Reset submission?",
            [new QuizQuestionSubmissionOptionDraft("A", true), new QuizQuestionSubmissionOptionDraft("B", false)],
            null));

        Assert.NotEmpty(await quizzes.GetAllAsync());
        Assert.NotEmpty(await submissions.GetPendingAsync(1, 10));

        await host.ResetAsync();

        Assert.Empty(await quizzes.GetAllAsync());
        Assert.Empty(await submissions.GetPendingAsync(1, 10));
    }

    [Fact]
    public async Task WebHostVariantCache_Get_starts_host_and_exposes_context()
    {
        await using var cache = new WebHostVariantCache();

        var activity = cache.Get(WebHostVariants.TestingRecordingMemberActivity);
        Assert.NotNull(activity.MemberActivity);

        var community = cache.Get(WebHostVariants.TestingMutableCommunityArticles);
        Assert.NotNull(community.CommunityArticles);

        var quota = cache.Get(WebHostVariants.PhotoUploadQuota1);
        Assert.NotNull(quota.UploadQuota);
    }

    [Fact]
    public async Task IsolatedHomePolls_and_trivia_reset_clears_seed()
    {
        await using var cache = new WebHostVariantCache();
        var pollsHost = cache.Get(WebHostVariants.IsolatedHomePolls);
        using var pollClient = pollsHost.CreateAnonymousClient();
        var polls = pollsHost.Services.GetRequiredService<IHomePollRepository>();
        var pollId = await polls.CreateAsync(new AdminHomePollDraft("Reset poll?", ["A", "B"]), Guid.NewGuid());
        await polls.PublishAsync(pollId);
        Assert.NotNull(await polls.GetCurrentAsync(null));

        await pollsHost.ResetAsync();
        Assert.Null(await polls.GetCurrentAsync(null));

        var triviaHost = cache.Get(WebHostVariants.IsolatedTrivia);
        using var triviaClient = triviaHost.CreateAnonymousClient();
        var trivia = triviaHost.Services.GetRequiredService<ITriviaRepository>();
        await trivia.CreateAsync(new AdminTriviaDraft("Reset fact", true));
        Assert.NotEmpty(await trivia.GetAllAsync());

        await triviaHost.ResetAsync();
        Assert.Empty(await trivia.GetAllAsync());
    }

    [Fact]
    public async Task IsolatedNewsDiscussion_seeds_and_resets()
    {
        await using var cache = new WebHostVariantCache();
        var host = cache.Get(WebHostVariants.IsolatedNewsDiscussion);
        using var client = host.CreateAnonymousClient();
        Assert.NotNull(host.SeedableNews);
        Assert.NotNull(host.SeedableDiscussion);

        host.SeedableNews!.Seed(new NewsItem(
            6101,
            "Seeded discussion",
            "Excerpt",
            "Body",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true,
            ForumTopicId: 1002));
        host.SeedableDiscussion!.Seed(1002, 2, [new NewsDiscussionPreview("Alice", DateTime.UtcNow, "Hi")]);
        Assert.Equal("Seeded discussion", (await host.Services.GetRequiredService<INewsRepository>().GetByIdAsync(6101))?.Title);
        var discussion = await host.Services.GetRequiredService<INewsForumDiscussionLookup>()
            .GetDiscussionAsync(1002, 2);
        Assert.Equal(2, discussion.ReplyCount);

        await host.ResetAsync();
        Assert.Null(await host.Services.GetRequiredService<INewsRepository>().GetByIdAsync(6101));
        var cleared = await host.Services.GetRequiredService<INewsForumDiscussionLookup>()
            .GetDiscussionAsync(1002, 2);
        Assert.Equal(0, cleared.ReplyCount);
    }

    [Fact]
    public async Task Content_api_variants_register_expected_services()
    {
        await using var cache = new WebHostVariantCache();

        var emptyQuotes = cache.Get(WebHostVariants.EmptyQuotes);
        using var quoteClient = emptyQuotes.CreateAnonymousClient();
        Assert.Null(await emptyQuotes.Services.GetRequiredService<IQuoteRepository>().GetRandomPublishedAsync());

        var throwBlob = cache.Get(WebHostVariants.ThrowOnReadBlob);
        using var blobClient = throwBlob.CreateAnonymousClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            throwBlob.Services.GetRequiredService<IBlobUploadService>().OpenReadAsync("songfiles", "x.mp3"));

        var clock = cache.Get(WebHostVariants.IsolatedQuizzesWithClock);
        using var clockClient = clock.CreateAnonymousClient();
        Assert.NotNull(clock.Clock);
        clock.Clock!.SetUtcNow(new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero));
        clock.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 0, 0, 1, TimeSpan.Zero), clock.Clock.GetUtcNow());
        await clock.ResetAsync();
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), clock.Clock.GetUtcNow());

        var sequential = cache.Get(WebHostVariants.SequentialTrivia);
        using var sequentialClient = sequential.CreateAnonymousClient();
        Assert.NotNull(sequential.SequentialTrivia);
        await sequential.Services.GetRequiredService<ITriviaRepository>().GetAllAsync();
        Assert.Equal(1, sequential.SequentialTrivia!.AllCallCount);
        await sequential.ResetAsync();
        Assert.Equal(0, sequential.SequentialTrivia.AllCallCount);
    }

    [Fact]
    public void Recording_activity_and_community_articles_seed_and_reset()
    {
        var activity = new RecordingMemberPublicActivityRepository();
        var authorId = Guid.NewGuid();
        activity.Seed(
        [
            new MemberPublicActivityItem(
                MemberPublicActivityType.Article,
                "Seeded",
                "summary",
                DateTimeOffset.UtcNow,
                AuthorId: authorId,
                AuthorDisplayName: "Author"),
        ]);
        Assert.Equal(0, activity.FeedPageCalls);

        var community = new MutableCommunityArticleRepository();
        community.Seed(
        [
            new PublishedArticleSubmission(
                Guid.NewGuid(),
                "Title",
                "slug",
                "excerpt",
                "<p>Body</p>",
                null,
                null,
                DateTimeOffset.UtcNow,
                "Author",
                1,
                null),
        ]);

        activity.Reset();
        community.Reset();
        Assert.Equal(0, activity.FeedPageCalls);
    }
}
