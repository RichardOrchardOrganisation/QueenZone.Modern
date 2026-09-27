using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>Read-only SQL Express mirror probe for the NEWS_T_SearchPublished full-text procedure.</summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfNewsFullTextSearchLiveProbeTests
{
    [Fact]
    public async Task Search_materializes_published_rows_count_and_pages_when_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_NEWS_FTS_PROBE"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var services = new ServiceCollection();
        services.AddQueenZoneLegacyData(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<INewsRepository>();

        var first = await repository.SearchAsync("Queen", page: 1, pageSize: 5);
        Assert.True(first.TotalCount > 0, "The mirror should contain published Queen news matches.");
        Assert.NotEmpty(first.Items);
        Assert.True(first.TotalCount >= first.Items.Count);
        Assert.All(first.Items, item => Assert.True(item.IsPublished));
        Assert.Equal(first.Items.Count, first.Items.Select(item => item.Id).Distinct().Count());

        var second = await repository.SearchAsync("Queen", page: 2, pageSize: 5);
        Assert.Equal(first.TotalCount, second.TotalCount);
        Assert.True(second.Items.Count <= 5);
        Assert.All(second.Items, item => Assert.True(item.IsPublished));
        Assert.DoesNotContain(second.Items, item => first.Items.Any(firstItem => firstItem.Id == item.Id));

        var missing = await repository.SearchAsync("queenzoneunlikelytermzzzxqv", page: 1, pageSize: 5);
        Assert.Equal(0, missing.TotalCount);
        Assert.Empty(missing.Items);
    }
}
