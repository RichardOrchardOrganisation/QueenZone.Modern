using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

// ---------------------------------------------------------------------------
// InMemory repository: unit tests for the in-memory LINQ search path
// ---------------------------------------------------------------------------

public sealed class InMemoryNewsSearchTests
{
    private static FixedNewsRepository CreateRepository() =>
        new(SampleNewsData.CreateSeedArticles()
            .Select(a => new NewsItem(
                a.Id, a.Title, a.Excerpt, a.Body, a.PublishedAt, a.SourceUrl, a.IsPublished,
                string.IsNullOrWhiteSpace(a.Slug) ? null : a.Slug,
                ImageBlobKey: a.ImageBlobKey,
                ImageGalleryPicId: a.ImageGalleryPicId)));

    [Fact]
    public async Task SearchReturnsResultsMatchingTitle()
    {
        var repo = CreateRepository();

        var page = await repo.SearchAsync("modernisation", 1, 20);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item =>
            Assert.True(
                item.Title.Contains("modernisation", StringComparison.OrdinalIgnoreCase) ||
                item.Excerpt.Contains("modernisation", StringComparison.OrdinalIgnoreCase) ||
                item.Body.Contains("modernisation", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task SearchIsCaseInsensitive()
    {
        var repo = CreateRepository();

        var lower = await repo.SearchAsync("modernisation", 1, 20);
        var upper = await repo.SearchAsync("MODERNISATION", 1, 20);

        Assert.Equal(lower.TotalCount, upper.TotalCount);
    }

    [Fact]
    public async Task SearchReturnsEmptyForNoMatch()
    {
        var repo = CreateRepository();

        var page = await repo.SearchAsync("xyzzy_no_match_zzzqq", 1, 20);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task SearchReturnsEmptyForBlankQuery()
    {
        var repo = CreateRepository();

        var page = await repo.SearchAsync("   ", 1, 20);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task SearchNeverReturnsHiddenOrDraftRecords()
    {
        var repo = CreateRepository();

        var page = await repo.SearchAsync("moderation", 1, 20);

        Assert.Empty(page.Items);
        Assert.DoesNotContain(page.Items, item => item.Title.Contains("Hidden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchMatchesExcerpt()
    {
        var repo = CreateRepository();

        // ID 1002 excerpt mentions "canonical routes"
        var page = await repo.SearchAsync("canonical routes", 1, 20);

        Assert.NotEmpty(page.Items);
    }

    [Fact]
    public async Task SearchMatchesBody()
    {
        var repo = CreateRepository();

        // SampleNewsData bodies contain "Body for archive sample article {id}"
        var page = await repo.SearchAsync("Body for archive sample", 1, 20);

        Assert.NotEmpty(page.Items);
    }

    [Fact]
    public async Task SearchRespectsPagination()
    {
        var repo = CreateRepository();

        // "archive" matches IDs 1002 and 1004–1022 (many items)
        var pageOne = await repo.SearchAsync("archive", 1, 3);
        var pageTwo = await repo.SearchAsync("archive", 2, 3);

        Assert.Equal(3, pageOne.Items.Count);
        Assert.True(pageOne.TotalCount > 3);
        Assert.NotEqual(pageOne.Items[0].Id, pageTwo.Items[0].Id);
    }

    [Fact]
    public async Task SearchTotalCountReflectsAllMatches()
    {
        var repo = CreateRepository();

        var allResults = await repo.SearchAsync("archive", 1, 100);
        var pagedResults = await repo.SearchAsync("archive", 1, 3);

        Assert.Equal(allResults.TotalCount, pagedResults.TotalCount);
        Assert.True(pagedResults.TotalCount > pagedResults.Items.Count);
    }
}

// ---------------------------------------------------------------------------
// EfNewsRepository: provider-agnostic SearchAsync early return.
// Matching, ranking, and paging live in NewsSearchSqlServerTests (#1875 / #1882).
// ---------------------------------------------------------------------------

public sealed class EfNewsRepositorySearchBlankQueryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfNewsRepository repository;

    public EfNewsRepositorySearchBlankQueryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        repository = new EfNewsRepository(
            dbContext,
            new NewsRepositorySqlTemplates(
                LatestSql: string.Empty,
                CountSql: string.Empty,
                ArchivePageSql: string.Empty,
                ByIdSql: string.Empty,
                SitemapSql: string.Empty));
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SearchAsync_returns_empty_for_blank_query()
    {
        var result = await repository.SearchAsync("   ", 1, 20);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }
}

// ---------------------------------------------------------------------------
// Web integration tests for /news/search route
// ---------------------------------------------------------------------------

public sealed class NewsSearchRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public NewsSearchRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    private HttpClient CreateNonRedirectingClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task NewsSearchPage_redirects_to_unified_search_without_query()
    {
        var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/news/search");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/search?type=news", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task NewsSearchPage_redirects_to_unified_search_with_query()
    {
        var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/news/search?q=modernisation");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/search?type=news&q=modernisation", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task NewsSearchPage_redirects_preserving_page_number()
    {
        var client = CreateNonRedirectingClient();

        var response = await client.GetAsync("/news/search?q=archive&page=2");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/search?type=news&q=archive&page=2", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task NewsSearchRedirect_lands_on_working_unified_search_page()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/news/search?q=modernisation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("QueenZone modernisation begins", body);
    }

    [Fact]
    public async Task NewsIndex_has_search_form_pointing_to_news_search()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news");

        Assert.Contains("action=\"/news/search\"", body);
    }
}
