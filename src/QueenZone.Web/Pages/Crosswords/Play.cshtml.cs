using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Crosswords;

[EnableRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)]
public sealed class CrosswordPlayModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress,
    TimeProvider clock) : PageModel
{
    public CrosswordDetailDto Puzzle { get; private set; } = null!;
    public Guid? MemberId { get; private set; }
    public bool OfflineShell { get; private set; }

    public Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken) => LoadAsync(slug, false, cancellationToken);
    public Task<IActionResult> OnGetOfflineShellAsync(string slug, CancellationToken cancellationToken) => LoadAsync(slug, true, cancellationToken);

    private async Task<IActionResult> LoadAsync(string slug, bool offlineShell, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var item = await FindPlayableAsync(slug, cancellationToken);
        if (item is null) return NotFound();
        Puzzle = CrosswordPublicProjection.Detail(item);
        OfflineShell = offlineShell;
        if (offlineShell) Response.Headers["X-QueenZone-Crossword-Shell"] = "public";
        else
        {
            MemberId = await HttpContext.AuthenticateMemberIdAsync();
        }
        ViewData["Title"] = Puzzle.Title + " | Queen crosswords";
        ViewData["CanonicalPath"] = "/crosswords/" + Puzzle.Slug;
        return Page();
    }

    public Task<IActionResult> OnGetSessionAsync(string slug, CancellationToken cancellationToken) =>
        WithPlayableAsync(slug, false, (puzzle, member) =>
        {
            var tokens = HttpContext.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(HttpContext);
            return Task.FromResult<object?>(new { MemberId = member, Tokens = tokens.RequestToken, puzzle.PlayVersion });
        }, cancellationToken, checkAccount: false);

    public Task<IActionResult> OnPostCheckAsync(string slug, [FromBody] CrosswordCheckRequestDto request, CancellationToken cancellationToken) =>
        WithPlayableAsync(slug, false, async (puzzle, member) => await CrosswordPlayActions.CheckAsync(puzzle, request, member, progress, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostRevealAsync(string slug, [FromBody] CrosswordRevealRequestDto request, CancellationToken cancellationToken) =>
        WithPlayableAsync(slug, false, async (puzzle, member) => await CrosswordPlayActions.RevealAsync(puzzle, request, member, progress, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostSaveAsync(string slug, [FromBody] CrosswordProgressRequestDto request, CancellationToken cancellationToken) =>
        WithPlayableAsync(slug, true, async (puzzle, member) => CrosswordPlayActions.Progress(
            await progress.SaveAsync(puzzle.Id, member!.Value, request.ToWrite(), cancellationToken)), cancellationToken);

    public Task<IActionResult> OnPostCompleteAsync(string slug, [FromBody] CrosswordProgressRequestDto request, CancellationToken cancellationToken) =>
        WithPlayableAsync(slug, true, async (puzzle, member) => await CrosswordPlayActions.CompleteAsync(puzzle, request, member!.Value, progress, cancellationToken), cancellationToken);

    public Task<IActionResult> OnGetProgressAsync(string slug, CancellationToken cancellationToken) =>
        WithPlayableAsync(slug, true, async (puzzle, member) =>
        {
            var saved = await progress.GetAsync(puzzle.Id, member!.Value, cancellationToken);
            return saved is null ? null : CrosswordPlayActions.Progress(saved);
        }, cancellationToken);

    private async Task<CrosswordCatalogItem?> FindPlayableAsync(string slug, CancellationToken cancellationToken) =>
        (await catalog.GetAllAsync(cancellationToken)).SingleOrDefault(item =>
            item.Seed.Slug == slug && CrosswordVisibility.IsPlayable(item, clock.GetUtcNow()));

    private async Task<IActionResult> WithPlayableAsync(string slug, bool memberRequired,
        Func<CrosswordCatalogItem, Guid?, Task<object?>> action, CancellationToken cancellationToken, bool checkAccount = true)
    {
        Response.Headers.CacheControl = "no-store";
        var (member, error) = await ResolveMemberAsync(memberRequired, checkAccount);
        if (error is not null) return error;
        if (!ModelState.IsValid) return Problem("Invalid crossword input", 400);
        try
        {
            var puzzle = await FindPlayableAsync(slug, cancellationToken);
            if (puzzle is null) return NotFound();
            var result = await action(puzzle, member);
            var changed = await ValidateLatestAsync(puzzle, cancellationToken);
            if (changed is not null) return changed;
            return result is null ? new NoContentResult() : new JsonResult(result);
        }
        catch (ArgumentException) { return Problem("Invalid crossword input", 400); }
        catch (OptimisticConcurrencyException) { return Problem("Crossword changed. Reload before continuing.", 409); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private async Task<(Guid? Member, IActionResult? Error)> ResolveMemberAsync(bool required, bool checkAccount)
    {
        var member = await HttpContext.AuthenticateMemberIdAsync();
        if (required && member is null) return (null, Unauthorized());
        if (checkAccount && member is { } id && !MatchesExpectedMember(id))
        {
            if (required) return (null, Problem("Account changed. Reload before continuing.", 409));
            member = null;
        }
        return (member, null);
    }

    private bool MatchesExpectedMember(Guid member) =>
        Guid.TryParse(Request.Headers["X-Crossword-Member"], out var expected) && expected == member;

    private async Task<IActionResult?> ValidateLatestAsync(CrosswordCatalogItem puzzle, CancellationToken cancellationToken)
    {
        var latest = await catalog.GetByIdAsync(puzzle.Id, cancellationToken);
        if (latest is null || !CrosswordVisibility.IsPlayable(latest, clock.GetUtcNow())) return NotFound();
        return latest.PlayVersion != puzzle.PlayVersion ? Problem("Crossword changed. Reload before continuing.", 409) : null;
    }

    private static ObjectResult Problem(string title, int status) => new(new ProblemDetails { Title = title, Status = status }) { StatusCode = status };
}
