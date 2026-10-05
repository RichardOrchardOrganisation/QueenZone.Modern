using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class CommunityArticleRoutesTests : IClassFixture<WebHostVariantCache>
{
    private readonly WebHostVariantCache variants;

    public CommunityArticleRoutesTests(WebHostVariantCache variants)
    {
        this.variants = variants;
    }

    // -------------------------------------------------------------------------
    // InMemoryArticleRepository unit tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPageAsync_ReturnsOnlyPublishedArticles()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("published-slug", "Published Title", DateTimeOffset.UtcNow.AddDays(-1)),
        ]));

        var result = await repo.GetPageAsync(1, 10);

        Assert.Single(result);
        Assert.Equal("published-slug", result[0].Slug);
    }

    [Fact]
    public async Task GetPageAsync_RespectsTagFilter()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("article-a", "Article A", DateTimeOffset.UtcNow.AddDays(-2), tags: "queen,freddie"),
            Published("article-b", "Article B", DateTimeOffset.UtcNow.AddDays(-1), tags: "roger,john"),
            Published("article-c", "Article C", DateTimeOffset.UtcNow, tags: "queen,brian"),
        ]));

        var result = await repo.GetPageAsync(1, 10, tag: "queen");

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.True(
            ("," + item.Tags + ",").Contains(",queen,", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetPageAsync_PaginatesResults()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("art-1", "Article 1", DateTimeOffset.UtcNow.AddDays(-3)),
            Published("art-2", "Article 2", DateTimeOffset.UtcNow.AddDays(-2)),
            Published("art-3", "Article 3", DateTimeOffset.UtcNow.AddDays(-1)),
        ]));

        var page1 = await repo.GetPageAsync(1, 2);
        var page2 = await repo.GetPageAsync(2, 2);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNullWhenNoPublishedArticlesExist()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo([]));

        var result = await repo.GetBySlugAsync("any-slug");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsPublishedArticle()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("my-article", "My Article", DateTimeOffset.UtcNow),
        ]));

        var result = await repo.GetBySlugAsync("my-article");

        Assert.NotNull(result);
        Assert.Equal("my-article", result.Slug);
        Assert.Equal("My Article", result.Title);
    }

    [Fact]
    public async Task GetCountAsync_ReturnsPublishedCount()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("pub-1", "Published 1", DateTimeOffset.UtcNow.AddDays(-1)),
            Published("pub-2", "Published 2", DateTimeOffset.UtcNow),
        ]));

        var count = await repo.GetCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCountAsync_WithTag_ReturnsFilteredCount()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("art-a", "Article A", DateTimeOffset.UtcNow.AddDays(-2), tags: "queen,freddie"),
            Published("art-b", "Article B", DateTimeOffset.UtcNow.AddDays(-1), tags: "roger"),
            Published("art-c", "Article C", DateTimeOffset.UtcNow, tags: "queen,brian"),
        ]));

        Assert.Equal(2, await repo.GetCountAsync("queen"));
        Assert.Equal(1, await repo.GetCountAsync("roger"));
        Assert.Equal(0, await repo.GetCountAsync("john"));
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsPreviousAndNextArticle()
    {
        var t1 = DateTimeOffset.UtcNow.AddDays(-2);
        var t2 = DateTimeOffset.UtcNow.AddDays(-1);
        var t3 = DateTimeOffset.UtcNow;
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            // submitted in order; GetPublishedAsync returns desc by date
            Published("prev-art", "Previous Article", t1),
            Published("middle-art", "Middle Article", t2),
            Published("next-art", "Next Article", t3),
        ]));

        var (prev, next) = await repo.GetAdjacentAsync(t2);

        Assert.NotNull(prev);
        Assert.Equal("prev-art", prev.Slug);
        Assert.NotNull(next);
        Assert.Equal("next-art", next.Slug);
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsNulls_WhenOnlyOneArticle()
    {
        var t = DateTimeOffset.UtcNow;
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("only-art", "Only Article", t),
        ]));

        var (prev, next) = await repo.GetAdjacentAsync(t);

        Assert.Null(prev);
        Assert.Null(next);
    }

    [Fact]
    public async Task GetSitemapEntriesAsync_ReturnsPublished()
    {
        var repo = new InMemoryArticleRepository(new StubSubmissionRepo(
        [
            Published("sitemap-1", "Sitemap 1", DateTimeOffset.UtcNow.AddDays(-1)),
            Published("sitemap-2", "Sitemap 2", DateTimeOffset.UtcNow),
        ]));

        var result = await repo.GetSitemapEntriesAsync();

        Assert.Equal(2, result.Count);
    }

    // -------------------------------------------------------------------------
    // Web integration tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_Articles_Returns200_WithPublishedCommunityArticle()
    {
        var client = (await WithRepo([Published("test-community-slug", "Community Test Article", DateTimeOffset.UtcNow.AddDays(-1))])).CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.Contains("Community Test Article", body);
    }

    [Fact]
    public async Task Get_CommunityDetail_Returns200_ForPublishedArticle()
    {
        var client = (await WithRepo([Published("my-published-article", "My Published Article", DateTimeOffset.UtcNow.AddDays(-1))])).CreateClient();

        var response = await client.GetAsync("/articles/my-published-article");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("My Published Article", body);
    }

    [Fact]
    public async Task CommunityArticle_ListAndDetailLinkSubmittedByProfile()
    {
        var authorMemberId = Guid.NewGuid();
        var client = (await WithRepo(
        [
            Published(
                "linked-author",
                "Linked Author Article",
                DateTimeOffset.UtcNow.AddDays(-1),
                authorMemberId: authorMemberId),
        ])).CreateClient();

        var listBody = await client.GetStringAsync("/articles");
        var detailBody = await client.GetStringAsync("/articles/linked-author");

        Assert.Contains("class=\"qz-news-row\"", listBody);
        Assert.Contains("href=\"/articles/linked-author\"", listBody);
        Assert.Contains("Linked Author Article", listBody);
        Assert.Contains($"Submitted by <a class=\"qz-attribution-link\" href=\"/members/{authorMemberId}\">Test Author</a>", detailBody);
    }

    [Fact]
    public async Task Get_CommunityDetail_Returns404_ForUnknownSlug()
    {
        var client = (await WithRepo([])).CreateClient();

        var response = await client.GetAsync("/articles/does-not-exist");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_CommunityDetail_Returns404_WhenSlugNotInPublishedSet()
    {
        var client = (await WithRepo([Published("different-slug", "Different Article", DateTimeOffset.UtcNow)])).CreateClient();

        var response = await client.GetAsync("/articles/draft-only-slug");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_CommunityDetail_RendersAdjacentArticleNav()
    {
        var t1 = DateTimeOffset.UtcNow.AddDays(-2);
        var t2 = DateTimeOffset.UtcNow.AddDays(-1);
        var t3 = DateTimeOffset.UtcNow;
        var client = (await WithRepo(
        [
            Published("older-article", "Older Article", t1),
            Published("target-article", "Target Article", t2),
            Published("newer-article", "Newer Article", t3),
        ])).CreateClient();

        var body = await client.GetStringAsync("/articles/target-article");

        Assert.Contains("Older Article", body);
        Assert.Contains("Newer Article", body);
    }

    [Fact]
    public async Task Get_CommunityDetail_RendersReadTime()
    {
        var client = (await WithRepo([Published("read-time-article", "Read Time Article", DateTimeOffset.UtcNow.AddDays(-1))])).CreateClient();

        var body = await client.GetStringAsync("/articles/read-time-article");

        Assert.Contains("min read", body);
    }

    [Fact]
    public async Task Get_ArticlesFeed_Returns200_WithRssContent()
    {
        var client = (await WithRepo([Published("rss-test-article", "RSS Test Article", DateTimeOffset.UtcNow.AddDays(-1))])).CreateClient();

        var response = await client.GetAsync("/articles/feed.rss");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<rss", body);
        Assert.Contains("RSS Test Article", body);
        Assert.Contains("/articles/rss-test-article", body);
    }

    [Fact]
    public async Task Get_ArticlesFeed_WithNoArticles_ReturnsEmptyRss()
    {
        var client = (await WithRepo([])).CreateClient();

        var response = await client.GetAsync("/articles/feed.rss");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<rss", body);
        Assert.Contains("<channel>", body);
    }

    [Fact]
    public async Task Get_ArticlesFeed_WithExcerpt_IncludesDescription()
    {
        var client = (await WithRepo(
        [
            new PublishedArticleSubmission(
                Guid.NewGuid(), "Excerpted Article", "excerpted-article",
                "This is the excerpt.", "<p>Body.</p>",
                null, null, DateTimeOffset.UtcNow.AddDays(-1), "Author", 50),
        ])).CreateClient();

        var body = await client.GetStringAsync("/articles/feed.rss");

        Assert.Contains("This is the excerpt.", body);
    }

    [Fact]
    public async Task Get_Articles_TagFilter_ShowsOnlyMatchingArticles()
    {
        var client = (await WithRepo(
        [
            Published("tagged-article", "Tagged Article", DateTimeOffset.UtcNow.AddDays(-2), tags: "queen,freddie"),
            Published("other-article", "Other Article", DateTimeOffset.UtcNow.AddDays(-1), tags: "roger"),
        ])).CreateClient();

        var body = await client.GetStringAsync("/articles?tag=queen");

        Assert.Contains("Tagged Article", body);
        Assert.DoesNotContain("Other Article", body);
        Assert.DoesNotContain("/articles/101/", body);
        Assert.Contains("class=\"qz-news-row\"", body);
        Assert.Contains(">Article<", body);
    }

    [Fact]
    public async Task Get_Articles_EmptyExcerpt_DoesNotRenderEmptyParagraph()
    {
        var client = (await WithRepo(
        [
            new PublishedArticleSubmission(
                Guid.NewGuid(),
                "No Excerpt Article",
                "no-excerpt-article",
                "",
                "<p>Body.</p>",
                null,
                null,
                DateTimeOffset.UtcNow.AddDays(-1),
                "Author",
                40,
                Category: "Interviews"),
        ])).CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.Contains("No Excerpt Article", body);
        Assert.Contains(">Interviews<", body);
        Assert.DoesNotContain("<p></p>", body);
    }

    [Fact]
    public async Task Get_Articles_TagPageOutOfRange_Returns404()
    {
        var client = (await WithRepo(
            [Published("tagged-article", "Tagged Article", DateTimeOffset.UtcNow, tags: "queen")]))
            .CreateClient();

        var response = await client.GetAsync("/articles?tag=queen&page=99");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Articles_WhenLegacyCommunityPageQuery_RedirectsToArticles()
    {
        var client = (await WithRepo([Published("only-one", "Only One", DateTimeOffset.UtcNow)]))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/articles?cp=99");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/articles", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_Articles_WhenLegacyCommunityPageQueryWithTag_KeepsTag()
    {
        var client = (await WithRepo(
            [
                Published("tagged-article", "Tagged Article", DateTimeOffset.UtcNow, tags: "queen"),
            ]))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/articles?cp=2&tag=queen");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/articles?tag=queen", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_Articles_WhenCommunityRepoThrowsSqlException_StillReturnsArchive()
    {
        var client = variants.Get(WebHostVariants.TestingSqlFailingCommunityArticles).CreateClient();

        var response = await client.GetAsync("/articles");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Articles", body);
        Assert.DoesNotContain("qz-community-articles-header", body);
    }

    [Fact]
    public async Task Get_ArticlesFeed_WhenCommunityRepoThrowsSqlException_StillReturnsArchiveRss()
    {
        var client = variants.Get(WebHostVariants.TestingSqlFailingCommunityArticles).CreateClient();

        var response = await client.GetAsync("/articles/feed.rss");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<rss", body);
        Assert.Contains("<channel>", body);
        // Community table may be unavailable; legacy archive items still syndicate.
        Assert.Contains("Inside the Making of Bohemian Rhapsody", body);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<WebApplicationFactory<Program>> WithRepo(IEnumerable<PublishedArticleSubmission> seed)
    {
        var host = variants.Get(WebHostVariants.TestingMutableCommunityArticles);
        await host.ResetAsync();
        var repository = host.CommunityArticles
            ?? throw new InvalidOperationException("TestingMutableCommunityArticles must register MutableCommunityArticleRepository.");
        repository.Seed(seed);
        return host;
    }

    private static PublishedArticleSubmission Published(
        string slug, string title, DateTimeOffset publishedAt,
        string? tags = null,
        Guid? authorMemberId = null) =>
        new(
            Guid.NewGuid(),
            title,
            slug,
            "Test excerpt.",
            "<p>Body text content.</p>",
            null,
            tags,
            publishedAt,
            "Test Author",
            100,
            authorMemberId);

    // Stub IArticleSubmissionRepository — only GetPublishedAsync is used by InMemoryArticleRepository
    private sealed class StubSubmissionRepo(IEnumerable<PublishedArticleSubmission> published) : IArticleSubmissionRepository
    {
        private readonly IReadOnlyList<PublishedArticleSubmission> items = [.. published];

        public Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedAsync(CancellationToken ct = default) =>
            Task.FromResult(items);

        public Task<ArticleSubmission> UpsertDraftAsync(ArticleSubmissionDraft d, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ArticleSubmission?> SubmitForReviewAsync(Guid id, Guid m, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<SubmissionListPage<ArticleSubmission>> GetDraftsForMemberAsync(
            Guid m, int page = 1, int pageSize = 10, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ArticleSubmissionListItem>> GetPendingAsync(int p, int s, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ArticleSubmission?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ArticleSubmission?> UpdateStatusAsync(Guid id, string status, string? re,
            string? n, string? rr, string? sl = null, string? ex = null, string? t = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<SubmissionTypeCounts> GetDashboardCountsAsync(DateTimeOffset utcNow, CancellationToken ct = default) =>
            Task.FromResult(SubmissionTypeCounts.Empty);
        public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
            DateTimeOffset monthStart, int maxCount, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubmissionContributor>>([]);
    }

    // Minimal IArticleRepository stub for web integration tests
    private sealed class StubArticleRepo(IEnumerable<PublishedArticleSubmission> seed) : IArticleRepository
    {
        private readonly List<PublishedArticleSubmission> items = [.. seed.OrderByDescending(a => a.PublishedAt)];

        private static bool HasTag(string? tags, string tag) =>
            !string.IsNullOrWhiteSpace(tags) &&
            ("," + tags + ",").Contains("," + tag + ",", StringComparison.OrdinalIgnoreCase);

        public Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default) =>
            Task.FromResult(string.IsNullOrWhiteSpace(tag) ? items.Count : items.Count(a => HasTag(a.Tags, tag)));

        public Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(
            string? tag = null, CancellationToken ct = default)
        {
            IEnumerable<PublishedArticleSubmission> filtered = string.IsNullOrWhiteSpace(tag)
                ? items
                : items.Where(a => HasTag(a.Tags, tag));
            return Task.FromResult<IReadOnlyList<ArticleFeedKey>>(
                filtered.Select(a => ArticleFeedKey.Community(a.Id, a.PublishedAt.UtcDateTime)).ToList());
        }

        public Task<IReadOnlyList<PublishedArticleSubmission>> GetPublishedByIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            var set = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>(
                items.Where(a => set.Contains(a.Id)).ToList());
        }

        public Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
            int page, int pageSize, string? tag = null, CancellationToken ct = default)
        {
            IReadOnlyList<PublishedArticleSubmission> result = (string.IsNullOrWhiteSpace(tag)
                ? items : items.Where(a => HasTag(a.Tags, tag)))
                .Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(result);
        }

        public Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(items.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase)));

        public Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
            DateTimeOffset publishedAt, CancellationToken ct = default)
        {
            var prev = items.FirstOrDefault(a => a.PublishedAt < publishedAt);
            var next = items.LastOrDefault(a => a.PublishedAt > publishedAt);
            return Task.FromResult<(PublishedArticleSubmission?, PublishedArticleSubmission?)>((prev, next));
        }

        public Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PublishedArticleSubmission>>(items);
    }
}
