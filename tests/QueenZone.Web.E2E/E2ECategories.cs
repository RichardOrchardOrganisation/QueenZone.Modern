namespace QueenZone.Web.E2E;

/// <summary>
/// NUnit <see cref="CategoryAttribute"/> names for the Playwright suite split.
/// <list type="bullet">
/// <item><see cref="Deterministic"/> — PR-gate fixtures against in-memory <c>Testing</c> sample data.</item>
/// <item><see cref="RealData"/> — nightly / live-site fixtures; not a required PR check (#1597).</item>
/// <item><see cref="DeployedAuth"/> — scheduled/on-demand dev member cookie check; not a required PR check (#1597).</item>
/// <item><see cref="DevJourney"/> — scheduled/on-demand deployed-DEV journey tip gate; not a required PR check (#1597).</item>
/// <item><see cref="ReadOnly"/> — orthogonal marker: fixture performs no database writes (live-site sweep).</item>
/// </list>
/// Every concrete fixture must have <see cref="Deterministic"/>, <see cref="RealData"/>,
/// <see cref="DeployedAuth"/>, or <see cref="DevJourney"/> (see <c>E2ECategoryGuardTests</c>).
/// </summary>
internal static class E2ECategories
{
    public const string Deterministic = "Deterministic";
    public const string RealData = "RealData";
    public const string DeployedAuth = "DeployedAuth";
    public const string DevJourney = "DevJourney";
    public const string ReadOnly = "ReadOnly";
}
