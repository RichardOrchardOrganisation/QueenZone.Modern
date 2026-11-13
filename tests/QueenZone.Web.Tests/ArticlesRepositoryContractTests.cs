using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class ArticlesRepositoryContractTests
{
    protected abstract IArticlesRepository Repository { get; }
    protected abstract IEditorialArticleRepository Editorial { get; }
    protected abstract Task SeedAsync(ArticleItem item);
    private static ArticleItem Article(int id, DateTime date, bool published = true) =>
        new(id, $"Archive {id}", "Archive body", "Archive body", date, "Magazine", "Interviews", published);

    [Fact]
    public async Task Published_archive_lists_page_in_date_and_id_order_and_omit_body()
    {
        await SeedAsync(Article(1, new(2008, 3, 4)));
        await SeedAsync(Article(2, new(2020, 6, 2)));
        await SeedAsync(Article(3, new(2020, 6, 2)));
        await SeedAsync(Article(4, new(2026, 9, 1), false));
        var latest = await Repository.GetLatestAsync(10);
        Assert.Equal(new[] { 3, 2, 1 }, latest.Select(x => x.Id));
        Assert.All(latest, x => Assert.Empty(x.Body));
        Assert.Equal(3, Assert.Single(await Repository.GetLatestAsync(1)).Id);
        Assert.Equal(3, Assert.Single(await Repository.GetLatestAsync(0)).Id);
        Assert.Equal(3, Assert.Single(await Repository.GetArchivePageAsync(1, 1)).Id);
        Assert.Equal(2, Assert.Single(await Repository.GetArchivePageAsync(2, 1)).Id);
        Assert.Equal(3, Assert.Single(await Repository.GetArchivePageAsync(0, 0)).Id);
        Assert.All(await Repository.GetArchivePageAsync(1, 10), x => Assert.Empty(x.Body));
        Assert.Equal(3, await Repository.GetPublishedCountAsync());
        Assert.Equal(new[] { 1, 2, 3 }, (await Repository.GetPublishedFeedKeysAsync()).Select(x => x.ArchiveId).Order());
        Assert.Equal(new[] { 1, 2, 3 }, (await Repository.GetPublishedSitemapEntriesAsync()).Select(x => x.Id).Order());
        var bulk = await Repository.GetPublishedByIdsAsync([1, 2, 2, 3, 4, -1]);
        Assert.Equal(new[] { 1, 2, 3 }, bulk.Select(x => x.Id).Order());
        Assert.All(bulk, x => Assert.Empty(x.Body));
        Assert.Empty(await Repository.GetPublishedByIdsAsync([]));
        Assert.Null(await Repository.GetByIdAsync(-1));
        Assert.Null(await Repository.GetByIdAsync(4));
        var detail = await Repository.GetByIdAsync(1);
        Assert.Equal("Archive 1", detail!.Title);
        Assert.Equal("Archive body", detail.Body);
    }

    [Fact]
    public async Task Latest_and_archive_sizes_are_capped_at_one_hundred()
    {
        for (var id = 1; id <= 101; id++)
        {
            await SeedAsync(Article(id, new(2020, 6, 2)));
        }
        Assert.Equal(100, (await Repository.GetLatestAsync(1000)).Count);
        Assert.Equal(100, (await Repository.GetArchivePageAsync(1, 1000)).Count);
        Assert.Single(await Repository.GetArchivePageAsync(2, 1000));
    }

    [Fact]
    public async Task Published_overlay_changes_detail_and_unpublished_overlay_hides_every_public_surface()
    {
        await SeedAsync(Article(101, new(2008, 3, 4)));
        var overlay = await Editorial.SaveDraftAsync(EditorialArticleRepositoryContractTests.Draft("Edited archive") with { LegacyArticleId = 101 }, "editor");
        await Editorial.SetStatusAsync(overlay.Id, EditorialArticleStatus.Published, "editor");
        var detail = await Repository.GetByIdAsync(101);
        Assert.Equal("Edited archive", detail!.Title);
        Assert.Equal("Excerpt", detail.Excerpt);
        Assert.Equal("<p>Body</p>", detail.Body);
        Assert.Empty(Assert.Single(await Repository.GetLatestAsync(10)).Body);
        Assert.Empty(Assert.Single(await Repository.GetPublishedByIdsAsync([101])).Body);
        await Editorial.SetStatusAsync(overlay.Id, EditorialArticleStatus.Unpublished, "editor");
        Assert.Null(await Repository.GetByIdAsync(101));
        Assert.Empty(await Repository.GetLatestAsync(10));
        Assert.Empty(await Repository.GetArchivePageAsync(1, 10));
        Assert.Empty(await Repository.GetPublishedByIdsAsync([101]));
        Assert.Equal(0, await Repository.GetPublishedCountAsync());
        Assert.Empty(await Repository.GetPublishedSitemapEntriesAsync());
        Assert.Empty(await Repository.GetPublishedFeedKeysAsync());
    }
}

public sealed class InMemoryArticlesRepositoryContractTests : ArticlesRepositoryContractTests
{
    private readonly List<ArticleItem> articles = [];
    protected override IEditorialArticleRepository Editorial { get; } = new InMemoryEditorialArticleRepository(new NewsEditorialContractClock());
    protected override IArticlesRepository Repository => new QueenZone.Data.InMemoryArticlesRepository(articles, Editorial);
    protected override Task SeedAsync(ArticleItem item)
    {
        articles.Add(item);
        return Task.CompletedTask;
    }
}

public sealed class EfArticlesRepositoryContractTests : ArticlesRepositoryContractTests, IAsyncDisposable
{
    private readonly NewsEditorialContractFixture fixture = new();
    protected override IEditorialArticleRepository Editorial => fixture.Editorial;
    protected override IArticlesRepository Repository { get; }
    public EfArticlesRepositoryContractTests() => Repository = fixture.Articles();
    protected override async Task SeedAsync(ArticleItem item) =>
        await fixture.Context.Database.ExecuteSqlAsync($"""
            INSERT INTO Articles (Id, Title, Body, PublishedAt, Source, CategoryName, IsPublished)
            VALUES ({item.Id}, {item.Title}, {item.Body}, {item.PublishedAt}, {item.Source}, {item.CategoryName}, {item.IsPublished});
            """);
    public ValueTask DisposeAsync() => fixture.DisposeAsync();
}
