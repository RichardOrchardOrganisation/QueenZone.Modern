using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Reads the legacy studio-album catalogue via its original stored procedures
/// (<c>Q_ALBUM_LIST_SP</c>, <c>Q_ALBUM_T_DISPLAY_SP</c>) plus a direct tracklist query
/// ordered by <c>TRACK_NUMBER</c>, invoked through EF Core rather than Dapper. Streaming links
/// come from the EF-managed <c>DiscographyStreamingLinks</c> table.
/// </summary>
public sealed class EfDiscographyRepository : IDiscographyRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly string listSql;
    private readonly Func<int, FormattableString> displaySql;
    private readonly Func<int, FormattableString> songsSql;

    public EfDiscographyRepository(QueenZoneDbContext dbContext)
        : this(
            dbContext,
            listSql: EfProductionSql.CreateDiscographyQueries().ListSql,
            displaySql: EfProductionSql.CreateDiscographyQueries().DisplaySql,
            songsSql: EfProductionSql.CreateDiscographyQueries().SongsSql)
    {
    }

    internal EfDiscographyRepository(
        QueenZoneDbContext dbContext,
        string listSql,
        Func<int, FormattableString> displaySql,
        Func<int, FormattableString> songsSql)
    {
        this.dbContext = dbContext;
        this.listSql = listSql;
        this.displaySql = displaySql;
        this.songsSql = songsSql;
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default)
    {
        // Use EfSql for EXEC (flexible ADO conversion for tinyint/smallint); SqlQuery for tests.
        IReadOnlyList<AlbumListRow> rows;
        if (IsExec(listSql))
        {
            rows = await EfSql.QueryProcAsync<AlbumListRow>(
                dbContext,
                ProcName(listSql),
                cancellationToken: cancellationToken);
        }
        else
        {
            rows = await dbContext.Database
                .SqlQueryRaw<AlbumListRow>(listSql)
                .ToListAsync(cancellationToken);
        }

        return rows
            .Select(row => new AlbumSummary(
                AlbumId: row.Q_ALBUM_ID,
                Name: row.ALBUM_NAME,
                Slug: NewsSlug.Slugify(row.ALBUM_NAME),
                ReleaseYear: row.release_year,
                ThumbnailUrl: AlbumCoverUrl.Build(row.thumb_url)))
            .ToList();
    }

    public async Task<AlbumDetail?> GetAlbumByIdAsync(int albumId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AlbumDisplayRow> albumRows;
        var display = displaySql(albumId);
        if (IsExec(display.Format))
        {
            albumRows = await EfSql.QueryProcAsync<AlbumDisplayRow>(
                dbContext,
                ProcName(display.Format),
                command => command.Parameters.Add(EfSql.Input("@Q_ALBUM_ID", albumId)),
                cancellationToken: cancellationToken);
        }
        else
        {
            albumRows = await dbContext.Database
                .SqlQuery<AlbumDisplayRow>(display)
                .ToListAsync(cancellationToken);
        }

        var album = albumRows.FirstOrDefault();

        // Q_ALBUM_T_DISPLAY_SP does not filter on ACTIVE itself, so apply the visibility
        // gate here (mirrors the DISPLAY=1 pattern used for fan performances).
        if (album is null || album.ACTIVE != 1)
        {
            return null;
        }

        IReadOnlyList<AlbumSongRow> songRows;
        var songs = songsSql(albumId);
        if (IsExec(songs.Format))
        {
            songRows = await EfSql.QueryProcAsync<AlbumSongRow>(
                dbContext,
                ProcName(songs.Format),
                command => command.Parameters.Add(EfSql.Input("@Q_ALBUM_ID", albumId)),
                cancellationToken: cancellationToken);
        }
        else
        {
            songRows = await dbContext.Database
                .SqlQuery<AlbumSongRow>(songs)
                .ToListAsync(cancellationToken);
        }

        var links = await dbContext.DiscographyStreamingLinks
            .AsNoTracking()
            .Where(link => link.AlbumId == albumId)
            .Select(link => new { link.AlbumSongId, link.Provider, link.Url })
            .ToListAsync(cancellationToken);

        IReadOnlyList<StreamingLink> LinksFor(int? albumSongId) =>
            links
                .Where(link => link.AlbumSongId == albumSongId)
                .OrderBy(link => link.Provider)
                .Select(link => new StreamingLink(link.Provider, link.Url))
                .ToList();

        var songItems = songRows
            .Select(row => new AlbumSong(
                row.Q_ALBUM_SONG_ID,
                row.SONG_TITLE,
                row.IS_SINGLE == 1,
                string.IsNullOrWhiteSpace(row.SONG_LYRICS) ? null : row.SONG_LYRICS,
                string.IsNullOrWhiteSpace(row.SONG_NOTES) ? null : row.SONG_NOTES,
                AlbumCoverUrl.Build(row.COVER_URL))
            {
                StreamingLinks = LinksFor(row.Q_ALBUM_SONG_ID),
            })
            .ToList();

        return new AlbumDetail(
            AlbumId: album.Q_ALBUM_ID,
            Name: album.ALBUM_NAME,
            Slug: NewsSlug.Slugify(album.ALBUM_NAME),
            ReleaseYear: album.RELEASE_DATE?.Year,
            ArtistName: album.ARTIST_NAME,
            GeneralNotes: string.IsNullOrWhiteSpace(album.GENERAL_NOTES) ? null : album.GENERAL_NOTES,
            CoverUrl: AlbumCoverUrl.Build(album.PICTURE_URL) ?? AlbumCoverUrl.Build(album.THUMB_URL),
            Songs: songItems,
            ReleaseDate: album.RELEASE_DATE)
        {
            StreamingLinks = LinksFor(null),
        };
    }

    public Task<IReadOnlyList<SongSummary>> GetSongsAsync(CancellationToken cancellationToken = default) =>
        SongCatalog.GetSongsAsync(this, cancellationToken);

    public Task<SongDetail?> GetSongBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        SongCatalog.GetSongBySlugAsync(this, slug, cancellationToken);

    private static bool IsExec(string sql) =>
        sql.TrimStart().StartsWith("EXEC", StringComparison.OrdinalIgnoreCase);

    private static string ProcName(string execSql)
    {
        var text = execSql.Trim();
        if (text.StartsWith("EXEC ", StringComparison.OrdinalIgnoreCase))
        {
            text = text["EXEC ".Length..].Trim();
        }

        var space = text.IndexOfAny([' ', '@', '\r', '\n']);
        return space < 0 ? text : text[..space];
    }

    internal sealed class AlbumListRow
    {
        public int Q_ALBUM_ID { get; set; }

        public string ALBUM_NAME { get; set; } = string.Empty;

        public int? release_year { get; set; }

        public string? thumb_url { get; set; }
    }

    internal sealed class AlbumDisplayRow
    {
        public int Q_ALBUM_ID { get; set; }

        public string ALBUM_NAME { get; set; } = string.Empty;

        public DateTime? RELEASE_DATE { get; set; }

        public string? GENERAL_NOTES { get; set; }

        public string ARTIST_NAME { get; set; } = string.Empty;

        public string? THUMB_URL { get; set; }

        public string? PICTURE_URL { get; set; }

        public int ACTIVE { get; set; }
    }

    internal sealed class AlbumSongRow
    {
        public int Q_ALBUM_SONG_ID { get; set; }

        public string SONG_TITLE { get; set; } = string.Empty;

        public int IS_SINGLE { get; set; }

        public string? SONG_LYRICS { get; set; }

        public string? SONG_NOTES { get; set; }

        public string? COVER_URL { get; set; }
    }
}
