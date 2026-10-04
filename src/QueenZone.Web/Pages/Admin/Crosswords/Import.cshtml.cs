using System.Text;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Crosswords;

[RequestSizeLimit(300 * 1024)]
public sealed class ImportModel(ICrosswordCatalogRepository catalog) : AdminCrosswordPageModel
{
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string SeedJson { get; set; } = "";
    [BindProperty] public string NewSlug { get; set; } = "";
    public CrosswordSeedParseResult? Preview { get; private set; }
    public void OnGet() => ViewData["Title"] = "Import crossword";
    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        if (Upload is null || Upload.Length > CrosswordSeedJson.MaxBytes || Upload.ContentType != "application/json")
        { Error = "Upload one application/json file, no larger than 256 KB."; return Page(); }
        using var buffer = new MemoryStream();
        await Upload.CopyToAsync(buffer, cancellationToken);
        Preview = CrosswordSeedJson.Parse(buffer.ToArray());
        SeedJson = Encoding.UTF8.GetString(buffer.ToArray());
        NewSlug = Preview.Seed?.Slug ?? "";
        return Page();
    }
    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        Preview = CrosswordSeedJson.Parse(Encoding.UTF8.GetBytes(SeedJson));
        if (!Preview.IsValid) { Error = "Fix the validation errors before importing."; return Page(); }
        try
        {
            var seed = Preview.Seed! with { Slug = NewSlug };
            var result = await catalog.ImportAsync([seed], Creator, Actor, publish: false, cancellationToken);
            if (result.Skipped.Count > 0) { Error = "That slug already exists. Choose a new slug; nothing was overwritten."; return Page(); }
            var item = (await catalog.GetAllAsync(cancellationToken)).Single(row => row.Seed.Slug == seed.Slug);
            return Redirect($"/admin/crosswords/{item.Id}/edit");
        }
        catch (Exception ex) when (IsEditError(ex)) { HandleEditError(ex); return Page(); }
    }
}
