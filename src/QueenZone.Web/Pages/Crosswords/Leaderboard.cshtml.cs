using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Crosswords;

public sealed class CrosswordLeaderboardModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress,
    IMemberAccountRepository members, TimeProvider clock) : PageModel
{
    public CrosswordCatalogItem Puzzle { get; private set; } = null!;
    public CrosswordLeaderboardDto Leaderboard { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var puzzle = (await catalog.GetAllAsync(cancellationToken)).SingleOrDefault(row => row.Seed.Slug == slug);
        if (puzzle is null || !CrosswordVisibility.IsPlayable(puzzle, clock.GetUtcNow())) return NotFound();
        Puzzle = puzzle;
        var viewer = await HttpContext.AuthenticateMemberIdAsync();
        Leaderboard = await CrosswordResultsReader.LeaderboardAsync(puzzle.Id, viewer, progress, members, cancellationToken);
        ViewData["Title"] = puzzle.Seed.Title + " leaderboard";
        return Page();
    }
}
