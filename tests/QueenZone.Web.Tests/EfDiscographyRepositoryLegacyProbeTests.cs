using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfDiscographyRepository"/> against the real legacy
/// <c>Q_ALBUM_LIST_SP</c> / <c>Q_ALBUM_T_DISPLAY_SP</c> / <c>Q_ALBUM_SONG_T_LIST_SP</c>
/// procedures (#1672 / #1886). Skips when <c>ConnectionStrings__QueenZoneLegacy</c> is not
/// set; the nightly <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>DiscographyRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfDiscographyRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_discography_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfDiscographyRepository(dbContext);

        // tinyint Q_ALBUM_ID / ACTIVE and smallint Q_ALBUM_SONG_ID must materialize into the row models.
        var albums = await repository.GetAlbumsAsync();
        Assert.NotEmpty(albums);

        var album = albums[0];
        var detail = await repository.GetAlbumByIdAsync(album.AlbumId);
        Assert.NotNull(detail);
        Assert.Equal(album.Name, detail.Name);
        Assert.False(string.IsNullOrWhiteSpace(detail.ArtistName));
        Assert.NotEmpty(detail.Songs);
    }
}
