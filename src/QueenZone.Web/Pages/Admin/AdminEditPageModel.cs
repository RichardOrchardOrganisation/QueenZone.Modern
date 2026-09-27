using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Admin;

public abstract class AdminEditPageModel : PageModel
{
    protected async Task<IActionResult> LoadEditAsync<TKey, TItem>(
        TKey id,
        Func<TKey, CancellationToken, Task<TItem?>> load,
        Action<TItem> configure,
        CancellationToken cancellationToken)
        where TItem : class
    {
        var item = await load(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        configure(item);
        return Page();
    }
}
