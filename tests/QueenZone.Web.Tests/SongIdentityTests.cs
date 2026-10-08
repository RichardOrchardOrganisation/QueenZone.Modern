using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Data test for the Ship A song identity lock: one song per
/// <see cref="NewsSlug.Slugify"/> of an active-album title. Parentheticals stay distinct.
/// </summary>
public sealed class SongIdentityTests
{
    [Fact]
    public async Task Same_title_on_two_active_albums_is_one_song_and_parenthetical_is_not()
    {
        var repository = new InMemoryDiscographyRepository(
        [
            new AlbumSeed(1, "Queen", 1973, "", ["Death on Two Legs", "Seven Seas of Rhye"]),
            new AlbumSeed(2, "Queen II", 1974, "", ["Seven Seas of Rhye"]),
            new AlbumSeed(3, "A Night at the Opera", 1975, "", ["Death on Two Legs (Dedicated to...)"]),
        ]);

        var songs = await repository.GetSongsAsync();

        Assert.Equal(3, songs.Count);
        var sevenSeas = Assert.Single(songs, song => song.Slug == "seven-seas-of-rhye");
        Assert.Equal("Seven Seas of Rhye", sevenSeas.Title);
        Assert.Equal(2, sevenSeas.AppearanceCount);
        Assert.Equal(["Queen", "Queen II"], sevenSeas.AlbumNames);

        Assert.Contains(songs, song => song.Slug == "death-on-two-legs" && song.Title == "Death on Two Legs");
        Assert.Contains(
            songs,
            song => song.Slug == "death-on-two-legs-dedicated-to"
                && song.Title == "Death on Two Legs (Dedicated to...)");

        var canonical = await repository.GetSongBySlugAsync("seven-seas-of-rhye");
        Assert.NotNull(canonical);
        Assert.Equal("Seven Seas of Rhye", canonical.Title);
        Assert.Equal(2, canonical.Appearances.Count);
        Assert.Equal("Queen", canonical.Appearances[0].AlbumName);
        Assert.Equal(1973, canonical.Appearances[0].ReleaseYear);
        Assert.Equal("Queen II", canonical.Appearances[1].AlbumName);
    }

    [Fact]
    public async Task Sample_seven_seas_of_rhye_dedupes_across_queen_and_queen_ii()
    {
        var repository = new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums());

        var song = await repository.GetSongBySlugAsync("seven-seas-of-rhye");

        Assert.NotNull(song);
        Assert.Equal("Seven Seas of Rhye", song.Title);
        Assert.Equal(2, song.Appearances.Count);
        Assert.Equal(["Queen", "Queen II"], song.Appearances.Select(appearance => appearance.AlbumName).ToArray());
    }

    [Fact]
    public void Set_based_catalogue_matches_album_detail_aggregation_for_multi_album_songs()
    {
        var cover = AlbumCoverUrl.Build("kya.webp");
        var albums = new[]
        {
            new AlbumDetail(
                1,
                "Queen",
                "queen",
                1973,
                "Queen",
                null,
                null,
                [
                    new AlbumSong(101, "Seven Seas of Rhye", false, "Early lyrics", null, null),
                    new AlbumSong(102, "Keep Yourself Alive", true, "KYA lyrics", "Debut single", cover),
                ],
                new DateTime(1973, 7, 13)),
            new AlbumDetail(
                2,
                "Queen II",
                "queen-ii",
                1974,
                "Queen",
                null,
                null,
                [new AlbumSong(201, "Seven Seas of Rhye", false, "Later lyrics", "II notes", null)],
                new DateTime(1974, 3, 8)),
        };

        var fromAlbums = SongCatalog.TracksFromAlbums(albums);
        var fromSet = new[]
        {
            new SongTrackSource(101, "Seven Seas of Rhye", "Early lyrics", null, false, 1, "Queen", new DateTime(1973, 7, 13)),
            new SongTrackSource(102, "Keep Yourself Alive", "KYA lyrics", "Debut single", true, 1, "Queen", new DateTime(1973, 7, 13), cover),
            new SongTrackSource(201, "Seven Seas of Rhye", "Later lyrics", "II notes", false, 2, "Queen II", new DateTime(1974, 3, 8)),
        };

        var sevenSeas = SongCatalog.DetailFor(fromSet, "seven-seas-of-rhye");
        AssertSongDetailEqual(SongCatalog.DetailFor(fromAlbums, "seven-seas-of-rhye"), sevenSeas);
        Assert.Equal("Seven Seas of Rhye", sevenSeas!.Title);
        Assert.Equal("Early lyrics", sevenSeas.Lyrics);
        Assert.Equal("Queen", sevenSeas.Appearances[0].AlbumName);
        Assert.Equal(1973, sevenSeas.Appearances[0].ReleaseYear);
        Assert.Equal("II notes", sevenSeas.Appearances[1].Notes);
        Assert.Equal("/discography/albums/1/queen", DiscographyRoutes.GetAlbumPath(sevenSeas.Appearances[0].AlbumId, sevenSeas.Appearances[0].AlbumSlug));
        Assert.Equal("/discography/albums/2/queen-ii", DiscographyRoutes.GetAlbumPath(sevenSeas.Appearances[1].AlbumId, sevenSeas.Appearances[1].AlbumSlug));

        var keepAlive = SongCatalog.DetailFor(fromSet, "keep-yourself-alive");
        AssertSongDetailEqual(SongCatalog.DetailFor(fromAlbums, "keep-yourself-alive"), keepAlive);
        Assert.Equal(cover, keepAlive!.CoverUrl);
        Assert.True(keepAlive.Appearances[0].IsSingle);
        Assert.Equal("Debut single", keepAlive.Appearances[0].Notes);
        Assert.Equal(cover, keepAlive.Appearances[0].CoverUrl);
    }

    internal static void AssertSongDetailEqual(SongDetail? expected, SongDetail? actual)
    {
        Assert.Equal(expected is null, actual is null);
        if (expected is null || actual is null)
        {
            return;
        }

        Assert.Equal(expected.Slug, actual.Slug);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Lyrics, actual.Lyrics);
        Assert.Equal(expected.CoverUrl, actual.CoverUrl);
        Assert.Equal(expected.StreamingLinks, actual.StreamingLinks);
        Assert.Equal(
            expected.Appearances.Select(appearance => (appearance.AlbumId, appearance.AlbumName, appearance.AlbumSlug, appearance.ReleaseYear, appearance.IsSingle, appearance.Notes, appearance.CoverUrl)),
            actual.Appearances.Select(appearance => (appearance.AlbumId, appearance.AlbumName, appearance.AlbumSlug, appearance.ReleaseYear, appearance.IsSingle, appearance.Notes, appearance.CoverUrl)));
        foreach (var (expectedAppearance, actualAppearance) in expected.Appearances.Zip(actual.Appearances))
        {
            Assert.Equal(expectedAppearance.StreamingLinks, actualAppearance.StreamingLinks);
        }
    }

    [Fact]
    public async Task Canonical_lyrics_come_from_the_earliest_non_blank_appearance()
    {
        var tracks = new[]
        {
            new SongTrackSource(20, "Love of My Life", "Later lyrics", "Compilation note", false, 9, "Greatest Hits", new DateTime(1981, 1, 1)),
            new SongTrackSource(5, "Love of My Life", "Original lyrics", "Studio note", true, 4, "A Night at the Opera", new DateTime(1975, 11, 21)),
        };

        var song = SongCatalog.DetailFor(tracks, "love-of-my-life");

        Assert.NotNull(song);
        Assert.Equal("Love of My Life", song.Title);
        Assert.Equal("Original lyrics", song.Lyrics);
        Assert.Equal("Studio note", song.Appearances[0].Notes);
        Assert.True(song.Appearances[0].IsSingle);
        Assert.Equal("Compilation note", song.Appearances[1].Notes);
        Assert.False(song.Appearances[1].IsSingle);

        await Task.CompletedTask;
    }

    [Fact]
    public void Related_content_matches_title_only_and_omits_song_documents()
    {
        var documents = new[]
        {
            new QueenZone.Data.Entities.SearchDocumentEntity
            {
                ContentType = SiteSearchContentType.News,
                Title = "Bohemian Rhapsody",
                Url = "/news/1/bohemian-rhapsody",
            },
            new QueenZone.Data.Entities.SearchDocumentEntity
            {
                ContentType = SiteSearchContentType.Song,
                Title = "Bohemian Rhapsody",
                Url = "/songs/bohemian-rhapsody",
            },
            new QueenZone.Data.Entities.SearchDocumentEntity
            {
                ContentType = SiteSearchContentType.Forum,
                Title = "Bohemian Rhapsody discussion",
                Url = "/forum/topic/1/discussion",
            },
        };

        var related = SongRelatedContent.ForTitle(documents, "Bohemian Rhapsody");

        var news = Assert.Single(related);
        Assert.Equal("News", news.Label);
        Assert.Equal("/news/1/bohemian-rhapsody", Assert.Single(news.Links).Url);
    }

    [Theory]
    [InlineData(SiteSearchContentType.Forum)]
    [InlineData(SiteSearchContentType.News)]
    [InlineData(SiteSearchContentType.Article)]
    [InlineData(SiteSearchContentType.LegacyArticle)]
    [InlineData(SiteSearchContentType.Photo)]
    [InlineData(SiteSearchContentType.Timeline)]
    [InlineData(SiteSearchContentType.Tribute)]
    [InlineData(SiteSearchContentType.FreddieTribute)]
    public void Related_types_are_title_match_candidates(string contentType)
    {
        Assert.True(SongRelatedContent.IsRelatedType(contentType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(SiteSearchContentType.Song)]
    [InlineData(SiteSearchContentType.Discography)]
    [InlineData(SiteSearchContentType.Biography)]
    public void Song_and_unrelated_document_types_are_not_related(string? contentType)
    {
        Assert.False(SongRelatedContent.IsRelatedType(contentType));
    }

    [Theory]
    [InlineData(SiteSearchContentType.Article, "articles", "Articles")]
    [InlineData(SiteSearchContentType.LegacyArticle, "articles", "Articles")]
    [InlineData(SiteSearchContentType.Tribute, "tribute", "Freddie Tribute")]
    [InlineData(SiteSearchContentType.FreddieTribute, "tribute", "Freddie Tribute")]
    [InlineData(SiteSearchContentType.News, "news", "News")]
    [InlineData(SiteSearchContentType.Forum, "forum", "Forum")]
    [InlineData(SiteSearchContentType.Photo, "photo", "Photography")]
    [InlineData(SiteSearchContentType.Timeline, "timeline", "Timeline")]
    [InlineData("unknown", "unknown", "unknown")]
    public void Related_sections_use_locked_keys_and_labels(string contentType, string key, string label)
    {
        Assert.Equal(key, SongRelatedContent.SectionKey(contentType));
        Assert.Equal(label, SongRelatedContent.SectionLabel(contentType));
    }
}
