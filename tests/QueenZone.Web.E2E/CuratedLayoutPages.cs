namespace QueenZone.Web.E2E;

/// <summary>
/// Small high-value page set for hard 390px overflow, encoding, and keyboard checks
/// in the deterministic PR gate (#1597). Keep this catalog curated — the broad
/// archive lives in the sampled live-site sitemap sweep, which stays soft.
/// </summary>
public sealed record CuratedLayoutPage(string Path, string Heading)
{
    public override string ToString() => Path;
}

internal static class CuratedLayoutPages
{
    public const int PhoneWidth = 390;
    public const int PhoneHeight = 844;

    /// <summary>Visitor-facing archive and editorial chrome.</summary>
    public static readonly CuratedLayoutPage[] Public =
    [
        new("/", "Twenty-five years of the Queen internet zone"),
        new("/news", "News"),
        new("/news/1003/queenzone-modernisation-begins", "QueenZone modernisation begins"),
        new("/forum", "Forum"),
        new("/forum/topic/1002/ranking-every-studio-album", "Ranking every studio album"),
    ];

    /// <summary>Member entry and signed-in surfaces (test-header auth in Testing).</summary>
    public static readonly CuratedLayoutPage[] Member =
    [
        new("/account/login", "Sign in"),
        new("/messages", "Messages"),
        new("/following", "Following"),
    ];

    public static IEnumerable<CuratedLayoutPage> All => Public.Concat(Member);
}
