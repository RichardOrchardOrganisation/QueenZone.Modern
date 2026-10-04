using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>Opt-in deterministic browser content. Never publishes to a database-backed catalog.</summary>
internal static class CrosswordBrowserFixture
{
    public static async Task SeedAsync(IHostEnvironment environment, bool enabled, IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        if (!enabled || !environment.IsEnvironment(QueenZoneEnvironments.Testing)) return;
        using var scope = services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICrosswordCatalogRepository>();
        if (catalog is not InMemoryCrosswordCatalogRepository)
            throw new InvalidOperationException("Crossword browser fixtures require an in-memory Testing catalog.");
        foreach (var puzzle in await catalog.GetAllAsync(cancellationToken))
        {
            if (puzzle.Status == CrosswordStatus.Draft)
                await catalog.SetPublicationAsync(puzzle.Id, CrosswordStatus.Published, null, puzzle.RowVersion,
                    "crossword-browser-fixture", cancellationToken);
        }
    }
}
