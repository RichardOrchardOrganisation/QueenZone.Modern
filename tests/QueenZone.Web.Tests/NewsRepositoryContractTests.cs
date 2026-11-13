using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class NewsRepositoryContractTests
{
    protected abstract INewsRepository Repository { get; }
    protected abstract IAdminNewsRepository Admin { get; }
    private static readonly DateTime Recent = new(2020, 6, 2, 0, 0, 0, DateTimeKind.Utc);

    private async Task<int> Seed(string title, DateTime date, bool published = true)
    {
        var draft = new AdminNewsDraft(title, null, "Excerpt " + title, "Body " + title, date, null);
        var id = await Admin.CreateDraftAsync(draft, "editor@test.local");
        if (published)
        {
            await Admin.PublishAsync(id, "editor@test.local");
            await Admin.UpdateAsync(id, draft, "editor@test.local");
        }
        return id;
    }

    [Fact]
    public async Task Lists_page_published_rows_in_date_and_id_order_without_body()
    {
        var old = await Seed("Archive", new(2008, 3, 4));
        var first = await Seed("Recent first", Recent);
        var last = await Seed("Recent last", Recent);
        await Seed("Hidden", Recent.AddYears(1), false);
        Assert.Equal(new[] { last, first, old }, (await Repository.GetLatestAsync(10)).Select(x => x.Id));
        Assert.Equal(last, Assert.Single(await Repository.GetLatestAsync(1)).Id);
        Assert.Equal(last, Assert.Single(await Repository.GetLatestAsync(0)).Id);
        Assert.Equal(last, Assert.Single(await Repository.GetLatestAsync(-1)).Id);
        Assert.All(await Repository.GetLatestAsync(10), x => Assert.Empty(x.Body));
        Assert.Equal(last, Assert.Single(await Repository.GetArchivePageAsync(1, 1)).Id);
        Assert.Equal(first, Assert.Single(await Repository.GetArchivePageAsync(2, 1)).Id);
        Assert.Equal(last, Assert.Single(await Repository.GetArchivePageAsync(0, 0)).Id);
        Assert.All(await Repository.GetArchivePageAsync(1, 10), x => Assert.Empty(x.Body));
        Assert.Equal(3, await Repository.GetPublishedCountAsync());
    }

    [Fact]
    public async Task Filters_apply_to_the_archive_before_paging_and_year_range_ignores_drafts()
    {
        var old = await Seed("2008 archive", new(2008, 3, 4));
        await Seed("2020 first", Recent);
        await Seed("2020 second", Recent.AddDays(1));
        await Seed("2026 newest", new(2026, 9, 1));
        await Seed("Hidden old", new(1960, 1, 1), false);
        Assert.NotEqual(old, Assert.Single(await Repository.GetArchivePageAsync(1, 1)).Id);
        foreach (var filter in new[] { new NewsArchiveFilter(2000), new NewsArchiveFilter(null, 2008) })
        {
            Assert.Equal(1, await Repository.GetPublishedCountAsync(filter));
            Assert.Equal(old, Assert.Single(await Repository.GetArchivePageAsync(1, 1, filter)).Id);
            Assert.Empty(await Repository.GetArchivePageAsync(2, 1, filter));
        }
        Assert.Equal(0, await Repository.GetPublishedCountAsync(new(1980)));
        Assert.Empty(await Repository.GetArchivePageAsync(1, 10, new(1980)));
        Assert.Equal(new NewsArchiveYearRange(2008, 2026), await Repository.GetArchiveYearRangeAsync());
    }

    [Fact]
    public async Task Details_and_bulk_reads_preserve_body_and_omit_missing_and_unpublished_ids()
    {
        var id = await Seed("Public title", Recent);
        var hidden = await Seed("Draft", Recent, false);
        var item = await Repository.GetByIdAsync(id);
        Assert.NotNull(item);
        Assert.Equal("Public title", item.Title);
        Assert.Equal("Excerpt Public title", item.Excerpt);
        Assert.Equal("Body Public title", item.Body);
        Assert.Equal(Recent, item.PublishedAt);
        Assert.Equal("public-title", item.Slug);
        Assert.Null(await Repository.GetByIdAsync(hidden));
        Assert.Null(await Repository.GetByIdAsync(-1));
        Assert.Equal(item, Assert.Single(await Repository.GetByIdsAsync([id, id, hidden, -1])));
        Assert.Empty(await Repository.GetByIdsAsync([]));
        var entry = Assert.Single(await Repository.GetPublishedSitemapEntriesAsync());
        Assert.Equal(id, entry.Id);
        Assert.Equal(item.Title, entry.Title);
        Assert.Equal(Recent, entry.PublishedAt);
        Assert.Equal(item.Slug, entry.Slug);
    }

    [Fact]
    public async Task Latest_and_archive_sizes_are_capped_at_one_hundred()
    {
        for (var index = 0; index < 101; index++)
        {
            await Seed($"Article {index}", Recent);
        }
        Assert.Equal(100, (await Repository.GetLatestAsync(1000)).Count);
        Assert.Equal(100, (await Repository.GetArchivePageAsync(1, 1000)).Count);
        Assert.Single(await Repository.GetArchivePageAsync(2, 1000));
    }

    [Fact]
    public async Task Blank_search_is_empty_even_with_published_content()
    {
        await Seed("Searchable", Recent);
        foreach (var query in new[] { "", "   " })
        {
            var result = await Repository.SearchAsync(query, 1, 10);
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        }
    }
}

public sealed class InMemoryNewsRepositoryContractTests : NewsRepositoryContractTests
{
    private readonly SharedNewsStore store = new();
    protected override INewsRepository Repository => new InMemoryNewsRepository(store);
    protected override IAdminNewsRepository Admin => new InMemoryAdminNewsRepository(store);
}

public sealed class EfNewsRepositoryContractTests : NewsRepositoryContractTests, IAsyncDisposable
{
    private readonly NewsEditorialContractFixture fixture = new();
    protected override INewsRepository Repository { get; }
    protected override IAdminNewsRepository Admin { get; }
    public EfNewsRepositoryContractTests() => (Admin, Repository) = fixture.News();
    public ValueTask DisposeAsync() => fixture.DisposeAsync();
}
