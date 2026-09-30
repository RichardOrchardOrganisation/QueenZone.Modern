using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only SQL Express mirror probe for <c>dbo.SearchDocument_Search</c> via
/// <see cref="EfSiteSearchService"/> (#1895). Gated like the news FTS probe:
/// returns early unless <c>RUN_SITE_SEARCH_FTS_PROBE=true</c>. Scratch-schema
/// coverage (FREETEXTTABLE swapped for LIKE) lives in
/// <c>SearchDocumentSearchSqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfSiteSearchFullTextSearchLiveProbeTests
{
    [Fact]
    public async Task Search_materializes_ranked_pages_and_excludes_tributes_when_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_SITE_SEARCH_FTS_PROBE"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueenZoneLegacyData(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<ISiteSearchService>();
        Assert.IsType<EfSiteSearchService>(search);

        var first = await search.SearchAsync("Queen", null, page: 1, pageSize: 5);
        Assert.True(first.TotalCount > 0, "The mirror SearchDocument index should contain Queen matches.");
        Assert.NotEmpty(first.Results);
        Assert.True(first.TotalCount >= first.Results.Count);
        Assert.Equal(first.Results.Count, first.Results.Select(item => item.SourceKey).Distinct().Count());
        Assert.All(first.Results, item =>
        {
            Assert.False(SiteSearchContentType.IsExcludedFromSiteSearch(item.ContentType));
            Assert.False(SearchDocumentSourceKey.IsTribute(item.SourceKey));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Url));
        });

        var second = await search.SearchAsync("Queen", null, page: 2, pageSize: 5);
        Assert.Equal(first.TotalCount, second.TotalCount);
        Assert.True(second.Results.Count <= 5);
        Assert.DoesNotContain(
            second.Results,
            item => first.Results.Any(firstItem => firstItem.SourceKey == item.SourceKey));

        var typedNews = await search.SearchAsync("Queen", SiteSearchContentType.News, page: 1, pageSize: 5);
        Assert.True(typedNews.TotalCount >= typedNews.Results.Count);
        Assert.All(typedNews.Results, item => Assert.Equal(SiteSearchContentType.News, item.ContentType));

        var typedTribute = await search.SearchAsync("Queen", SiteSearchContentType.Tribute, page: 1, pageSize: 5);
        Assert.Equal(0, typedTribute.TotalCount);
        Assert.Empty(typedTribute.Results);

        var missing = await search.SearchAsync("queenzoneunlikelytermzzzxqv", null, page: 1, pageSize: 5);
        Assert.Equal(0, missing.TotalCount);
        Assert.Empty(missing.Results);
    }
}
