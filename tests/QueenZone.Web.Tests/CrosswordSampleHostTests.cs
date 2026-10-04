using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordSampleHostTests(QueenZoneWebApplicationFactory factory) : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Fact]
    public async Task Testing_resolves_in_memory_catalog_with_the_ten_draft_seed_files()
    {
        var catalog = factory.Services.GetRequiredService<ICrosswordCatalogRepository>();
        Assert.IsType<InMemoryCrosswordCatalogRepository>(catalog);
        var items = await catalog.GetAllAsync();
        Assert.Equal(10, items.Count);
        Assert.All(items, item => Assert.Equal(CrosswordStatus.Draft, item.Status));
        Assert.Equal(CrosswordSampleData.Load().Select(seed => seed.Slug).Order(StringComparer.Ordinal),
            items.Select(item => item.Seed.Slug));
        Assert.Null(factory.Services.GetService<QueenZoneDbContext>());
    }
}
