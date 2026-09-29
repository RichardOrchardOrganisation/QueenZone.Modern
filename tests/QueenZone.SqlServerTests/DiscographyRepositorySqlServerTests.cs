using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfDiscographyRepository"/> (legacy <c>Q_ALBUM_LIST_SP</c> /
/// <c>Q_ALBUM_T_DISPLAY_SP</c> / <c>Q_ALBUM_SONG_T_LIST_SP</c>) against a scratch SQL Server
/// database (#1672 / #1886). Tables and procedures were copied from <c>docs/db-schema.txt</c>
/// (the committed legacy dump; this environment cannot reach <c>queenzone_legacy_sync</c>):
/// <c>tinyint</c> album ids and flags, <c>smallint</c> song ids, <c>smalldatetime</c> release
/// dates. The read-only mirror probe is <c>EfDiscographyRepositoryLegacyProbeTests</c> in
/// <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class DiscographyRepositorySqlServerTests : IAsyncLifetime
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
            CREATE_DATE smalldatetime NOT NULL DEFAULT (getdate())
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
            CREATE_DATE smalldatetime NOT NULL DEFAULT (getdate())
        );
        """,
        """
        CREATE PROCEDURE [dbo].[Q_ALBUM_LIST_SP]
         AS
        SELECT     Q_ALBUM_ID, ALBUM_NAME, year(release_date) as release_year, isnull(THUMB_URL, '') as thumb_url, isnull(THUMB_HEIGHT, '') as thumb_height, isnull(THUMB_WIDTH, '') as thumb_width
        FROM         Q_ALBUM_T
        WHERE     (ACTIVE = 1)
        ORDER BY year(RELEASE_DATE), month(release_date) asc
        """,
        """
        CREATE PROCEDURE [dbo].[Q_ALBUM_T_DISPLAY_SP]
        	( @Q_ALBUM_ID int )
        AS
        BEGIN

        set nocount on

        SELECT     Q_ALBUM_T.Q_ALBUM_ID, Q_ALBUM_T.ALBUM_NAME, Q_ALBUM_T.AMAZON, Q_ALBUM_T.ARTIST, Q_ALBUM_T.RELEASE_DATE, Q_ALBUM_T.GENERAL_NOTES, 
                              Q_ARTIST_T.ARTIST_NAME, Q_ALBUM_T.THUMB_URL, Q_ALBUM_T.THUMB_HEIGHT, Q_ALBUM_T.THUMB_WIDTH, Q_ALBUM_T.PICTURE_URL, 
                              Q_ALBUM_T.ACTIVE
        FROM         Q_ALBUM_T INNER JOIN
                              Q_ARTIST_T ON Q_ALBUM_T.ARTIST = Q_ARTIST_T.Q_ARTIST_ID
        		where Q_ALBUM_ID = @Q_ALBUM_ID

        END
        """,
        """
        CREATE PROCEDURE [dbo].[Q_ALBUM_SONG_T_LIST_SP]
        @Q_ALBUM_ID smallint
        AS
        BEGIN

        set nocount on

        SELECT     Q_ALBUM_SONG_T.SONG_TITLE, Q_ALBUM_SONG_T.Q_ALBUM_SONG_ID, Q_ALBUM_SONG_T.SONG_LYRICS, Q_ALBUM_SONG_T.Q_ALBUM_ID, 
                              Q_ALBUM_SONG_T.SONG_NOTES, Q_ALBUM_SONG_T.Q_ARTIST_ID, Q_ALBUM_SONG_T.IS_SINGLE, Q_ALBUM_T.ALBUM_NAME
        FROM         Q_ALBUM_SONG_T INNER JOIN
                              Q_ALBUM_T ON Q_ALBUM_SONG_T.Q_ALBUM_ID = Q_ALBUM_T.Q_ALBUM_ID 
        			where Q_ALBUM_SONG_T.Q_ALBUM_ID = @Q_ALBUM_ID
        		order by
        			Q_ALBUM_SONG_ID

        END
        """,
    ];

    private readonly string databaseName = $"QueenZoneDiscographyTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfDiscographyRepository repository = null!;

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
        repository = new EfDiscographyRepository(dbContext);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Stored_procedure_reads_order_albums_and_materialize_legacy_types()
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.Q_ARTIST_T (ARTIST_NAME) VALUES ('Queen');
            INSERT INTO dbo.Q_ALBUM_T
                (Q_ALBUM_ID, ALBUM_NAME, ARTIST, RELEASE_DATE, GENERAL_NOTES, THUMB_URL, PICTURE_URL, ACTIVE)
            VALUES
                (1, 'Queen II', 1, '1974-03-08', 'Second album.', 'queen-ii.jpg', 'queen-ii-full.jpg', 1),
                (2, 'Sheer Heart Attack', 1, '1974-11-08', NULL, 'sha.jpg', NULL, 1),
                (3, 'A Night at the Opera', 1, '1975-11-21', 'Notes', NULL, 'opera.jpg', 1),
                (4, 'Hidden', 1, '1970-01-01', NULL, NULL, NULL, 0);
            INSERT INTO dbo.Q_ALBUM_SONG_T
                (SONG_TITLE, SONG_LYRICS, Q_ALBUM_ID, SONG_NOTES, Q_ARTIST_ID, IS_SINGLE)
            VALUES
                ('Procession', '', 1, NULL, 1, 0),
                ('Bohemian Rhapsody', 'Is this the real life', 3, 'Single', 1, 1),
                ('You''re My Best Friend', '   ', 3, NULL, 1, 0);
            """);

        var albums = await repository.GetAlbumsAsync();
        Assert.Equal([1, 2, 3], albums.Select(album => album.AlbumId));
        Assert.Equal(["Queen II", "Sheer Heart Attack", "A Night at the Opera"], albums.Select(album => album.Name));
        Assert.Equal([1974, 1974, 1975], albums.Select(album => album.ReleaseYear));
        Assert.Equal("queen-ii", albums[0].Slug);
        Assert.Equal(AlbumCoverUrl.Build("queen-ii.jpg"), albums[0].ThumbnailUrl);
        // Q_ALBUM_LIST_SP projects isnull(THUMB_URL, '') so a missing filename is not a cover URL.
        Assert.Null(albums[2].ThumbnailUrl);

        var opera = await repository.GetAlbumByIdAsync(3);
        Assert.NotNull(opera);
        Assert.Equal("A Night at the Opera", opera.Name);
        Assert.Equal("a-night-at-the-opera", opera.Slug);
        Assert.Equal(1975, opera.ReleaseYear);
        Assert.Equal("Queen", opera.ArtistName);
        Assert.Equal("Notes", opera.GeneralNotes);
        Assert.Equal(AlbumCoverUrl.Build("opera.jpg"), opera.CoverUrl);
        Assert.Equal(["Bohemian Rhapsody", "You're My Best Friend"], opera.Songs.Select(song => song.Title));
        Assert.True(opera.Songs[0].IsSingle);
        Assert.Equal("Is this the real life", opera.Songs[0].Lyrics);
        Assert.Equal("Single", opera.Songs[0].Notes);
        Assert.False(opera.Songs[1].IsSingle);
        Assert.Null(opera.Songs[1].Lyrics);
        Assert.Null(opera.Songs[1].Notes);

        var queenIi = await repository.GetAlbumByIdAsync(1);
        Assert.Equal("Procession", Assert.Single(queenIi!.Songs).Title);
        Assert.Equal(AlbumCoverUrl.Build("queen-ii-full.jpg"), queenIi.CoverUrl);

        // Q_ALBUM_T_DISPLAY_SP does not filter ACTIVE; the repository hides inactive rows.
        Assert.Null(await repository.GetAlbumByIdAsync(4));
        Assert.Null(await repository.GetAlbumByIdAsync(99));
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy objects come from LegacySchemaBatches.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
