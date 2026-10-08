using QueenZone.Data;

namespace QueenZone.Web.Pages.Songs;

/// <summary>One appearance row on the song page; <paramref name="SongTitle"/> names its links.</summary>
public sealed record SongAppearanceViewModel(SongAppearance Appearance, string SongTitle);
