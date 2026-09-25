using System.Net;
using System.Text.RegularExpressions;

namespace QueenZone.Data;

/// <summary>
/// Plain-text helpers for building <see cref="Entities.SearchDocumentEntity"/> rows —
/// <c>Body</c>/<c>Summary</c> must never carry HTML into the full-text index or result cards.
/// </summary>
public static class SearchDocumentText
{
    private const int DefaultSummaryLength = 320;

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var stripped = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, RegexDefaults.MatchTimeout);
        var decoded = WebUtility.HtmlDecode(stripped);
        return string.Join(" ", decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Summarize(string? plainText, int maxLength = DefaultSummaryLength)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        var trimmed = plainText.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "…";
    }
}
