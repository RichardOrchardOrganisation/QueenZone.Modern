using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs <see cref="EfAdminDiscographyRepository"/> and the
/// <see cref="AddAlbumSongTrackNumberAndCover"/> migration against a scratch SQL Server
/// database whose legacy tables match the <c>queenzone_legacy_sync</c> dump used by
/// <see cref="DiscographyRepositorySqlServerTests"/> (tinyint album id, smallint identity
/// song id, NOT NULL lyrics) before the migration adds TRACK_NUMBER / COVER_URL.
/// </summary>
public sealed class AdminDiscographyRepositorySqlServerTests : IAsyncLifetime
{
    private static readonly string[] LegacySchemaBatches =
    [
        """
        CREATE TABLE dbo.Q_ARTIST_T
        (
            Q_ARTIST_ID smallint IDENTITY(1,1) NOT NULL PRIMARY KEY,
            ARTIST_NAME varchar(50) NOT NULL
        );
        """,
        """
        CREATE TABLE dbo.Q_ALBUM_T
        (
            Q_ALBUM_ID tinyint NOT NULL PRIMARY KEY,
            ALBUM_NAME varchar(50) NULL,
            AMAZON varchar(100) NULL,
            ARTIST tinyint NOT NULL,
            RELEASE_DATE smalldatetime NULL,
            GENERAL_NOTES varchar(4000) NULL,
            THUMB_URL varchar(50) NULL,
            THUMB_HEIGHT smallint NULL,
            THUMB_WIDTH smallint NULL,
            PICTURE_URL varchar(50) NULL,
            PICTURE_HEIGHT smallint NULL,
            PICTURE_WIDTH smallint NULL,
            ACTIVE tinyint NOT NULL,
            CREATE_DATE smalldatetime NOT NULL
        );
        """,
        """
        CREATE TABLE dbo.Q_ALBUM_SONG_T
        (
            Q_ALBUM_SONG_ID smallint IDENTITY(1,1) NOT NULL PRIMARY KEY,
            SONG_TITLE varchar(100) NOT NULL,
            SONG_LYRICS varchar(4000) NOT NULL,
            Q_ALBUM_ID smallint NOT NULL,
            SONG_NOTES varchar(2000) NULL,
            Q_ARTIST_ID tinyint NOT NULL,
            IS_SINGLE tinyint NOT NULL,
            CREATE_DATE smalldatetime NOT NULL
        );
        """,
        """
        INSERT INTO dbo.Q_ARTIST_T (ARTIST_NAME) VALUES ('Queen'), ('Freddie Mercury');
        INSERT INTO dbo.Q_ALBUM_T (Q_ALBUM_ID, ALBUM_NAME, ARTIST, RELEASE_DATE, GENERAL_NOTES, THUMB_URL, ACTIVE, CREATE_DATE)
        VALUES
            (2, 'Queen II', 1, '1974-03-08', 'Second album.', 'queen-ii.jpg', 1, '2020-01-01'),
            (5, 'Hidden', 1, NULL, NULL, NULL, 0, '2020-01-01');
        INSERT INTO dbo.Q_ALBUM_SONG_T (SONG_TITLE, SONG_LYRICS, Q_ALBUM_ID, SONG_NOTES, Q_ARTIST_ID, IS_SINGLE, CREATE_DATE)
        VALUES
            ('Procession', '', 2, NULL, 1, 0, '2020-01-01'),
            ('Father to Son', 'A word in your ear', 2, 'Notes', 1, 0, '2020-01-01'),
            ('The Loser in the End', '', 2, NULL, 1, 0, '2020-01-01'),
            ('Other album track', '', 5, NULL, 1, 0, '2020-01-01');
        """,
    ];

    private readonly string databaseName = $"QueenZoneAdminDiscographyTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfAdminDiscographyRepository repository = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            foreach (var batch in LegacySchemaBatches)
            {
                await schema.Database.ExecuteSqlRawAsync(batch);
            }
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfAdminDiscographyRepository(dbContext);
        await DiscographyStreamingLinkSchema.CreateAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Migration_backfills_existing_order_and_round_trips()
    {
        await ApplyMigrationAsync(up: true);
        await ApplyMigrationAsync(up: false);
        await ApplyMigrationAsync(up: true);

        var numbers = await dbContext.Database.SqlQueryRaw<int>(
            "SELECT CAST(TRACK_NUMBER AS int) AS Value FROM dbo.Q_ALBUM_SONG_T WHERE Q_ALBUM_ID = 2 ORDER BY Q_ALBUM_SONG_ID")
            .ToListAsync();
        Assert.Equal([1, 2, 3], numbers);

        // Re-running Up is harmless: the column guards skip and the backfill only touches NULLs.
        await ApplyMigrationAsync(up: true);
        var album = await repository.GetAlbumAsync(2);
        Assert.Equal(["Procession", "Father to Son", "The Loser in the End"], album!.Songs.Select(song => song.Title));
    }

    [Fact]
    public async Task Album_reads_include_hidden_albums_and_artists()
    {
        await ApplyMigrationAsync(up: true);

        var albums = await repository.GetAlbumsAsync();
        Assert.Equal([2, 5], albums.Select(album => album.AlbumId));
        Assert.Equal(3, albums[0].SongCount);
        Assert.Equal("queen-ii.jpg", albums[0].ThumbFileName);
        Assert.False(albums[1].IsActive);

        var album = await repository.GetAlbumAsync(2);
        Assert.Equal("Queen II", album!.Name);
        Assert.Equal(1, album.ArtistId);
        Assert.Equal(new DateTime(1974, 3, 8), album.ReleaseDate);
        Assert.True(album.IsActive);
        var fatherToSon = album.Songs[1];
        Assert.Equal(2, fatherToSon.TrackNumber);
        Assert.Equal("A word in your ear", fatherToSon.Lyrics);
        Assert.Null(album.Songs[0].Lyrics);

        Assert.Equal(["Freddie Mercury", "Queen"], (await repository.GetArtistsAsync()).Select(artist => artist.Name));
        Assert.Null(await repository.GetAlbumAsync(99));
    }

    [Fact]
    public async Task Album_create_update_cover_and_delete()
    {
        await ApplyMigrationAsync(up: true);

        var albumId = await repository.CreateAlbumAsync(new AdminAlbumInput(" Jazz ", 1, new DateTime(1978, 11, 10, 15, 30, 0), "  ", false));
        Assert.Equal(6, albumId);

        await repository.UpdateAlbumAsync(albumId, new AdminAlbumInput("Jazz", 2, new DateTime(1978, 11, 10), "Mustapha", true));
        await repository.SetAlbumCoverAsync(albumId, new AdminAlbumCover("jazz.webp", 1200, 1200, "jazz_t.webp", 300, 300));

        var album = await repository.GetAlbumAsync(albumId);
        Assert.Equal("Jazz", album!.Name);
        Assert.Equal(2, album.ArtistId);
        Assert.Equal(new DateTime(1978, 11, 10), album.ReleaseDate);
        Assert.Equal("Mustapha", album.GeneralNotes);
        Assert.True(album.IsActive);
        Assert.Equal("jazz.webp", album.PictureFileName);
        Assert.Equal("jazz_t.webp", album.ThumbFileName);

        await repository.CreateSongAsync(albumId, new AdminSongInput("Bicycle Race", null, null, true), 1);
        await repository.SetAlbumCoverAsync(albumId, null);
        Assert.Null((await repository.GetAlbumAsync(albumId))!.CoverUrl);

        await repository.DeleteAlbumAsync(albumId);
        Assert.Null(await repository.GetAlbumAsync(albumId));
        var orphanSongs = await dbContext.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS Value FROM dbo.Q_ALBUM_SONG_T WHERE Q_ALBUM_ID = {albumId}").SingleAsync();
        Assert.Equal(0, orphanSongs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAlbumAsync(99, new AdminAlbumInput("x", 1, null, null, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetAlbumCoverAsync(99, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAlbumAsync(99));
    }

    [Fact]
    public async Task Album_ids_stop_at_the_tinyint_limit()
    {
        await ApplyMigrationAsync(up: true);
        await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO dbo.Q_ALBUM_T (Q_ALBUM_ID, ALBUM_NAME, ARTIST, ACTIVE, CREATE_DATE) VALUES (255, 'Last', 1, 0, '2020-01-01')");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAlbumAsync(new AdminAlbumInput("Overflow", 1, null, null, false)));
    }

    [Fact]
    public async Task Songs_insert_move_update_cover_and_delete_keep_a_dense_order()
    {
        await ApplyMigrationAsync(up: true);

        var ogre = await repository.CreateSongAsync(2, new AdminSongInput(" Ogre Battle ", null, " ", false), 2);
        Assert.Equal(["Procession", "Ogre Battle", "Father to Son", "The Loser in the End"], await TitlesAsync());

        var appended = await repository.CreateSongAsync(2, new AdminSongInput("Seven Seas of Rhye", "Fear me", null, true), 0);
        Assert.Equal(5, (await repository.GetSongAsync(appended))!.TrackNumber);

        await repository.MoveSongAsync(ogre, 4);
        Assert.Equal(["Procession", "Father to Son", "The Loser in the End", "Ogre Battle", "Seven Seas of Rhye"], await TitlesAsync());

        await repository.UpdateSongAsync(ogre, new AdminSongInput("Ogre Battle", "Lyrics", "Heavy", true));
        await repository.SetSongCoverAsync(ogre, "ogre.webp");
        var song = await repository.GetSongAsync(ogre);
        Assert.Equal(4, song!.TrackNumber);
        Assert.Equal("Lyrics", song.Lyrics);
        Assert.Equal("Heavy", song.Notes);
        Assert.True(song.IsSingle);
        Assert.Equal("ogre.webp", song.CoverFileName);

        await repository.SetSongCoverAsync(ogre, null);
        Assert.Null((await repository.GetSongAsync(ogre))!.CoverFileName);

        var procession = (await repository.GetAlbumAsync(2))!.Songs[0].SongId;
        await repository.DeleteSongAsync(procession);
        var numbers = await dbContext.Database.SqlQueryRaw<int>(
            "SELECT CAST(TRACK_NUMBER AS int) AS Value FROM dbo.Q_ALBUM_SONG_T WHERE Q_ALBUM_ID = 2 ORDER BY TRACK_NUMBER")
            .ToListAsync();
        Assert.Equal([1, 2, 3, 4], numbers);
        Assert.Null(await repository.GetSongAsync(procession));

        // Other albums keep their own numbering.
        Assert.Equal(1, (await repository.GetAlbumAsync(5))!.Songs.Single().TrackNumber);

        var input = new AdminSongInput("x", null, null, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateSongAsync(99, input, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateSongAsync(9999, input));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.MoveSongAsync(9999, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetSongCoverAsync(9999, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteSongAsync(9999));
    }

    [Fact]
    public async Task Streaming_links_set_replace_remove_and_are_deleted_with_their_album_or_song()
    {
        await ApplyMigrationAsync(up: true);
        var album = (await repository.GetAlbumAsync(2))!;
        var procession = album.Songs[0].SongId;
        var fatherToSon = album.Songs[1].SongId;

        await repository.SetAlbumStreamingLinkAsync(2, StreamingProvider.Spotify, Write("https://open.spotify.com/album/4KfrGvYXcZsFgMHVIdzkoW?si=1", StreamingLinkKind.Album, "admin@example.test"));
        await repository.SetAlbumStreamingLinkAsync(2, StreamingProvider.AppleMusic, Write("https://music.apple.com/gb/album/queen-ii/111", StreamingLinkKind.Album));
        await repository.SetSongStreamingLinkAsync(procession, StreamingProvider.AppleMusic, Write("https://music.apple.com/gb/album/queen-ii/111?i=222", StreamingLinkKind.Track));
        await repository.SetSongStreamingLinkAsync(fatherToSon, StreamingProvider.Spotify, Write("spotify:track:0000000000000000000001", StreamingLinkKind.Track));

        album = (await repository.GetAlbumAsync(2))!;
        Assert.Equal([StreamingProvider.Spotify, StreamingProvider.AppleMusic], album.StreamingLinks.Select(link => link.Provider));
        Assert.Equal("https://open.spotify.com/album/4KfrGvYXcZsFgMHVIdzkoW", album.StreamingLinks[0].Url);
        Assert.Equal("admin@example.test", album.StreamingLinks[0].UpdatedBy);
        Assert.Equal("222", Assert.Single(album.Songs[0].StreamingLinks).ExternalId);
        Assert.Equal(StreamingProvider.Spotify, Assert.Single((await repository.GetSongAsync(fatherToSon))!.StreamingLinks).Provider);

        // Replacing keeps one row per slot; an imported overwrite records its source.
        await repository.SetAlbumStreamingLinkAsync(
            2,
            StreamingProvider.AppleMusic,
            new StreamingLinkWrite(Parse("https://music.apple.com/us/album/queen-ii/333", StreamingLinkKind.Album), StreamingLinkSource.Imported, null));
        var apple = (await repository.GetAlbumAsync(2))!.StreamingLinks.Single(link => link.Provider == StreamingProvider.AppleMusic);
        Assert.Equal("333", apple.ExternalId);
        Assert.Equal(StreamingLinkSource.Imported, apple.Source);
        Assert.Equal(4, await LinkCountAsync());

        await repository.SetAlbumStreamingLinkAsync(2, StreamingProvider.Spotify, null);
        await repository.SetAlbumStreamingLinkAsync(2, StreamingProvider.Spotify, null);
        Assert.Equal([StreamingProvider.AppleMusic], (await repository.GetAlbumAsync(2))!.StreamingLinks.Select(link => link.Provider));

        await repository.DeleteSongAsync(procession);
        Assert.Equal(2, await LinkCountAsync());
        await repository.DeleteAlbumAsync(2);
        Assert.Equal(0, await LinkCountAsync());

        var track = Write("spotify:track:0000000000000000000001", StreamingLinkKind.Track);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetAlbumStreamingLinkAsync(5, StreamingProvider.Spotify, track));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetSongStreamingLinkAsync(4, StreamingProvider.AppleMusic, track));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetAlbumStreamingLinkAsync(99, StreamingProvider.Spotify, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetSongStreamingLinkAsync(9999, StreamingProvider.Spotify, track));
    }

    [Fact]
    public async Task Streaming_link_indexes_allow_one_row_per_album_or_track_and_provider()
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.DiscographyStreamingLinks (AlbumId, AlbumSongId, Provider, ExternalId, Url, Source, UpdatedAtUtc)
            VALUES
                (2, NULL, 'spotify', 'a', 'https://open.spotify.com/album/a', 'manual', '2026-01-01'),
                (2, NULL, 'apple-music', 'b', 'https://music.apple.com/gb/album/b/1', 'manual', '2026-01-01'),
                (2, 1, 'spotify', 'c', 'https://open.spotify.com/track/c', 'manual', '2026-01-01'),
                (2, 2, 'spotify', 'd', 'https://open.spotify.com/track/d', 'manual', '2026-01-01'),
                (5, NULL, 'spotify', 'e', 'https://open.spotify.com/album/e', 'manual', '2026-01-01');
            """);

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.DiscographyStreamingLinks (AlbumId, AlbumSongId, Provider, ExternalId, Url, Source, UpdatedAtUtc)
            VALUES (2, NULL, 'spotify', 'f', 'https://open.spotify.com/album/f', 'manual', '2026-01-01');
            """));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.DiscographyStreamingLinks (AlbumId, AlbumSongId, Provider, ExternalId, Url, Source, UpdatedAtUtc)
            VALUES (2, 1, 'spotify', 'g', 'https://open.spotify.com/track/g', 'manual', '2026-01-01');
            """));
    }

    private static StreamingLinkWrite Write(string url, StreamingLinkKind kind, string? updatedBy = null) =>
        new(Parse(url, kind), StreamingLinkSource.Manual, updatedBy);

    private static ParsedStreamingLink Parse(string url, StreamingLinkKind kind)
    {
        Assert.True(StreamingLinkUrl.TryParse(url, kind, out var link, out var error), error);
        return link;
    }

    private Task<int> LinkCountAsync() =>
        dbContext.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.DiscographyStreamingLinks").SingleAsync();

    private async Task<IReadOnlyList<string>> TitlesAsync() =>
        (await repository.GetAlbumAsync(2))!.Songs.Select(song => song.Title).ToList();

    private async Task ApplyMigrationAsync(bool up)
    {
        var migration = new AddAlbumSongTrackNumberAndCover();
        var commands = dbContext.GetService<IMigrationsSqlGenerator>()
            .Generate(up ? migration.UpOperations : migration.DownOperations);
        foreach (var command in commands)
        {
            await dbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
