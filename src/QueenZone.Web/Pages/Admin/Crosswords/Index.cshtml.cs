using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Pages.Admin.Crosswords;

public sealed class IndexModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress) : AdminCrosswordPageModel
{
    public IReadOnlyList<CrosswordCatalogItem> Puzzles { get; private set; } = [];
    public Dictionary<Guid, (int Starts, int Completions)> Counts { get; } = [];
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Crosswords";
        var rows = await catalog.GetAllAsync(cancellationToken);
        var filtered = rows.Where(row => (string.IsNullOrWhiteSpace(Status) || row.Status.ToString().Equals(Status, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(Search) || row.Seed.Title.Contains(Search, StringComparison.OrdinalIgnoreCase)));
        Puzzles = Sort == "oldest" ? filtered.OrderBy(row => row.PublishAt).ToArray() : filtered.OrderByDescending(row => row.PublishAt).ToArray();
        foreach (var row in Puzzles)
            Counts[row.Id] = ((await progress.GetForPuzzleAsync(row.Id, cancellationToken)).Count,
                (await progress.GetCompletionsAsync(row.Id, null, cancellationToken)).Count);
    }
    public async Task<IActionResult> OnPostPublicationAsync(Guid id, string rowVersion, CrosswordStatus status, DateTimeOffset? publishAt, CancellationToken cancellationToken)
    {
        try { await catalog.SetPublicationAsync(id, status, publishAt, Token(rowVersion), Actor, cancellationToken); }
        catch (Exception ex) when (IsEditError(ex)) { HandleEditError(ex); await LoadAsync(cancellationToken); return Page(); }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostPublishSelectedAsync(Guid[]? ids, string[]? rowVersions, CancellationToken cancellationToken)
    {
        if (ids is null || rowVersions is null || ids.Length != rowVersions.Length || ids.Length > 100) { Error = "Invalid selection. Reload the list."; await LoadAsync(cancellationToken); return Page(); }
        try
        {
            var selection = ids.Select((id, index) => new CrosswordPublishSelection(id, Token(rowVersions[index]))).ToArray();
            await catalog.PublishSelectedAsync(selection, Actor, cancellationToken);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (IsEditError(ex)) { HandleEditError(ex); await LoadAsync(cancellationToken); return Page(); }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostDuplicateAsync(Guid id, string newSlug, CancellationToken cancellationToken)
    {
        try { var copy = await catalog.DuplicateAsync(id, newSlug, Creator, Actor, cancellationToken); return Redirect($"/admin/crosswords/{copy}/edit"); }
        catch (Exception ex) when (IsEditError(ex)) { HandleEditError(ex); await LoadAsync(cancellationToken); return Page(); }
    }
}
