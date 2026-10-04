using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Crosswords;

public sealed class CrosswordsIndexModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress,
    TimeProvider clock) : PageModel
{
    public IReadOnlyList<CrosswordCard> Items { get; private set; } = [];
    public string Difficulty { get; private set; } = "";
    public string Size { get; private set; } = "";
    public bool SignedIn { get; private set; }

    public async Task OnGetAsync(string? difficulty, string? size, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        ViewData["Title"] = "Queen crosswords";
        ViewData["CanonicalPath"] = "/crosswords";
        Difficulty = difficulty is "easy" or "medium" or "hard" ? difficulty : "";
        Size = size is "small" or "large" ? size : "";
        var member = await HttpContext.AuthenticateMemberIdAsync();
        SignedIn = member is not null;
        var saved = member is { } id ? await progress.GetForMemberAsync(id, cancellationToken) : [];
        var completed = member is { } memberId ? await progress.GetCompletionsAsync(null, memberId, cancellationToken) : [];
        Items = (await catalog.GetAllAsync(cancellationToken)).Where(item => CrosswordVisibility.IsListed(item, clock.GetUtcNow()))
            .Where(item => Difficulty.Length == 0 || item.Seed.Difficulty == Difficulty)
            .Where(item => Size.Length == 0 || (Size == "small" ? item.Seed.Grid.Width <= 9 : item.Seed.Grid.Width > 9))
            .OrderByDescending(item => item.PublishAt ?? item.PublishedAt).ThenBy(item => item.Seed.Title)
            .Select(item => new CrosswordCard(item.Seed.Slug, item.Seed.Title, item.Seed.Description, item.Seed.Difficulty,
                item.Seed.Grid.Width, item.Seed.Grid.Height, completed.Any(row => row.CrosswordId == item.Id) ? "Completed" :
                saved.Any(row => row.CrosswordId == item.Id && row.PlayVersion == item.PlayVersion) ? "In progress" : "Not started"))
            .ToArray();
    }
}

public sealed record CrosswordCard(string Slug, string Title, string Description, string Difficulty, int Width, int Height, string Status);
