using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordBrowserFixtureTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("E2E", true)]
    [InlineData("Testing", false)]
    public async Task Fixture_is_disabled_without_both_testing_environment_and_explicit_opt_in(string environment, bool enabled)
    {
        var catalog = System.Reflection.DispatchProxy.Create<ICrosswordCatalogRepository, RejectCatalog>();
        using var guarded = new ServiceCollection().AddSingleton(catalog).BuildServiceProvider();
        await CrosswordBrowserFixture.SeedAsync(new Host(environment), enabled, guarded);
        Assert.Equal(0, ((RejectCatalog)catalog).Calls);
    }

    [Fact]
    public async Task Fixture_only_publishes_in_memory_drafts_and_is_idempotent()
    {
        var catalog = new InMemoryCrosswordCatalogRepository(TimeProvider.System);
        await catalog.ImportAsync(CrosswordSampleData.Load(), Guid.Empty, "seed");
        using var services = new ServiceCollection().AddSingleton<ICrosswordCatalogRepository>(catalog).BuildServiceProvider();
        await CrosswordBrowserFixture.SeedAsync(new Host("Testing"), true, services);
        var puzzles = await catalog.GetAllAsync();
        Assert.Equal(10, puzzles.Count);
        Assert.All(puzzles, puzzle => Assert.Equal(CrosswordStatus.Published, puzzle.Status));
        await CrosswordBrowserFixture.SeedAsync(new Host("Testing"), true, services);
        foreach (var puzzle in puzzles) Assert.Equivalent(puzzle, await catalog.GetByIdAsync(puzzle.Id));
    }

    [Fact]
    public async Task Database_or_unknown_catalog_is_rejected_before_any_fixture_write()
    {
        var catalog = System.Reflection.DispatchProxy.Create<ICrosswordCatalogRepository, RejectCatalog>();
        using var services = new ServiceCollection().AddSingleton(catalog).BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CrosswordBrowserFixture.SeedAsync(new Host("Testing"), true, services));
        Assert.Equal(0, ((RejectCatalog)catalog).Calls);
    }

    private sealed class Host(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Crossword fixture tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public class RejectCatalog : System.Reflection.DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("A non-memory catalog must never be called by fixtures.");
        }
    }
}

public sealed class CrosswordBrowserFixtureProductionConfigTests
{
    [Fact]
    public void Production_host_config_does_not_enable_crossword_browser_fixture()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(RepoPaths.Combine("src", "QueenZone.Web", "appsettings.json"))
            .Build();
        Assert.False(shipped.GetValue<bool>("CrosswordBrowserFixture:Enabled"));
        Assert.False(
            ProductionHostSettings.Values.TryGetValue("CrosswordBrowserFixture:Enabled", out var stub)
            && bool.TryParse(stub, out var enabled)
            && enabled);
        var production = new ConfigurationBuilder()
            .AddJsonFile(RepoPaths.Combine("src", "QueenZone.Web", "appsettings.json"))
            .AddInMemoryCollection(ProductionHostSettings.Values)
            .Build();
        Assert.False(production.GetValue<bool>("CrosswordBrowserFixture:Enabled"));
    }
}
