using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class CrosswordTeaserViewComponent(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress,
    TimeProvider clock) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var puzzle = (await catalog.GetAllAsync(cancellationToken)).Where(item => CrosswordVisibility.IsListed(item, clock.GetUtcNow()))
            .OrderByDescending(item => item.PublishAt ?? item.PublishedAt).ThenBy(item => item.Seed.Slug).FirstOrDefault();
        if (puzzle is null) return Content("");
        var member = await HttpContext.AuthenticateMemberIdAsync();
        var saved = member is { } id ? await progress.GetAsync(puzzle.Id, id, cancellationToken) : null;
        var completed = member is { } completionMember
            && (await progress.GetCompletionsAsync(puzzle.Id, completionMember, cancellationToken)).Count > 0;
        return View(new CrosswordTeaserViewModel(puzzle.Seed.Slug, puzzle.Seed.Title, puzzle.Seed.Grid.Width, puzzle.Seed.Grid.Height,
            !completed && saved is not null && saved.PlayVersion == puzzle.PlayVersion));
    }
}

public sealed record CrosswordTeaserViewModel(string Slug, string Title, int Width, int Height, bool Continue);
