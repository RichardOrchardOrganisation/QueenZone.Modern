using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Shared axe-core runner. The deterministic PR gate fails on critical and on
/// serious findings that are not in <see cref="AxeSeriousExceptions"/>. The
/// sampled live-site sitemap sweep still fails on critical only so archive UGC
/// cannot turn the whole sitemap into a brittle gate (#1597).
/// </summary>
internal static class AxeAssertions
{
    public static Task AssertNoCriticalViolationsAsync(IPage page) =>
        AssertViolationsAsync(page, pagePath: null, failOnSerious: false);

    public static Task AssertNoBlockingViolationsAsync(IPage page, string pagePath) =>
        AssertViolationsAsync(page, pagePath, failOnSerious: true);

    private static async Task AssertViolationsAsync(IPage page, string? pagePath, bool failOnSerious)
    {
        var options = new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]
            }
        };

        AxeResult results = await page.RunAxe(options);
        var critical = results.Violations
            .Where(v => string.Equals(v.Impact, "critical", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var serious = results.Violations
            .Where(v => string.Equals(v.Impact, "serious", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var allowedSerious = serious
            .Where(v => AxeSeriousExceptions.IsAllowed(pagePath, v.Id))
            .ToList();
        var blockingSerious = serious
            .Where(v => !AxeSeriousExceptions.IsAllowed(pagePath, v.Id))
            .ToList();

        if (!failOnSerious && serious.Count > 0)
        {
            TestContext.Out.WriteLine(
                "Serious (non-blocking on sitemap/live-site sweep) axe findings:"
                + Environment.NewLine
                + FormatViolations(serious));
        }

        if (failOnSerious && allowedSerious.Count > 0)
        {
            TestContext.Out.WriteLine(
                "Serious axe findings allowed by AxeSeriousExceptions:"
                + Environment.NewLine
                + FormatViolations(allowedSerious));
        }

        Assert.That(
            critical.Select(v => v.Id).ToList(),
            Is.Empty,
            "Critical axe-core violations:" + Environment.NewLine + FormatViolations(critical));

        if (failOnSerious)
        {
            Assert.That(
                blockingSerious.Select(v => v.Id).ToList(),
                Is.Empty,
                "Serious axe-core violations (add a triaged AxeSeriousExceptions row only after review):"
                + Environment.NewLine
                + FormatViolations(blockingSerious));
        }
    }

    private static string FormatViolations(IEnumerable<AxeResultItem> items) =>
        string.Join(
            Environment.NewLine,
            items.Select(v =>
            {
                var nodeCount = v.Nodes?.Count() ?? 0;
                return $"- [{v.Impact}] {v.Id}: {v.Help} ({v.HelpUrl}) nodes={nodeCount}";
            }));
}
