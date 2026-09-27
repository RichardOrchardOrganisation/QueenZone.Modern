using System.Text.RegularExpressions;

namespace QueenZone.Data;

/// <summary>
/// The one shared "strip HTML tags" pattern for plain-text excerpts and heuristics. It only
/// matches markup shape, so it is culture- and case-independent.
/// </summary>
public static partial class HtmlTags
{
    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
}
