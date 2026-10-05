using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;
using QueenZone.Routing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ArticleMergedFeedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(41)]
    public async Task Concatenating_pages_matches_full_ordered_list(int totalCount)
    {
        var cache = CreateInterleavedFeed(totalCount, identicalTimestamps: totalCount >= 2);
        var index = await cache.GetMergedArticleFeedIndexAsync();
        Assert.Equal(totalCount, index.Count);

        var pages = new List<ArticleArchiveItem>();
        var totalPages = ArticlesRoutes.GetArchiveTotalPages(index.Count);
        if (totalPages == 0)
        {
            Assert.Empty(await cache.HydrateArticleFeedAsync(index));
            return;
        }

        for (var page = 1; page <= totalPages; page++)
        {
            var slice = index
                .Skip((page - 1) * ArticlesRoutes.ArchivePageSize)
                .Take(ArticlesRoutes.ArchivePageSize)
                .ToList();
            pages.AddRange(await cache.HydrateArticleFeedAsync(slice));
        }

        var full = await cache.HydrateArticleFeedAsync(index);
        Assert.Equal(full.Select(item => item.DetailPath), pages.Select(item => item.DetailPath));
        Assert.Equal(full.Count, pages.DistinctBy(item => item.DetailPath).Count());
    }

    [Fact]
    public async Task Overlay_date_reorders_an_archive_item()
    {
        var editorial = new InMemoryEditorialArticleRepository();
        var archive = new QueenZone.Data.InMemoryArticlesRepository(
            [
                Item(1, "Older archive", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Item(2, "Newer archive", new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            ],
            editorial);
        var draft = await editorial.SaveDraftAsync(
            Overlay(1, "Reordered archive", DateTimeOffset.Parse("2024-01-01T00:00:00Z")),
            "admin");
        await editorial.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");

        var cache = PublicQueryCacheServiceTests.CreateService(
            new MemoryCache(new MemoryCacheOptions()),
            articlesRepository: archive,
            communityArticleRepository: new EmptyArticleRepository());
        var index = await cache.GetMergedArticleFeedIndexAsync();

        Assert.Equal([1, 2], index.Select(key => key.ArchiveId));
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), index[0].PublishedAtUtc);
    }

    [Fact]
    public async Task Overlay_unpublish_excludes_the_item_and_shrinks_the_total()
    {
        var editorial = new InMemoryEditorialArticleRepository();
        var archive = new QueenZone.Data.InMemoryArticlesRepository(
            [
                Item(1, "Hidden archive", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Item(2, "Visible archive", new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            ],
            editorial);
        var draft = await editorial.SaveDraftAsync(
            Overlay(1, "Hidden overlay", DateTimeOffset.Parse("2024-01-01T00:00:00Z")),
            "admin");
        await editorial.SetStatusAsync(draft.Id, EditorialArticleStatus.Published, "admin");
        await editorial.SetStatusAsync(draft.Id, EditorialArticleStatus.Unpublished, "admin");

        var cache = PublicQueryCacheServiceTests.CreateService(
            new MemoryCache(new MemoryCacheOptions()),
            articlesRepository: archive,
            communityArticleRepository: new EmptyArticleRepository());
        var index = await cache.GetMergedArticleFeedIndexAsync();

        Assert.Equal([2], index.Select(key => key.ArchiveId));
    }

    [Fact]
    public void DetailPath_dedupe_keeps_the_first_item_in_total_order()
    {
        var first = new ArticleArchiveItem(1, "First", "One", DateTime.UtcNow, "Article", "/articles/same-slug");
        var second = new ArticleArchiveItem(2, "Second", "Two", DateTime.UtcNow, "Article", "/articles/same-slug");

        var deduped = PublicContentMapper.DedupeArticleArchiveItemsByDetailPath([first, second]);

        Assert.Single(deduped);
        Assert.Equal("First", deduped[0].Title);
    }

    [Fact]
    public async Task Community_sql_failure_returns_archive_only_and_is_not_cached()
    {
        var archive = new QueenZone.Data.InMemoryArticlesRepository(
            [Item(5, "Archive only", new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc))]);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = PublicQueryCacheServiceTests.CreateService(
            memory,
            articlesRepository: archive,
            communityArticleRepository: new SqlFailingCommunityArticleRepository());

        var first = await cache.GetMergedArticleFeedIndexAsync();
        Assert.Equal([5], first.Select(key => key.ArchiveId));

        cache.InvalidateArticlesCache();
        var second = await cache.GetMergedArticleFeedIndexAsync();
        Assert.Equal([5], second.Select(key => key.ArchiveId));
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Publish_invalidation_rebuilds_the_cached_index()
    {
        var archive = new MutableArticlesRepository(
            [Item(1, "Only archive", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))]);
        var cache = PublicQueryCacheServiceTests.CreateService(
            new MemoryCache(new MemoryCacheOptions()),
            articlesRepository: archive,
            communityArticleRepository: new EmptyArticleRepository());

        var first = await cache.GetMergedArticleFeedIndexAsync();
        Assert.Single(first);

        archive.Replace([
            Item(1, "Only archive", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Item(2, "Published later", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        ]);
        Assert.Single(await cache.GetMergedArticleFeedIndexAsync());

        cache.InvalidateArticlesCache();
        var refreshed = await cache.GetMergedArticleFeedIndexAsync();
        Assert.Equal([2, 1], refreshed.Select(key => key.ArchiveId));
    }

    private static PublicQueryCacheService CreateInterleavedFeed(int totalCount, bool identicalTimestamps)
    {
        var archive = new List<ArticleItem>();
        var community = new List<PublishedArticleSubmission>();
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < totalCount; i++)
        {
            var date = identicalTimestamps && i % 2 == 1 ? start.AddDays(i - 1) : start.AddDays(i);
            if (i % 2 == 0)
            {
                var id = 100 + i;
                archive.Add(Item(id, $"Archive {id}", date));
            }
            else
            {
                var id = Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}");
                community.Add(new PublishedArticleSubmission(
                    id,
                    $"Community {i}",
                    $"community-{i}",
                    $"Excerpt community-{i}",
                    "<p>Body</p>",
                    null,
                    null,
                    new DateTimeOffset(date, TimeSpan.Zero),
                    "Author",
                    50));
            }
        }

        var communityRepo = new MutableCommunityArticleRepository();
        communityRepo.Seed(community);
        return PublicQueryCacheServiceTests.CreateService(
            new MemoryCache(new MemoryCacheOptions()),
            articlesRepository: new QueenZone.Data.InMemoryArticlesRepository(archive),
            communityArticleRepository: communityRepo);
    }

    private static ArticleItem Item(int id, string title, DateTime publishedAt) =>
        new(id, title, $"Excerpt {id}", $"<p>{title}</p>", publishedAt, null, "Features", true);

    private static EditorialArticleDraft Overlay(int legacyId, string title, DateTimeOffset publishedAt) =>
        new(null, legacyId, null, title, null, "Excerpt", "<p>Body</p>", "Editor", "Features", null, null, null, publishedAt);

    private sealed class MutableArticlesRepository(IReadOnlyList<ArticleItem> seed) : IArticlesRepository
    {
        private IReadOnlyList<ArticleItem> items = seed;

        public void Replace(IReadOnlyList<ArticleItem> next) => items = next;

        public Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleItem>>(items.Take(count).ToList());

        public Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(
            int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleItem>>(items.Skip(Math.Max(page - 1, 0) * pageSize).Take(pageSize).ToList());

        public Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(items.Count);

        public Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleFeedKey>>(
                items.Select(item => ArticleFeedKey.Archive(item.Id, item.PublishedAt)).ToList());

        public Task<IReadOnlyList<ArticleItem>> GetPublishedByIdsAsync(
            IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        {
            var set = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<ArticleItem>>(items.Where(item => set.Contains(item.Id)).ToList());
        }

        public Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SitemapContentEntry>>(
                items.Select(item => new SitemapContentEntry(item.Id, item.Title, item.PublishedAt)).ToList());
    }
}
