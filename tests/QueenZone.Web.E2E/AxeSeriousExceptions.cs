namespace QueenZone.Web.E2E;

/// <summary>
/// Triaged serious axe-core exceptions for the deterministic PR gate.
/// Fail on serious by default; add a row only after documenting why the finding
/// is known chrome/design-token debt or legacy/UGC content rather than a new
/// regression. See docs/architecture/testing-policy.md (axe serious policy).
/// </summary>
internal static class AxeSeriousExceptions
{
    /// <summary>
    /// (page-path prefix or <c>*</c>, axe rule id). <c>*</c> is reserved for a
    /// single already-triaged rule. A prefix of <c>/</c> is exact-only so it
    /// cannot waive every page.
    /// </summary>
    internal static readonly (string PathPrefix, string RuleId)[] Allowed =
    [
        // Muted meta (#8a8a85 on white, 3.46:1) and dark-band footer links
        // (#244a8f on #111111, 2.2:1) come from design tokens, not a new
        // chrome regression. Raise tokens in a design follow-up; do not reuse
        // * for other rules.
        ("*", "color-contrast"),

        // Editorial/sample article HTML inlines links without underline.
        ("/news/", "link-in-text-block"),
    ];

    public static bool IsAllowed(string? pagePath, string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return false;
        }

        var path = pagePath ?? string.Empty;
        return Allowed.Any(entry =>
            string.Equals(entry.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)
            && PathMatches(path, entry.PathPrefix));
    }

    internal static bool PathMatches(string pagePath, string prefix)
    {
        if (prefix == "*")
        {
            return true;
        }

        if (string.Equals(prefix, pagePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return prefix.Length > 1
            && pagePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
