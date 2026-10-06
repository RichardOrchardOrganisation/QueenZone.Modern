using System.Text;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Admin writes against legacy <c>Q_ALBUM_T</c> / <c>Q_ALBUM_SONG_T</c>. Track order is the
/// <c>TRACK_NUMBER</c> column (added by <c>AddAlbumSongTrackNumberAndCover</c>); every write that
/// changes an album's order rewrites that album's numbers to a dense 1..n sequence inside one
/// transaction. Covered by <c>AdminDiscographyRepositorySqlServerTests</c>.
/// </summary>
public sealed class EfAdminDiscographyRepository(QueenZoneDbContext dbContext) : IAdminDiscographyRepository
{
    private const string SongSelect = """
        SELECT
            CAST(s.Q_ALBUM_SONG_ID AS int) AS SongId,
            CAST(s.Q_ALBUM_ID AS int) AS AlbumId,
            CAST(ROW_NUMBER() OVER (ORDER BY ISNULL(s.TRACK_NUMBER, 32767), s.Q_ALBUM_SONG_ID) AS int) AS TrackNumber,
            s.SONG_TITLE AS Title,
            s.SONG_LYRICS AS Lyrics,
            s.SONG_NOTES AS Notes,
            CAST(CASE WHEN s.IS_SINGLE = 1 THEN 1 ELSE 0 END AS bit) AS IsSingle,
            s.COVER_URL AS CoverFileName
        FROM dbo.Q_ALBUM_SONG_T s
        """;

    public async Task<IReadOnlyList<AdminAlbumListItem>> GetAlbumsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                CAST(a.Q_ALBUM_ID AS int) AS AlbumId,
                ISNULL(a.ALBUM_NAME, '') AS Name,
                a.RELEASE_DATE AS ReleaseDate,
                CAST(CASE WHEN a.ACTIVE = 1 THEN 1 ELSE 0 END AS bit) AS IsActive,
                a.THUMB_URL AS ThumbFileName,
                (SELECT COUNT(*) FROM dbo.Q_ALBUM_SONG_T s WHERE s.Q_ALBUM_ID = a.Q_ALBUM_ID) AS SongCount
            FROM dbo.Q_ALBUM_T a
            ORDER BY CASE WHEN a.RELEASE_DATE IS NULL THEN 1 ELSE 0 END, a.RELEASE_DATE, a.Q_ALBUM_ID
            """;

        var rows = await EfSql.QuerySqlAsync<AlbumListRow>(dbContext, sql, cancellationToken: cancellationToken);
        return rows
            .Select(row => new AdminAlbumListItem(
                row.AlbumId,
                row.Name,
                row.ReleaseDate,
                row.IsActive,
                AdminDiscographyValidation.NullIfBlank(row.ThumbFileName),
                row.SongCount))
            .ToList();
    }

    public async Task<AdminAlbum?> GetAlbumAsync(int albumId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                CAST(a.Q_ALBUM_ID AS int) AS AlbumId,
                ISNULL(a.ALBUM_NAME, '') AS Name,
                CAST(a.ARTIST AS int) AS ArtistId,
                a.RELEASE_DATE AS ReleaseDate,
                a.GENERAL_NOTES AS GeneralNotes,
                CAST(CASE WHEN a.ACTIVE = 1 THEN 1 ELSE 0 END AS bit) AS IsActive,
                a.THUMB_URL AS ThumbFileName,
                a.PICTURE_URL AS PictureFileName
            FROM dbo.Q_ALBUM_T a
            WHERE a.Q_ALBUM_ID = @AlbumId
            """;

        var rows = await EfSql.QuerySqlAsync<AlbumRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@AlbumId", albumId)),
            cancellationToken: cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var songs = await GetSongsForAlbumAsync(albumId, cancellationToken);
        return new AdminAlbum(
            row.AlbumId,
            row.Name,
            row.ArtistId,
            row.ReleaseDate,
            AdminDiscographyValidation.NullIfBlank(row.GeneralNotes),
            row.IsActive,
            AdminDiscographyValidation.NullIfBlank(row.ThumbFileName),
            AdminDiscographyValidation.NullIfBlank(row.PictureFileName),
            songs);
    }

    public async Task<IReadOnlyList<AdminArtist>> GetArtistsAsync(CancellationToken cancellationToken = default)
    {
        // Q_ALBUM_T.ARTIST is tinyint, so only artists with ids up to 255 can be referenced.
        const string sql = """
            SELECT CAST(Q_ARTIST_ID AS int) AS ArtistId, ARTIST_NAME AS Name
            FROM dbo.Q_ARTIST_T
            WHERE Q_ARTIST_ID BETWEEN 1 AND 255
            ORDER BY ARTIST_NAME
            """;

        var rows = await EfSql.QuerySqlAsync<ArtistRow>(dbContext, sql, cancellationToken: cancellationToken);
        return rows.Select(row => new AdminArtist(row.ArtistId, row.Name)).ToList();
    }

    public async Task<int> CreateAlbumAsync(AdminAlbumInput input, CancellationToken cancellationToken = default)
    {
        // Q_ALBUM_ID is a tinyint primary key without IDENTITY, so allocate MAX + 1 under a range lock.
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @NewId int =
                (SELECT ISNULL(MAX(Q_ALBUM_ID), 0) + 1 FROM dbo.Q_ALBUM_T WITH (UPDLOCK, HOLDLOCK));
            IF @NewId > 255
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT -1;
                RETURN;
            END;
            INSERT INTO dbo.Q_ALBUM_T (Q_ALBUM_ID, ALBUM_NAME, ARTIST, RELEASE_DATE, GENERAL_NOTES, ACTIVE, CREATE_DATE)
            VALUES (@NewId, @Name, @ArtistId, @ReleaseDate, @GeneralNotes, @Active, GETDATE());
            COMMIT TRANSACTION;
            SELECT @NewId;
            """;

        var albumId = await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            sql,
            command => AddAlbumParameters(command, input),
            cancellationToken: cancellationToken);

        if (albumId < 1)
        {
            throw new InvalidOperationException("No album ids are left (the legacy album id is limited to 255).");
        }

        return albumId;
    }

    public async Task UpdateAlbumAsync(int albumId, AdminAlbumInput input, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Q_ALBUM_T
            SET ALBUM_NAME = @Name,
                ARTIST = @ArtistId,
                RELEASE_DATE = @ReleaseDate,
                GENERAL_NOTES = @GeneralNotes,
                ACTIVE = @Active
            WHERE Q_ALBUM_ID = @AlbumId
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                AddAlbumParameters(command, input);
                command.Parameters.Add(EfSql.Input("@AlbumId", albumId));
            },
            cancellationToken: cancellationToken);
        EnsureAlbumFound(albumId, rows);
    }

    public async Task SetAlbumCoverAsync(int albumId, AdminAlbumCover? cover, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Q_ALBUM_T
            SET PICTURE_URL = @PictureUrl,
                PICTURE_WIDTH = @PictureWidth,
                PICTURE_HEIGHT = @PictureHeight,
                THUMB_URL = @ThumbUrl,
                THUMB_WIDTH = @ThumbWidth,
                THUMB_HEIGHT = @ThumbHeight
            WHERE Q_ALBUM_ID = @AlbumId
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@PictureUrl", cover?.PictureFileName));
                command.Parameters.Add(EfSql.Input("@PictureWidth", cover?.PictureWidth));
                command.Parameters.Add(EfSql.Input("@PictureHeight", cover?.PictureHeight));
                command.Parameters.Add(EfSql.Input("@ThumbUrl", cover?.ThumbFileName));
                command.Parameters.Add(EfSql.Input("@ThumbWidth", cover?.ThumbWidth));
                command.Parameters.Add(EfSql.Input("@ThumbHeight", cover?.ThumbHeight));
                command.Parameters.Add(EfSql.Input("@AlbumId", albumId));
            },
            cancellationToken: cancellationToken);
        EnsureAlbumFound(albumId, rows);
    }

    public Task DeleteAlbumAsync(int albumId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            async token =>
            {
                await EfSql.ExecuteNonQuerySqlAsync(
                    dbContext,
                    "DELETE FROM dbo.Q_ALBUM_SONG_T WHERE Q_ALBUM_ID = @AlbumId",
                    command => command.Parameters.Add(EfSql.Input("@AlbumId", albumId)),
                    cancellationToken: token);
                var rows = await EfSql.ExecuteNonQuerySqlAsync(
                    dbContext,
                    "DELETE FROM dbo.Q_ALBUM_T WHERE Q_ALBUM_ID = @AlbumId",
                    command => command.Parameters.Add(EfSql.Input("@AlbumId", albumId)),
                    cancellationToken: token);
                EnsureAlbumFound(albumId, rows);
            },
            cancellationToken);

    public async Task<AdminAlbumSong?> GetSongAsync(int songId, CancellationToken cancellationToken = default)
    {
        var sql = SongSelect + """

            WHERE s.Q_ALBUM_ID = (SELECT Q_ALBUM_ID FROM dbo.Q_ALBUM_SONG_T WHERE Q_ALBUM_SONG_ID = @SongId)
            """;

        var rows = await EfSql.QuerySqlAsync<SongRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@SongId", songId)),
            cancellationToken: cancellationToken);
        return rows.Where(row => row.SongId == songId).Select(MapSong).FirstOrDefault();
    }

    public Task<int> CreateSongAsync(
        int albumId,
        AdminSongInput input,
        int position,
        CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            async token =>
            {
                var artistRows = await EfSql.QuerySqlAsync<ValueRow>(
                    dbContext,
                    "SELECT CAST(ARTIST AS int) AS Value FROM dbo.Q_ALBUM_T WITH (UPDLOCK) WHERE Q_ALBUM_ID = @AlbumId",
                    command => command.Parameters.Add(EfSql.Input("@AlbumId", albumId)),
                    cancellationToken: token);
                var artist = artistRows.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Album {albumId} was not found.");

                var ordered = await GetOrderedSongIdsAsync(albumId, token);
                var target = AdminDiscographyValidation.ClampPosition(position, ordered.Count);

                const string insertSql = """
                    INSERT INTO dbo.Q_ALBUM_SONG_T
                        (SONG_TITLE, SONG_LYRICS, Q_ALBUM_ID, SONG_NOTES, Q_ARTIST_ID, IS_SINGLE, CREATE_DATE, TRACK_NUMBER)
                    VALUES
                        (@Title, @Lyrics, @AlbumId, @Notes, @ArtistId, @IsSingle, GETDATE(), @TrackNumber);
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;

                var songId = await EfSql.ExecuteScalarSqlAsync(
                    dbContext,
                    insertSql,
                    command =>
                    {
                        AddSongParameters(command, input);
                        command.Parameters.Add(EfSql.Input("@AlbumId", albumId));
                        command.Parameters.Add(EfSql.Input("@ArtistId", artist.Value));
                        command.Parameters.Add(EfSql.Input("@TrackNumber", target));
                    },
                    cancellationToken: token);

                await RenumberAsync(albumId, AdminDiscographyValidation.MoveTo(ordered, songId, target), token);
                return songId;
            },
            cancellationToken);

    public async Task UpdateSongAsync(int songId, AdminSongInput input, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Q_ALBUM_SONG_T
            SET SONG_TITLE = @Title,
                SONG_LYRICS = @Lyrics,
                SONG_NOTES = @Notes,
                IS_SINGLE = @IsSingle
            WHERE Q_ALBUM_SONG_ID = @SongId
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                AddSongParameters(command, input);
                command.Parameters.Add(EfSql.Input("@SongId", songId));
            },
            cancellationToken: cancellationToken);
        EnsureSongFound(songId, rows);
    }

    public Task MoveSongAsync(int songId, int position, CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            async token =>
            {
                var albumId = await GetAlbumIdForSongAsync(songId, token);
                var ordered = await GetOrderedSongIdsAsync(albumId, token);
                await RenumberAsync(albumId, AdminDiscographyValidation.MoveTo(ordered, songId, position), token);
            },
            cancellationToken);

    public async Task SetSongCoverAsync(int songId, string? coverFileName, CancellationToken cancellationToken = default)
    {
        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            "UPDATE dbo.Q_ALBUM_SONG_T SET COVER_URL = @CoverUrl WHERE Q_ALBUM_SONG_ID = @SongId",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@CoverUrl", AdminDiscographyValidation.NullIfBlank(coverFileName)));
                command.Parameters.Add(EfSql.Input("@SongId", songId));
            },
            cancellationToken: cancellationToken);
        EnsureSongFound(songId, rows);
    }

    public Task DeleteSongAsync(int songId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(
            async token =>
            {
                var albumId = await GetAlbumIdForSongAsync(songId, token);
                await EfSql.ExecuteNonQuerySqlAsync(
                    dbContext,
                    "DELETE FROM dbo.Q_ALBUM_SONG_T WHERE Q_ALBUM_SONG_ID = @SongId",
                    command => command.Parameters.Add(EfSql.Input("@SongId", songId)),
                    cancellationToken: token);
                await RenumberAsync(albumId, await GetOrderedSongIdsAsync(albumId, token), token);
            },
            cancellationToken);

    private async Task<IReadOnlyList<AdminAlbumSong>> GetSongsForAlbumAsync(int albumId, CancellationToken cancellationToken)
    {
        var sql = SongSelect + """

            WHERE s.Q_ALBUM_ID = @AlbumId
            ORDER BY TrackNumber
            """;

        var rows = await EfSql.QuerySqlAsync<SongRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@AlbumId", albumId)),
            cancellationToken: cancellationToken);
        return rows.Select(MapSong).ToList();
    }

    private async Task<int> GetAlbumIdForSongAsync(int songId, CancellationToken cancellationToken)
    {
        var rows = await EfSql.QuerySqlAsync<ValueRow>(
            dbContext,
            "SELECT CAST(Q_ALBUM_ID AS int) AS Value FROM dbo.Q_ALBUM_SONG_T WITH (UPDLOCK) WHERE Q_ALBUM_SONG_ID = @SongId",
            command => command.Parameters.Add(EfSql.Input("@SongId", songId)),
            cancellationToken: cancellationToken);
        return rows.FirstOrDefault()?.Value
            ?? throw new InvalidOperationException($"Song {songId} was not found.");
    }

    private async Task<IReadOnlyList<int>> GetOrderedSongIdsAsync(int albumId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CAST(Q_ALBUM_SONG_ID AS int) AS Value
            FROM dbo.Q_ALBUM_SONG_T WITH (UPDLOCK, HOLDLOCK)
            WHERE Q_ALBUM_ID = @AlbumId
            ORDER BY ISNULL(TRACK_NUMBER, 32767), Q_ALBUM_SONG_ID
            """;

        var rows = await EfSql.QuerySqlAsync<ValueRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@AlbumId", albumId)),
            cancellationToken: cancellationToken);
        return rows.Select(row => row.Value).ToList();
    }

    /// <summary>
    /// Writes TRACK_NUMBER = 1..n for <paramref name="orderedSongIds"/> in one statement.
    /// Only parameter placeholders are concatenated into the SQL text.
    /// </summary>
    private async Task RenumberAsync(int albumId, IReadOnlyList<int> orderedSongIds, CancellationToken cancellationToken)
    {
        if (orderedSongIds.Count == 0)
        {
            return;
        }

        var values = new StringBuilder();
        for (var index = 0; index < orderedSongIds.Count; index++)
        {
            values.Append(index == 0 ? string.Empty : ", ").Append($"(@Id{index}, @Track{index})");
        }

        var sql = $"""
            UPDATE s
            SET TRACK_NUMBER = v.TrackNumber
            FROM dbo.Q_ALBUM_SONG_T s
            INNER JOIN (VALUES {values}) AS v (SongId, TrackNumber) ON s.Q_ALBUM_SONG_ID = v.SongId
            WHERE s.Q_ALBUM_ID = @AlbumId
            """;

        await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@AlbumId", albumId));
                for (var index = 0; index < orderedSongIds.Count; index++)
                {
                    command.Parameters.Add(EfSql.Input($"@Id{index}", orderedSongIds[index]));
                    command.Parameters.Add(EfSql.Input($"@Track{index}", index + 1));
                }
            },
            cancellationToken: cancellationToken);
    }

    private async Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        await InTransactionAsync(
            async token =>
            {
                await work(token);
                return true;
            },
            cancellationToken);

    private Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            async token =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
                var result = await work(token);
                await transaction.CommitAsync(token);
                return result;
            },
            cancellationToken);
    }

    private static void AddAlbumParameters(Microsoft.Data.SqlClient.SqlCommand command, AdminAlbumInput input)
    {
        command.Parameters.Add(EfSql.Input("@Name", input.Name.Trim()));
        command.Parameters.Add(EfSql.Input("@ArtistId", input.ArtistId));
        command.Parameters.Add(EfSql.Input("@ReleaseDate", input.ReleaseDate?.Date));
        command.Parameters.Add(EfSql.Input("@GeneralNotes", AdminDiscographyValidation.NullIfBlank(input.GeneralNotes)));
        command.Parameters.Add(EfSql.Input("@Active", input.IsActive ? 1 : 0));
    }

    private static void AddSongParameters(Microsoft.Data.SqlClient.SqlCommand command, AdminSongInput input)
    {
        command.Parameters.Add(EfSql.Input("@Title", input.Title.Trim()));

        // SONG_LYRICS is NOT NULL in the legacy schema; blank lyrics are stored as ''.
        command.Parameters.Add(EfSql.Input("@Lyrics", input.Lyrics?.Trim() ?? string.Empty));
        command.Parameters.Add(EfSql.Input("@Notes", AdminDiscographyValidation.NullIfBlank(input.Notes)));
        command.Parameters.Add(EfSql.Input("@IsSingle", input.IsSingle ? 1 : 0));
    }

    private static void EnsureAlbumFound(int albumId, int rows)
    {
        if (rows < 1)
        {
            throw new InvalidOperationException($"Album {albumId} was not found.");
        }
    }

    private static void EnsureSongFound(int songId, int rows)
    {
        if (rows < 1)
        {
            throw new InvalidOperationException($"Song {songId} was not found.");
        }
    }

    private static AdminAlbumSong MapSong(SongRow row) =>
        new(
            row.SongId,
            row.AlbumId,
            row.TrackNumber,
            row.Title,
            AdminDiscographyValidation.NullIfBlank(row.Lyrics),
            AdminDiscographyValidation.NullIfBlank(row.Notes),
            row.IsSingle,
            AdminDiscographyValidation.NullIfBlank(row.CoverFileName));

    private sealed class AlbumListRow
    {
        public int AlbumId { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime? ReleaseDate { get; set; }

        public bool IsActive { get; set; }

        public string? ThumbFileName { get; set; }

        public int SongCount { get; set; }
    }

    private sealed class AlbumRow
    {
        public int AlbumId { get; set; }

        public string Name { get; set; } = string.Empty;

        public int ArtistId { get; set; }

        public DateTime? ReleaseDate { get; set; }

        public string? GeneralNotes { get; set; }

        public bool IsActive { get; set; }

        public string? ThumbFileName { get; set; }

        public string? PictureFileName { get; set; }
    }

    private sealed class SongRow
    {
        public int SongId { get; set; }

        public int AlbumId { get; set; }

        public int TrackNumber { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Lyrics { get; set; }

        public string? Notes { get; set; }

        public bool IsSingle { get; set; }

        public string? CoverFileName { get; set; }
    }

    private sealed class ArtistRow
    {
        public int ArtistId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ValueRow
    {
        public int Value { get; set; }
    }
}
