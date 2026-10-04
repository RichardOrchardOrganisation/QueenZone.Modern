using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Account;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class MyCrosswordsModel(ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, TimeProvider clock) : PageModel
{
    public CrosswordHistoryDto History { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var member = await HttpContext.AuthenticateMemberIdAsync();
        if (member is null) return Redirect("/account/login");
        History = await CrosswordResultsReader.HistoryAsync(member.Value, catalog, progress, clock.GetUtcNow(), cancellationToken);
        ViewData["Title"] = "My crosswords";
        return Page();
    }
}
