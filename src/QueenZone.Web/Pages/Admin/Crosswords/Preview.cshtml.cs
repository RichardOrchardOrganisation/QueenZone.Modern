using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web.Pages.Crosswords;

namespace QueenZone.Web.Pages.Admin.Crosswords;

public sealed class PreviewModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress) : AdminCrosswordPageModel
{
    public CrosswordPlayerViewModel? Player { get; private set; }
    public string[] Answers { get; private set; } = [];
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var puzzle = await catalog.GetByIdAsync(id, cancellationToken);
        if (puzzle is null) return NotFound();
        var validation = CrosswordSeedJson.Parse(System.Text.Encoding.UTF8.GetBytes(CrosswordSeedJson.Export(puzzle.Seed)));
        if (!validation.IsValid) { Error = "Fix validation errors in the builder before play-testing this draft."; return Page(); }
        Player = new(CrosswordPublicProjection.Detail(puzzle), null, false, true);
        Answers = puzzle.Seed.Grid.Rows.ToArray();
        ViewData["Title"] = "Preview crossword";
        return Page();
    }
    public async Task<IActionResult> OnGetSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        var puzzle = await catalog.GetByIdAsync(id, cancellationToken);
        if (puzzle is null) return NotFound();
        var tokens = HttpContext.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(HttpContext);
        return new JsonResult(new { MemberId = (Guid?)null, Tokens = tokens.RequestToken, puzzle.PlayVersion });
    }
    public Task<IActionResult> OnPostCheckAsync(Guid id, [FromBody] CrosswordCheckRequestDto request, CancellationToken cancellationToken) =>
        PlayAsync(id, puzzle => CrosswordPlayActions.CheckAsync(puzzle, request, null, progress, cancellationToken), cancellationToken);
    public Task<IActionResult> OnPostRevealAsync(Guid id, [FromBody] CrosswordRevealRequestDto request, CancellationToken cancellationToken) =>
        PlayAsync(id, puzzle => CrosswordPlayActions.RevealAsync(puzzle, request, null, progress, cancellationToken), cancellationToken);
    private async Task<IActionResult> PlayAsync<T>(Guid id, Func<CrosswordCatalogItem, Task<T>> action, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var puzzle = await catalog.GetByIdAsync(id, cancellationToken);
        if (puzzle is null) return NotFound();
        try { return new JsonResult(await action(puzzle)); }
        catch (ArgumentException) { return BadRequest(); }
        catch (OptimisticConcurrencyException) { return StatusCode(409); }
    }
}
