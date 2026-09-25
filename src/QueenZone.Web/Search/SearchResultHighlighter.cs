using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using QueenZone.Data;

namespace QueenZone.Web.Search;

/// <summary>
/// HTML-encodes a plain-text search result summary and wraps query-term matches in
/// <c>&lt;mark&gt;</c>. <paramref name="summary"/> comes from <c>SearchDocument.Summary</c>,
/// which is always plain text (never untrusted HTML) — see <c>SearchDocumentText</c>.
/// </summary>
public static class SearchResultHighlighter
{
    public static IHtmlContent Highlight(string summary, string query)
    {
        var encodedSummary = WebUtility.HtmlEncode(summary);
        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 1)
            .Select(Regex.Escape)
            .Distinct()
            .ToList();

        if (terms.Count == 0)
        {
            return new HtmlString(encodedSummary);
        }

        var pattern = string.Join('|', terms);
        return new HtmlString(ReplaceHighlighted(encodedSummary, pattern, RegexDefaults.MatchTimeout));
    }

    /// <summary>
    /// Query terms can come from user input. A timeout returns the encoded summary
    /// without highlights rather than failing the search page.
    /// </summary>
    internal static string ReplaceHighlighted(string encodedSummary, string pattern, TimeSpan matchTimeout)
    {
        try
        {
            return Regex.Replace(
                encodedSummary,
                pattern,
                static match => $"<mark>{match.Value}</mark>",
                RegexOptions.IgnoreCase,
                matchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return encodedSummary;
        }
    }
}
