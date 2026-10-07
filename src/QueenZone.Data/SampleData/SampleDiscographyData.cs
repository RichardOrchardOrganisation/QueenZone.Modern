namespace QueenZone.Data;

/// <summary>
/// Seed content for the in-memory discography repository, used when no legacy connection
/// string is configured (local dev without SQL Server, and the test environment).
/// </summary>
public static class SampleDiscographyData
{
    public static IReadOnlyList<AlbumSeed> CreateSeedAlbums() =>
    [
        new AlbumSeed(1, "Queen", 1973, "The hungry, heavy debut, recorded in stolen night-time hours at Trident and announcing a band already certain of its own ambition.",
        [
            "Keep Yourself Alive", "Doing All Right", "Great King Rat", "My Fairy King", "Liar",
            "The Night Comes Down", "Modern Times Rock 'n' Roll", "Son and Daughter", "Jesus", "Seven Seas of Rhye",
        ]),
        new AlbumSeed(2, "Queen II", 1974, "A dark fairy-tale of a record, split between a 'White' and a 'Black' side, where the band's multi-tracked grandeur first fully bloomed.",
        [
            "Procession", "Father to Son", "White Queen (As It Began)", "Some Day One Day", "The Loser in the End",
            "Ogre Battle", "The Fairy Feller's Master-Stroke", "Nevermore", "The March of the Black Queen", "Funny How Love Is", "Seven Seas of Rhye",
        ]),
        new AlbumSeed(3, "Sheer Heart Attack", 1974, "The breakthrough. 'Killer Queen' carried them onto the radio and into the charts, sharpening the sprawl into pop precision.",
        [
            "Brighton Rock", "Killer Queen", "Tenement Funster", "Flick of the Wrist", "Lily of the Valley",
            "Now I'm Here", "In the Lap of the Gods", "Stone Cold Crazy", "Dear Friends", "Misfire",
            "Bring Back That Leroy Brown", "She Makes Me (Stormtrooper in Stilettos)", "In the Lap of the Gods... Revisited",
        ]),
        new AlbumSeed(4, "A Night at the Opera", 1975, "<p>Reputedly the most expensive album ever made at the time, and home to <strong>'Bohemian Rhapsody'</strong>, the song that rewrote what a single could be.</p>",
        [
            "Death on Two Legs (Dedicated to...)", "Lazing on a Sunday Afternoon", "I'm in Love with My Car", "You're My Best Friend", "'39",
            "Sweet Lady", "Seaside Rendezvous", "The Prophet's Song", "Love of My Life", "Good Company", "Bohemian Rhapsody", "God Save the Queen",
        ]),
        new AlbumSeed(5, "A Day at the Races", 1976, "The companion piece to 'Opera', self-produced and confident, ranging from the gospel swell of 'Somebody to Love' to a tender Japanese farewell.",
        [
            "Tie Your Mother Down", "You Take My Breath Away", "Long Away", "The Millionaire Waltz", "You and I",
            "Somebody to Love", "White Man", "Good Old-Fashioned Lover Boy", "Drowse", "Teo Torriatte (Let Us Cling Together)",
        ]),
        new AlbumSeed(6, "News of the World", 1977, "Stripped back and stadium-built. Its opening one-two of 'We Will Rock You' and 'We Are the Champions' became the sound of crowds everywhere.",
        [
            "We Will Rock You", "We Are the Champions", "Sheer Heart Attack", "All Dead, All Dead", "Spread Your Wings",
            "Fight from the Inside", "Get Down, Make Love", "Sleeping on the Sidewalk", "Who Needs You", "It's Late", "My Melancholy Blues",
        ]),
    ];

    /// <summary>
    /// Made-up provider ids in the real URL shapes (not live releases). Seven Seas of Rhye has
    /// Spotify on "Queen" and Apple Music on "Queen II", so song pages resolve each provider from
    /// a different appearance.
    /// </summary>
    public static IReadOnlyList<StreamingLinkSeed> CreateSeedStreamingLinks() =>
    [
        new StreamingLinkSeed(4, null, "https://open.spotify.com/album/0SampleNightAtTheOpera"),
        new StreamingLinkSeed(4, null, "https://music.apple.com/gb/album/a-night-at-the-opera/1000000004"),
        new StreamingLinkSeed(4, 11, "https://open.spotify.com/track/0SampleBohemianRhapsod"),
        new StreamingLinkSeed(4, 11, "https://music.apple.com/gb/album/a-night-at-the-opera/1000000004?i=1000000411"),
        new StreamingLinkSeed(1, 10, "https://open.spotify.com/track/0SampleSevenSeasOfRhye"),
        new StreamingLinkSeed(2, 11, "https://music.apple.com/gb/album/queen-ii/1000000002?i=1000000211"),
    ];
}

/// <summary>
/// A sample link for an album (<paramref name="TrackNumber"/> null) or its 1-based track.
/// </summary>
public sealed record StreamingLinkSeed(int AlbumId, int? TrackNumber, string Url);

public sealed record AlbumSeed(int AlbumId, string Name, int ReleaseYear, string GeneralNotes, IReadOnlyList<string> SongTitles);
