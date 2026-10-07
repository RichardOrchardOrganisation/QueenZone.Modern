using System.Text.RegularExpressions;

namespace QueenZone.Web.Tests;

/// <summary>
/// The CSP <c>script-src</c> has no <c>'unsafe-inline'</c> / <c>'unsafe-hashes'</c>, so inline event
/// handlers (<c>onsubmit="return confirm(...)"</c>) silently never run. Use data attributes wired from
/// <c>site.js</c> instead, e.g. <c>_ConfirmDialog</c> for destructive confirmations.
/// </summary>
public sealed class InlineEventHandlerSourceTests
{
    private static readonly Regex InlineHandler = new(
        @"<[a-zA-Z][^<>]*?\son[a-z]{3,}\s*=\s*[""']",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void RazorViews_DoNotUseInlineEventHandlers()
    {
        var webRoot = RepoPaths.Combine("src", "QueenZone.Web");
        var offenders = Directory
            .EnumerateFiles(webRoot, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => InlineHandler.Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(webRoot, path)}: {match.Value.Trim()}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Inline event handlers are blocked by the CSP:\n" + string.Join('\n', offenders));
    }
}
