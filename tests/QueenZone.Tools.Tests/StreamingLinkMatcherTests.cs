using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class StreamingLinkMatcherTests
{
    [Theory]
    [InlineData("A Night At The Opera (2011 Remaster)", "a night at the opera")]
    [InlineData("Bohemian Rhapsody - Remastered 2011", "bohemian rhapsody")]
    [InlineData("Death on Two Legs (Dedicated to...)", "death on two legs")]
    [InlineData("'39", "39")]
    [InlineData("Brighton Rock [Live]", "brighton rock")]
    [InlineData("Flash & The Ming", "flash and the ming")]
    [InlineData("Mustapha – Live At Wembley", "mustapha")]
    [InlineData("Teo Torriatte (Let Us Cling Together)", "teo torriatte")]
    [InlineData("Señorita", "senorita")]
    public void Normalize_strips_version_noise(string title, string expected) =>
        Assert.Equal(expected, StreamingLinkMatcher.Normalize(title));

    [Theory]
    [InlineData("Doin Alright", "Doing All Right")]
    [InlineData("Looser in the End", "The Loser In The End")]
    [InlineData("The Fairy Fellers Master Stroke", "The Fairy Feller's Master-Stroke")]
    [InlineData("March Of The Black Queen", "The March of the Black Queen")]
    [InlineData("Prophets Song", "The Prophet's Song")]
    [InlineData("Teo Toriatte (let us cling together)", "Teo Torriatte (Let Us Cling Together)")]
    [InlineData("Mustahpa", "Mustapha")]
    [InlineData("Dreamers Ball", "Dreamer's Ball")]
    [InlineData("Leaving home aint easy", "Leaving Home Ain't Easy")]
    [InlineData("It's a kind of magic", "A Kind Of Magic")]
    [InlineData("Just one year of love", "One Year Of Love")]
    [InlineData("Modern Times Rock'n'Roll", "Modern Times Rock 'n’ Roll")]
    public void Best_track_matches_known_legacy_spellings(string local, string remote)
    {
        var match = StreamingLinkMatcher.BestTrack(local, 1, "Queen", [new("correct", remote, 1, 1, "https://x")]);

        Assert.Equal("correct", match!.Candidate.ExternalId);
    }

    [Theory]
    [InlineData("Queen I", "Queen")]
    [InlineData("Greatest Hits 1", "Greatest Hits")]
    [InlineData("Greatest Hits 2", "Greatest Hits II")]
    [InlineData("Greatest Hits 3", "Greatest Hits III")]
    [InlineData("Live At Wembley '86", "Live at Wembley Stadium")]
    [InlineData("Rock Montreal", "Queen Rock Montreal")]
    public void Legacy_album_names_search_and_score_the_catalogue_title(string local, string remote)
    {
        Assert.Equal(remote, StreamingLinkMatcher.AlbumSearchTitle(local));
        Assert.Equal(100, StreamingLinkMatcher.ScoreAlbum(local, 1981, 10, new("correct", remote, 1981, 10, "https://x")).Score);
    }

    [Fact]
    public void Queen_debut_prefers_original_release_over_the_2024_mix()
    {
        var mix = new CatalogAlbum("mix", "Queen I (2024 Mix)", 2024, 11, "https://x/mix");
        var original = new CatalogAlbum("original", "Queen", 1973, 10, "https://x/original");

        Assert.Equal("original", StreamingLinkMatcher.BestAlbum("Queen I", 1973, 10, [mix, original])!.Candidate.ExternalId);
        Assert.Contains(StreamingLinkMatcher.MixFlag, StreamingLinkMatcher.ScoreAlbum("Queen I", 1973, 10, mix).Flags);
    }

    [Fact]
    public void Live_in_a_song_title_is_not_a_live_recording_marker()
    {
        var studio = StreamingLinkMatcher.BestTrack("Who Wants to Live Forever?", 6, "A Kind of Magic",
            [new("studio", "Who Wants To Live Forever", 6, 1, "https://x")]);

        Assert.Empty(studio!.Flags);
        Assert.Equal(100, studio.Score);
        Assert.Null(StreamingLinkMatcher.BestTrack("In the Lap of the Gods", 7, "Sheer Heart Attack",
            [new("revisited", "In the Lap of the Gods... Revisited", 13, 1, "https://x")]));
        Assert.Null(StreamingLinkMatcher.BestTrack("One Vision", 1, "Live Magic",
            [new("wrong", "One Year Of Love", 1, 1, "https://x")]));
    }

    [Fact]
    public void Best_album_prefers_exact_title_year_and_track_count_over_deluxe_and_live()
    {
        CatalogAlbum[] candidates =
        [
            new("deluxe", "A Night At The Opera (Deluxe Edition 2011 Remaster)", 1975, 17, "https://x/deluxe"),
            new("live", "A Night At The Opera - Live", 1979, 12, "https://x/live"),
            new("studio", "A Night At The Opera (2011 Remaster)", 1975, 12, "https://x/studio"),
            new("other", "News Of The World", 1977, 11, "https://x/other"),
        ];

        var best = StreamingLinkMatcher.BestAlbum("A Night at the Opera", 1975, 12, candidates);

        Assert.NotNull(best);
        Assert.Equal("studio", best.Candidate.ExternalId);
        Assert.Equal(100, best.Score);
        Assert.Equal([StreamingLinkMatcher.RemasterFlag], best.Flags);
        Assert.Contains(StreamingLinkMatcher.DeluxeFlag, StreamingLinkMatcher.ScoreAlbum("A Night at the Opera", 1975, 12, candidates[0]).Flags);
        Assert.Contains(StreamingLinkMatcher.LiveFlag, StreamingLinkMatcher.ScoreAlbum("A Night at the Opera", 1975, 12, candidates[1]).Flags);
    }

    [Fact]
    public void Best_album_needs_a_title_match_and_keeps_live_albums_that_are_meant_to_be_live()
    {
        Assert.Null(StreamingLinkMatcher.BestAlbum("Jazz", 1978, 13, [new("x", "Innuendo", 1991, 12, "https://x")]));

        var liveKillers = StreamingLinkMatcher.ScoreAlbum("Live Killers", 1979, 22, new("lk", "Live Killers", 1979, 22, "https://x"));
        Assert.DoesNotContain(StreamingLinkMatcher.LiveFlag, liveKillers.Flags);
        Assert.Equal(100, liveKillers.Score);

        var compilation = StreamingLinkMatcher.ScoreAlbum("Queen", 1973, 10, new("gh", "Queen Greatest Hits", 1981, 17, "https://x", IsCompilation: true));
        Assert.Contains(StreamingLinkMatcher.CompilationFlag, compilation.Flags);
        Assert.True(compilation.Score < 50);
    }

    [Fact]
    public void Best_track_requires_a_title_match_and_uses_position_to_break_ties()
    {
        CatalogTrack[] tracks =
        [
            new("1", "In the Lap of the Gods - Remastered 2011", 7, 1, "https://x/1"),
            new("2", "In the Lap of the Gods... Revisited - Remastered 2011", 13, 1, "https://x/2"),
            new("3", "Brighton Rock - Remastered 2011", 1, 1, "https://x/3"),
            new("4", "Brighton Rock - Live At Hammersmith", 1, 2, "https://x/4"),
        ];

        var brighton = StreamingLinkMatcher.BestTrack("Brighton Rock", 1, "Sheer Heart Attack", tracks);
        Assert.Equal("3", brighton!.Candidate.ExternalId);
        Assert.Equal(100, brighton.Score);
        Assert.Equal([StreamingLinkMatcher.RemasterFlag], brighton.Flags);

        Assert.Equal("1", StreamingLinkMatcher.BestTrack("In the Lap of the Gods", 7, "Sheer Heart Attack", tracks)!.Candidate.ExternalId);
        Assert.Null(StreamingLinkMatcher.BestTrack("Misfire", 10, "Sheer Heart Attack", tracks));
        Assert.Null(StreamingLinkMatcher.BestTrack("()", 1, "x", tracks));
    }
}
