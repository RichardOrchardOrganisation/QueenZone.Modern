using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web;

/// <summary>
/// Admin discography writes: album/song changes, album and single cover uploads, and
/// public cache invalidation. Covers live in the public <c>images</c> container under
/// <c>discography/</c>, which <see cref="AlbumCoverUrl"/> serves via cdn.queenzone.org.
/// Only covers this service uploaded (GUID names) are ever deleted from storage;
/// legacy filenames are left in place because older rows may share them.
/// </summary>
public sealed partial class AdminDiscographyService(
    IAdminDiscographyRepository repository,
    IGalleryPhotoBlobService blobService,
    PublicQueryCacheService publicQueryCache,
    CoreSitemapService coreSitemapService,
    IOutputCacheStore outputCacheStore,
    ILogger<AdminDiscographyService> logger)
{
    public const string CoverContainer = "images";

    public const string CoverFolder = "discography";

    public async Task<int> CreateAlbumAsync(AdminAlbumInput input, string editorEmail, CancellationToken cancellationToken = default)
    {
        var albumId = await repository.CreateAlbumAsync(input, cancellationToken);
        logger.LogInformation("Discography album {AlbumId} created by {Editor}", albumId, editorEmail);
        await InvalidatePublicCachesAsync(cancellationToken);
        return albumId;
    }

    public async Task UpdateAlbumAsync(int albumId, AdminAlbumInput input, string editorEmail, CancellationToken cancellationToken = default)
    {
        await repository.UpdateAlbumAsync(albumId, input, cancellationToken);
        logger.LogInformation("Discography album {AlbumId} updated by {Editor}", albumId, editorEmail);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task DeleteAlbumAsync(int albumId, string editorEmail, CancellationToken cancellationToken = default)
    {
        var album = await repository.GetAlbumAsync(albumId, cancellationToken)
            ?? throw new InvalidOperationException($"Album {albumId} was not found.");

        await repository.DeleteAlbumAsync(albumId, cancellationToken);
        logger.LogInformation(
            "Discography album {AlbumId} and {SongCount} songs deleted by {Editor}",
            albumId,
            album.Songs.Count,
            editorEmail);
        await DeleteOwnedCoversAsync(
            [album.PictureFileName, album.ThumbFileName, .. album.Songs.Select(song => song.CoverFileName)],
            cancellationToken);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task SetAlbumCoverAsync(
        int albumId,
        IFormFile file,
        NewsArticleImageCrop? crop,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var album = await repository.GetAlbumAsync(albumId, cancellationToken)
            ?? throw new InvalidOperationException($"Album {albumId} was not found.");

        await using var upload = file.OpenReadStream();
        await using var processed = await DiscographyCoverImageProcessor.ProcessAsync(upload, file.FileName, crop, cancellationToken);
        var stem = Guid.NewGuid().ToString("N");
        var pictureFileName = stem + ".webp";
        var thumbFileName = stem + "_t.webp";
        await UploadAsync(pictureFileName, processed.Full, cancellationToken);
        await UploadAsync(thumbFileName, processed.Thumbnail, cancellationToken);

        await repository.SetAlbumCoverAsync(
            albumId,
            new AdminAlbumCover(
                pictureFileName,
                processed.FullWidth,
                processed.FullHeight,
                thumbFileName,
                processed.ThumbWidth,
                processed.ThumbHeight),
            cancellationToken);
        logger.LogInformation("Discography album {AlbumId} cover replaced by {Editor}", albumId, editorEmail);
        await DeleteOwnedCoversAsync([album.PictureFileName, album.ThumbFileName], cancellationToken);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task RemoveAlbumCoverAsync(int albumId, string editorEmail, CancellationToken cancellationToken = default)
    {
        var album = await repository.GetAlbumAsync(albumId, cancellationToken)
            ?? throw new InvalidOperationException($"Album {albumId} was not found.");

        await repository.SetAlbumCoverAsync(albumId, null, cancellationToken);
        logger.LogInformation("Discography album {AlbumId} cover removed by {Editor}", albumId, editorEmail);
        await DeleteOwnedCoversAsync([album.PictureFileName, album.ThumbFileName], cancellationToken);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task<int> CreateSongAsync(
        int albumId,
        AdminSongInput input,
        int position,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var songId = await repository.CreateSongAsync(albumId, input, position, cancellationToken);
        logger.LogInformation(
            "Discography song {SongId} added to album {AlbumId} at position {Position} by {Editor}",
            songId,
            albumId,
            position,
            editorEmail);
        await InvalidatePublicCachesAsync(cancellationToken);
        return songId;
    }

    public async Task UpdateSongAsync(
        int songId,
        AdminSongInput input,
        int? position,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        await repository.UpdateSongAsync(songId, input, cancellationToken);
        if (position is int target)
        {
            await repository.MoveSongAsync(songId, target, cancellationToken);
        }

        logger.LogInformation("Discography song {SongId} updated by {Editor}", songId, editorEmail);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task MoveSongAsync(int songId, int position, string editorEmail, CancellationToken cancellationToken = default)
    {
        await repository.MoveSongAsync(songId, position, cancellationToken);
        logger.LogInformation("Discography song {SongId} moved to {Position} by {Editor}", songId, position, editorEmail);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task DeleteSongAsync(int songId, string editorEmail, CancellationToken cancellationToken = default)
    {
        var song = await repository.GetSongAsync(songId, cancellationToken)
            ?? throw new InvalidOperationException($"Song {songId} was not found.");

        await repository.DeleteSongAsync(songId, cancellationToken);
        logger.LogInformation("Discography song {SongId} deleted by {Editor}", songId, editorEmail);
        await DeleteOwnedCoversAsync([song.CoverFileName], cancellationToken);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task SetSongCoverAsync(
        int songId,
        IFormFile file,
        NewsArticleImageCrop? crop,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var song = await repository.GetSongAsync(songId, cancellationToken)
            ?? throw new InvalidOperationException($"Song {songId} was not found.");

        await using var upload = file.OpenReadStream();
        await using var processed = await DiscographyCoverImageProcessor.ProcessAsync(upload, file.FileName, crop, cancellationToken);
        var coverFileName = Guid.NewGuid().ToString("N") + ".webp";
        await UploadAsync(coverFileName, processed.Full, cancellationToken);

        await repository.SetSongCoverAsync(songId, coverFileName, cancellationToken);
        logger.LogInformation("Discography song {SongId} cover replaced by {Editor}", songId, editorEmail);
        await DeleteOwnedCoversAsync([song.CoverFileName], cancellationToken);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    public async Task RemoveSongCoverAsync(int songId, string editorEmail, CancellationToken cancellationToken = default)
    {
        var song = await repository.GetSongAsync(songId, cancellationToken)
            ?? throw new InvalidOperationException($"Song {songId} was not found.");

        await repository.SetSongCoverAsync(songId, null, cancellationToken);
        logger.LogInformation("Discography song {SongId} cover removed by {Editor}", songId, editorEmail);
        await DeleteOwnedCoversAsync([song.CoverFileName], cancellationToken);
        await InvalidatePublicCachesAsync(cancellationToken);
    }

    /// <summary>True for cover names this service generates (GUID stem, WebP).</summary>
    public static bool IsOwnedCoverFileName(string? fileName) =>
        fileName is not null && OwnedCoverFileName().IsMatch(fileName);

    public static string BlobName(string fileName) => $"{CoverFolder}/{fileName}";

    private Task UploadAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        return blobService.UploadAsync(
            CoverContainer,
            BlobName(fileName),
            content,
            PhotoWebpDerivatives.WebpContentType,
            cancellationToken);
    }

    private async Task DeleteOwnedCoversAsync(IEnumerable<string?> fileNames, CancellationToken cancellationToken)
    {
        foreach (var fileName in fileNames.Where(IsOwnedCoverFileName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await blobService.DeleteAsync(CoverContainer, BlobName(fileName!), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row change already succeeded; an orphaned cover blob is harmless.
                logger.LogWarning(ex, "Could not delete discography cover blob {FileName}", fileName);
            }
        }
    }

    private async Task InvalidatePublicCachesAsync(CancellationToken cancellationToken)
    {
        publicQueryCache.InvalidateDiscographyCache();
        await coreSitemapService.InvalidateAsync(cancellationToken);
        await outputCacheStore.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, cancellationToken);
    }

    [GeneratedRegex("^[0-9a-f]{32}(_t)?\\.webp$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnedCoverFileName();
}
