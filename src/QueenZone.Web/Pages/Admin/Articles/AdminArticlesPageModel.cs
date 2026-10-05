using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Articles;

public abstract class AdminArticlesPageModel : PageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected string EditorEmail =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("preferred_username")
        ?? User.Identity?.Name
        ?? "unknown";

    protected static async Task<IReadOnlyList<ArticleItem>> LoadAllLegacyArchiveAsync(
        IArticlesRepository legacyArticles,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var items = new List<ArticleItem>();
        var page = 1;
        while (true)
        {
            var batch = await legacyArticles.GetArchivePageAsync(page++, pageSize, cancellationToken);
            items.AddRange(batch);
            if (batch.Count < pageSize)
            {
                break;
            }
        }

        return items;
    }
}
