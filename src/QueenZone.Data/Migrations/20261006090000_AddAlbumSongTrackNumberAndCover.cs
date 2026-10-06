using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Adds an explicit track order and an optional single-cover filename to the legacy
/// <c>Q_ALBUM_SONG_T</c> table so admins can insert tracks mid-album and attach
/// single artwork. Existing rows are numbered in their current
/// <c>Q_ALBUM_SONG_ID</c> order, which is what the public tracklist showed before.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261006090000_AddAlbumSongTrackNumberAndCover")]
public partial class AddAlbumSongTrackNumberAndCover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.Q_ALBUM_SONG_T', 'TRACK_NUMBER') IS NULL
                ALTER TABLE dbo.Q_ALBUM_SONG_T ADD TRACK_NUMBER smallint NULL;
            """);
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.Q_ALBUM_SONG_T', 'COVER_URL') IS NULL
                ALTER TABLE dbo.Q_ALBUM_SONG_T ADD COVER_URL varchar(100) NULL;
            """);
        migrationBuilder.Sql(
            """
            WITH ordered AS
            (
                SELECT TRACK_NUMBER,
                       ROW_NUMBER() OVER (PARTITION BY Q_ALBUM_ID ORDER BY Q_ALBUM_SONG_ID) AS rn
                FROM dbo.Q_ALBUM_SONG_T
            )
            UPDATE ordered SET TRACK_NUMBER = rn WHERE TRACK_NUMBER IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE dbo.Q_ALBUM_SONG_T DROP COLUMN COVER_URL;");
        migrationBuilder.Sql("ALTER TABLE dbo.Q_ALBUM_SONG_T DROP COLUMN TRACK_NUMBER;");
    }
}
