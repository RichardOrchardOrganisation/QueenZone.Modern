namespace QueenZone.Web.E2E;

/// <summary>
/// Triaged serious axe-core exceptions for the deterministic PR gate.
/// Fail on serious by default; add a row only after documenting why the finding
/// is known legacy/UGC content rather than chrome we own. See
/// docs/architecture/testing-policy.md (axe serious policy).
/// </summary>
internal static class AxeSeriousExceptions
{
    /// <summary>
    /// (page-path prefix, axe rule id). Prefix match is ordinal-ignore-case and
    /// must stay narrow (a section path, not <c>*</c>).
    /// </summary>
    private static readonly (string PathPrefix, string RuleId)[] Allowed =
    [
        // Empty on purpose. Add only a triaged legacy/UGC pair, for example:
        // ("/forum/topic/", "color-contrast"), // legacy post HTML, not site chrome
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
            && path.StartsWith(entry.PathPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
