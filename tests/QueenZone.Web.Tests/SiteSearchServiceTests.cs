using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class SiteSearchServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfSiteSearchService efSearch;

    public SiteSearchServiceTests()
    {
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        dbContext.Database.EnsureCreated();
        efSearch = new EfSiteSearchService(dbContext, NullLogger<EfSiteSearchService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData(" A ")]
    public async Task EfSearch_below_minimum_length_returns_empty_without_sql(string? query)
    {
        var result = await efSearch.SearchAsync(query!, null, 1, 20);

        Assert.Empty(result.Results);
        Assert.Equal(0, result.TotalCount);
        Assert.True(SiteSearchLimits.IsBelowMinimumLength(query));
    }

    [Fact]
    public void Tribute_is_not_a_selectable_search_type()
    {
        Assert.Equal(2, SiteSearchLimits.MinQueryLength);
        Assert.Equal(10, SiteSearchLimits.MaxPage);
        Assert.False(SiteSearchLimits.IsBeyondMaxPage(10));
        Assert.False(SiteSearchLimits.IsBeyondMaxPage(0));
        Assert.True(SiteSearchLimits.IsBeyondMaxPage(11));
        Assert.DoesNotContain(SiteSearchContentType.Tribute, SiteSearchContentType.All);
        Assert.DoesNotContain(SiteSearchContentType.FreddieTribute, SiteSearchContentType.All);
        Assert.Null(SiteSearchContentType.Normalize(SiteSearchContentType.Tribute));
        Assert.Null(SiteSearchContentType.Normalize(SiteSearchContentType.FreddieTribute));
        Assert.True(SiteSearchContentType.IsExcludedFromSiteSearch("TRIBUTE"));
        Assert.True(SiteSearchContentType.IsExcludedFromSiteSearch("Freddie-Tribute"));
        Assert.True(SiteSearchExclusion.IsExcluded(SiteSearchContentType.FreddieTribute, "news:3"));
        Assert.True(SiteSearchExclusion.IsExcluded(SiteSearchContentType.News, "tribute:12"));
        Assert.False(SiteSearchExclusion.IsExcluded(SiteSearchContentType.News, "news:3"));
        Assert.True(SearchDocumentSourceKey.IsTribute(SearchDocumentSourceKey.ForTribute(12)));
        Assert.True(SearchDocumentSourceKey.IsTribute("freddie-tribute:12"));
        Assert.False(SearchDocumentSourceKey.IsTribute(null));
        Assert.False(SearchDocumentSourceKey.IsTribute("   "));
        Assert.False(SearchDocumentSourceKey.IsTribute("news:3"));
        Assert.Equal(100, SiteSearchLimits.MaxPageSize);
        Assert.Equal(1, SiteSearchLimits.NormalizePage(0));
        Assert.Equal(100, SiteSearchLimits.NormalizePageSize(500));
        Assert.Equal(
            "ContentType IN (N'tribute', N'freddie-tribute') OR SourceKey LIKE N'tribute:%' OR SourceKey LIKE N'freddie-tribute:%'",
            SiteSearchExclusion.SqlIsExcluded("ContentType", "SourceKey"));
        Assert.Equal(
            "NOT (d.ContentType IN (N'tribute', N'freddie-tribute') OR d.SourceKey LIKE N'tribute:%' OR d.SourceKey LIKE N'freddie-tribute:%')",
            SiteSearchExclusion.SqlIsSearchable("d"));

        var leftover = new[]
        {
            new SearchDocumentEntity { ContentType = SiteSearchContentType.News, SourceKey = "news:3" },
            new SearchDocumentEntity { ContentType = SiteSearchContentType.Tribute, SourceKey = "news:4" },
            new SearchDocumentEntity { ContentType = SiteSearchContentType.News, SourceKey = "tribute:12" },
        }.AsQueryable();
        Assert.Equal(
            ["news:4", "tribute:12"],
            SiteSearchExclusion.WhereExcluded(leftover).Select(document => document.SourceKey).ToArray());
    }

    [Fact]
    public async Task InMemoryIndex_does_not_write_tribute_documents()
    {
        var store = new SharedSearchIndexStore();
        var index = new InMemorySearchIndexService(store);
        var leftover = new SearchDocumentEntity
        {
            SourceKey = SearchDocumentSourceKey.ForTribute(4),
            ContentType = SiteSearchContentType.Tribute,
            Title = "Old tribute",
            Body = "body",
            Summary = "summary",
            Url = "/freddie-mercury-tribute",
        };
        store.Upsert(leftover);

        await index.UpsertAsync(leftover);
        store.Upsert(new SearchDocumentEntity
        {
            SourceKey = "freddie-tribute:4",
            ContentType = SiteSearchContentType.FreddieTribute,
            Title = "Alias leftover",
            Body = "body",
            Summary = "summary",
            Url = "/freddie-mercury-tribute",
        });
        store.Upsert(new SearchDocumentEntity
        {
            SourceKey = SearchDocumentSourceKey.ForNews(9),
            ContentType = SiteSearchContentType.News,
            Title = "Keep this news",
            Body = "body",
            Summary = "summary",
            Url = "/news/9/keep-this-news",
        });

        await index.ReplaceContentTypeAsync(
            SiteSearchContentType.Tribute,
            [
                new SearchDocumentEntity
                {
                    SourceKey = SearchDocumentSourceKey.ForTribute(5),
                    ContentType = SiteSearchContentType.Tribute,
                    Title = "New tribute",
                    Body = "body",
                    Summary = "summary",
                    Url = "/freddie-mercury-tribute",
                },
            ]);

        var remaining = store.GetAll();
        Assert.Single(remaining);
        Assert.Equal("news:9", remaining[0].SourceKey);

        await index.ReplaceContentTypeAsync(
            SiteSearchContentType.News,
            [
                new SearchDocumentEntity
                {
                    SourceKey = SearchDocumentSourceKey.ForNews(10),
                    ContentType = SiteSearchContentType.News,
                    Title = "Replacement news",
                    Body = "body",
                    Summary = "summary",
                    Url = "/news/10/replacement-news",
                },
                new SearchDocumentEntity
                {
                    SourceKey = SearchDocumentSourceKey.ForTribute(11),
                    ContentType = SiteSearchContentType.News,
                    Title = "Should be skipped",
                    Body = "body",
                    Summary = "summary",
                    Url = "/news/11/skipped",
                },
            ]);

        remaining = store.GetAll();
        Assert.Single(remaining);
        Assert.Equal("news:10", remaining[0].SourceKey);
    }

    [Fact]
    public async Task InMemorySearch_never_returns_tribute_documents()
    {
        var store = new SharedSearchIndexStore();
        store.Upsert(new SearchDocumentEntity
        {
            SourceKey = SearchDocumentSourceKey.ForTribute(1012),
            ContentType = SiteSearchContentType.Tribute,
            Title = "Freddie, your voice still finds the exact place where joy and sorrow meet.",
            Body = "Thank you for teaching us to be fearless.",
            Summary = "Fearless tribute",
            Url = "/freddie-mercury-tribute",
        });
        store.Upsert(new SearchDocumentEntity
        {
            SourceKey = "freddie-tribute:3",
            ContentType = SiteSearchContentType.FreddieTribute,
            Title = "Freddie alias still fearless",
            Body = "Alias leftover",
            Summary = "Alias",
            Url = "/freddie-mercury-tribute",
        });
        store.Upsert(new SearchDocumentEntity
        {
            SourceKey = SearchDocumentSourceKey.ForTribute(4),
            ContentType = SiteSearchContentType.News,
            Title = "Freddie mismatched key is fearless",
            Body = "Should not appear",
            Summary = "Mismatch",
            Url = "/news/4/mismatch",
        });
        store.Upsert(new SearchDocumentEntity
        {
            SourceKey = SearchDocumentSourceKey.ForNews(3),
            ContentType = SiteSearchContentType.News,
            Title = "Freddie news about being fearless",
            Body = "Published news body",
            Summary = "News summary",
            Url = "/news/3/freddie-news",
        });

        var service = new InMemorySiteSearchService(store);

        var all = await service.SearchAsync("fearless", null, 1, 20);
        var typed = await service.SearchAsync("fearless", SiteSearchContentType.Tribute, 1, 20);

        Assert.Equal(1, all.TotalCount);
        Assert.Equal(SiteSearchContentType.News, Assert.Single(all.Results).ContentType);
        Assert.Empty(typed.Results);
        Assert.Equal(0, typed.TotalCount);
    }

    [Fact]
    public async Task InMemorySearch_beyond_max_page_returns_empty_with_true_total()
    {
        var store = new SharedSearchIndexStore();
        for (var index = 0; index < 25; index++)
        {
            store.Upsert(new SearchDocumentEntity
            {
                SourceKey = SearchDocumentSourceKey.ForNews(index + 1),
                ContentType = SiteSearchContentType.News,
                Title = $"Queen news {index}",
                Body = "body",
                Summary = "summary",
                Url = $"/news/{index + 1}/queen-news",
                PublishedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z").AddDays(index),
            });
        }

        var service = new InMemorySiteSearchService(store);
        var first = await service.SearchAsync("Queen", null, 1, 2);
        var deep = await service.SearchAsync("Queen", null, SiteSearchLimits.MaxPage + 1, 2);

        Assert.Equal(25, first.TotalCount);
        Assert.Equal(2, first.Results.Count);
        Assert.Equal(25, deep.TotalCount);
        Assert.Empty(deep.Results);
        Assert.Equal(11, deep.Page);
        Assert.Equal(2, deep.PageSize);
    }

    [Fact]
    public void Empty_page_with_total_keeps_requested_page_and_true_count()
    {
        var page = EfSiteSearchService.EmptyPageWithTotal(37, 11, 20);

        Assert.Empty(page.Results);
        Assert.Equal(37, page.TotalCount);
        Assert.Equal(11, page.Page);
        Assert.Equal(20, page.PageSize);
    }
}
