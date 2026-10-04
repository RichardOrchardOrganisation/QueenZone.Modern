namespace QueenZone.Web;

/// <summary>Session-only local-progress partition hint. Never used to authorize a request.</summary>
internal static class CrosswordAccountHint
{
    public const string CookieName = "qz-crossword-account";
    public static void Write(HttpContext context, Guid? member)
    {
        var options = new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, HttpOnly = false, IsEssential = true };
        if (member is { } id) context.Response.Cookies.Append(CookieName, id.ToString("D"), options);
        else context.Response.Cookies.Delete(CookieName, options);
    }
}
