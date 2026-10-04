using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Crosswords;

public abstract class AdminCrosswordPageModel : AdminEditPageModel
{
    public string? Error { get; protected set; }
    protected string Actor => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "admin";
    protected Guid Creator => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        Response.Headers.CacheControl = "no-store";
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }
    protected static byte[] Token(string token)
    {
        try { return Convert.FromBase64String(token); }
        catch (FormatException) { throw new ArgumentException("Invalid edit token. Reload before saving."); }
    }
    protected bool HandleEditError(Exception error)
    {
        Error = error is OptimisticConcurrencyException
            ? "Another admin changed this crossword. Reload before saving; your changes have not overwritten theirs."
            : error.Message;
        return true;
    }
    protected static bool IsEditError(Exception error) => error is ArgumentException or InvalidOperationException or OptimisticConcurrencyException;
}
