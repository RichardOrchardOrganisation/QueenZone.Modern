using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace QueenZone.Web.E2E;

/// <summary>
/// Fails the PR gate if a Playwright evaluation or wait call passes a bare JavaScript
/// expression (for example <c>"navigator.serviceWorker.controller !== null"</c>) instead
/// of a function (<c>"() => navigator.serviceWorker.controller !== null"</c>).
/// <para>
/// Playwright turns the string into code with <c>globalThis.eval</c>. For
/// <c>WaitForFunctionAsync</c> a bare expression is re-evaluated on every poll, and
/// polls after the first run from page timers, where the site's nonce-based CSP (no
/// <c>'unsafe-eval'</c>) blocks them with <c>EvalError</c>. The test then fails only
/// when the condition was false on the first check, so the flake is intermittent. A
/// function is evaluated once and cached, so it is safe. Evaluate calls follow the same
/// rule so the convention is uniform.
/// </para>
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class PlaywrightEvaluationGuardTests
{
    // A call whose script is the first argument, followed by a regular, verbatim or raw
    // string literal. Calls that pass a constant (or a selector first) are not matched.
    private static readonly Regex ScriptCall = new(
        """\b(?<method>WaitForFunctionAsync|EvaluateAsync|EvaluateHandleAsync|EvaluateAllAsync)(?:<[^>()]*>)?\(\s*(?:\$|@)?(?<quotes>"(?:"")?)""",
        RegexOptions.Compiled);

    private static readonly Regex FunctionStart = new(
        @"^\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*=>|[A-Za-z_$][\w$]*\s*=>)",
        RegexOptions.Compiled);

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    [TestCase("navigator.serviceWorker.controller !== null", false)]
    [TestCase("window.commands.includes('pauseVideo')", false)]
    [TestCase("scrollY", false)]
    [TestCase("() => navigator.serviceWorker.controller !== null", true)]
    [TestCase("async () => { await 1; }", true)]
    [TestCase("element => element.value", true)]
    [TestCase("async module => (await fetch(module)).text()", true)]
    [TestCase("(a, b) => a + b", true)]
    [TestCase("\n    async () => {\n", true)]
    public void FunctionStartRecognisesFunctionsOnly(string script, bool isFunction) =>
        Assert.That(FunctionStart.IsMatch(script), Is.EqualTo(isFunction));

    [Test]
    public void NoTestPassesABareExpressionToPlaywright()
    {
        var thisFilePath = ThisFilePath();
        var directory = Path.GetDirectoryName(thisFilePath);
        Assert.That(directory, Is.Not.Null.And.Not.Empty, "Could not resolve the E2E test source directory.");

        var thisFileName = Path.GetFileName(thisFilePath);
        var sourceFiles = Directory.GetFiles(directory!, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), thisFileName, StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.That(sourceFiles, Is.Not.Empty, "Expected to find other .cs files alongside this guard test.");

        var violations = new List<string>();
        var callsChecked = 0;
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match call in ScriptCall.Matches(text))
            {
                callsChecked++;
                var scriptStart = call.Index + call.Length;
                if (!FunctionStart.IsMatch(text.AsSpan(scriptStart, Math.Min(200, text.Length - scriptStart)).ToString()))
                {
                    var line = text.AsSpan(0, call.Index).Count('\n') + 1;
                    violations.Add($"{Path.GetFileName(file)}:{line}: {call.Groups["method"].Value} with a bare expression");
                }
            }
        }

        Assert.That(callsChecked, Is.GreaterThan(0), "Expected the guard to find Playwright evaluation calls to check.");
        Assert.That(
            violations,
            Is.Empty,
            "Pass a function such as \"() => ...\", not a bare expression: Playwright evals bare " +
            "expressions on every poll and the site's CSP blocks eval. " + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }
}
