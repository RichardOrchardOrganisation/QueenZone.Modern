using System.Text.RegularExpressions;

namespace QueenZone.Web.E2E;

/// <summary>
/// Discovery helpers for the deployed-DEV journey suite. Queries come from live
/// page text so the fixture never hardcodes Testing seed titles or ids.
/// </summary>
internal static class DevJourneyDiscovery
{
    private static readonly Regex SearchWordPattern = new(@"[A-Za-z]{3,}", RegexOptions.CultureInvariant);

    internal static string FirstSearchWord(string? source)
    {
        Assert.That(source, Is.Not.Null.And.Not.Empty, "Expected live DEV content to discover a search word from.");
        var match = SearchWordPattern.Match(source!);
        Assert.That(
            match.Success,
            Is.True,
            "Expected a searchable word of at least three letters from live DEV content.");
        return match.Value;
    }
}
