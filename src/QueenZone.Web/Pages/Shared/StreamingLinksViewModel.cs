using QueenZone.Data;

namespace QueenZone.Web.Pages;

/// <summary>
/// Public "Listen on" links for an album, song or track. <paramref name="Subject"/> is appended
/// to each accessible name so repeated links on one page stay distinguishable.
/// <paramref name="Compact"/> renders small inline provider links for tracklist rows.
/// </summary>
public sealed record StreamingLinksViewModel(
    IReadOnlyList<StreamingLink> Links,
    string Subject,
    StreamingLinkKind Kind,
    bool Compact = false)
{
    public string TargetType => Kind == StreamingLinkKind.Album ? "album" : "track";
}
