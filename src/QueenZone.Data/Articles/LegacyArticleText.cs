using System.Net;
using System.Text.RegularExpressions;

namespace QueenZone.Data;

public static partial class LegacyArticleText
{
    public static string GetExcerpt(string? body, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var plain = HtmlTags.Pattern().Replace(body, " ");
        plain = WebUtility.HtmlDecode(plain);
        plain = WhitespaceRegex().Replace(plain, " ").Trim();
        if (plain.Length <= maxLength)
        {
            return plain;
        }

        return plain[..maxLength].TrimEnd() + "…";
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
