using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Crosswords;

public sealed class EditModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress) : AdminCrosswordPageModel
{
    public CrosswordCatalogItem? Puzzle { get; private set; }
    public CrosswordStatistics? Statistics { get; private set; }
    public IReadOnlyList<CrosswordAuditItem> Audit { get; private set; } = [];
    [BindProperty] public string SeedJson { get; set; } = "";
    [BindProperty] public string RowVersion { get; set; } = "";
    [BindProperty] public bool ConfirmProgressReset { get; set; }
    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        ViewData["Title"] = id is null ? "New crossword" : "Edit crossword";
        if (id is { } puzzleId)
        {
            Puzzle = await catalog.GetByIdAsync(puzzleId, cancellationToken);
            if (Puzzle is null) return NotFound();
            SeedJson = CrosswordSeedJson.Export(Puzzle.Seed);
            RowVersion = Convert.ToBase64String(Puzzle.RowVersion);
            Audit = await catalog.GetAuditAsync(puzzleId, cancellationToken);
            Statistics = CrosswordStatistics.Calculate(Puzzle, await progress.GetForPuzzleAsync(puzzleId, cancellationToken),
                await progress.GetCompletionsAsync(puzzleId, null, cancellationToken));
        }
        else SeedJson = CrosswordSeedJson.Export(new("new-crossword", "New crossword", "", "easy", "british",
            new(5, 5, Enumerable.Repeat(".....", 5).ToArray(), [])));
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is { } existing)
        {
            Puzzle = await catalog.GetByIdAsync(existing, cancellationToken);
            if (Puzzle is null) return NotFound();
            Audit = await catalog.GetAuditAsync(existing, cancellationToken);
        }
        var parsed = CrosswordSeedJson.ParseDraft(Encoding.UTF8.GetBytes(SeedJson));
        if (parsed.Seed is null) { Error = string.Join("; ", parsed.Errors.Select(issue => issue.Code + ": " + issue.Message)); return Page(); }
        try
        {
            var saved = id ?? await catalog.CreateDraftAsync(parsed.Seed, Creator, Actor, cancellationToken);
            if (id is not null) await catalog.SaveEditorialAsync(saved, parsed.Seed, Token(RowVersion), Actor, ConfirmProgressReset, cancellationToken);
            return Redirect($"/admin/crosswords/{saved}/edit");
        }
        catch (Exception ex) when (IsEditError(ex)) { HandleEditError(ex); return Page(); }
    }
    public IActionResult OnPostValidate([FromBody] JsonElement seed)
    {
        var parsed = CrosswordSeedJson.ParseDraft(Encoding.UTF8.GetBytes(seed.GetRawText()));
        var validation = parsed.Seed is null ? null : CrosswordGridValidator.Validate(parsed.Seed.Grid);
        return new JsonResult(new { parsed.Errors, parsed.Warnings, Runs = validation?.Runs.Select(run => new
            { run.Number, Direction = run.Direction == CrosswordDirection.Across ? "across" : "down", run.Row, run.Column, run.Answer }) });
    }
    public async Task<IActionResult> OnGetExportAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await catalog.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : File(Encoding.UTF8.GetBytes(CrosswordSeedJson.Export(item.Seed)), "application/json", item.Seed.Slug + ".json");
    }
}
