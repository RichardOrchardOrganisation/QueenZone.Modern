using System.Net;
using System.Text.RegularExpressions;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Shared strip for legacy forum titles, names, and descriptions that occasionally
/// contain raw HTML (e.g. <c>&lt;b&gt;</c>) meant for old vBulletin-style templates.
/// List fields are rendered as plain text on the website and in <c>/api/v1/forum</c>,
/// so strip tags rather than showing the literal markup.
/// </summary>
internal static partial class ForumTextCleaning
{
    internal static string CleanForumText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var plain = HtmlTags.Pattern().Replace(value, string.Empty);
        plain = WebUtility.HtmlDecode(plain);
        return ForumWhitespaceRegex().Replace(plain, " ").Trim();
    }

    internal static string? CleanForumTextOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : CleanForumText(value);

    [GeneratedRegex("\\s+")]
    private static partial Regex ForumWhitespaceRegex();
}
