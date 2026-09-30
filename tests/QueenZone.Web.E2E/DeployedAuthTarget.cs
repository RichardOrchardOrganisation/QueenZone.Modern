namespace QueenZone.Web.E2E;

internal static class DeployedAuthTarget
{
    internal const string MemberEmail = "member@dev.queenzone.invalid";

    internal static Uri RequireDevUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "dev.queenzone.org", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException("Deployed auth E2E runs only against https://dev.queenzone.org.");
        }

        return uri;
    }
}
