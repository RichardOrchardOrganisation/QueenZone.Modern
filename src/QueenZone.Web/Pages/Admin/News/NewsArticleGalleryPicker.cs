using System.Globalization;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Pages.Admin.News;

/// <summary>
/// Shared helpers for the article-form gallery picker. Lists existing PIC rows
/// through <see cref="IAdminPhotoRepository"/> and opens the PIC original for a
/// read-only copy+crop. It never writes gallery blobs.
/// </summary>
public static class NewsArticleGalleryPicker
{
    public const int PageSize = 8;

    public const string Path = "/admin/news/gallery-picker";

    public const string OriginalPath = "/admin/news/gallery-original";

    public sealed record GalleryOriginalStream(Stream Stream, string ContentType, string FileName);

    public static AdminPhotoListFilter BuildFilter(int? catId, string? search) =>
        new(CatId: catId, Search: string.IsNullOrWhiteSpace(search) ? null : search.Trim());

    public static string GetFileName(AdminPhotoItem item)
    {
        var path = (item.LegacyUrl ?? string.Empty).Replace('\\', '/');
        var name = System.IO.Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? item.Title : name;
    }

    public static string BuildPath(int? catId, string? search, int page)
    {
        var parts = new List<string>();
        if (catId is int id)
        {
            parts.Add("catId=" + id.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add("q=" + Uri.EscapeDataString(search.Trim()));
        }

        if (page > 1)
        {
            parts.Add("pageNumber=" + page.ToString(CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? Path : Path + "?" + string.Join("&", parts);
    }

    public static string BuildOriginalPath(int picId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(picId);
        return OriginalPath + "/" + picId.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryResolveBlobLocation(string? legacyUrl, out string container, out string blobName)
    {
        container = string.Empty;
        blobName = string.Empty;
        if (string.IsNullOrWhiteSpace(legacyUrl))
        {
            return false;
        }

        var blobUrl = PhotoImageUrl.ToBlobStorageUrl(legacyUrl);
        return PhotoImageUrl.TryParseBlobLocation(blobUrl, out container, out blobName);
    }

    public static async Task<GalleryOriginalStream?> OpenOriginalAsync(
        IAdminPhotoRepository photos,
        IGalleryPhotoBlobService galleryBlobs,
        int picId,
        CancellationToken cancellationToken = default)
    {
        if (picId <= 0)
        {
            return null;
        }

        var photo = await photos.GetByIdAsync(picId, cancellationToken);
        if (photo is null || !TryResolveBlobLocation(photo.LegacyUrl, out var container, out var blobName))
        {
            return null;
        }

        var stream = await galleryBlobs.OpenReadAsync(container, blobName, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        var fileName = GetFileName(photo);
        var contentType = GuessOriginalContentType(fileName);
        return new GalleryOriginalStream(stream, contentType, fileName);
    }

    private static string GuessOriginalContentType(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    public static async Task<string?> ResolvePreviewUrlAsync(
        IAdminPhotoRepository photos,
        AdminNewsDraft draft,
        CancellationToken cancellationToken = default)
    {
        var picId = draft.ImageGalleryPicId ?? NewsArticleImage.TryParseGalleryPicId(draft.ImageBlobKey);
        if (picId is not int id)
        {
            return null;
        }

        var photo = await photos.GetByIdAsync(id, cancellationToken);
        return photo?.ImageUrl;
    }

    public static async Task<string?> ValidatePicAsync(
        IAdminPhotoRepository photos,
        IFormFile? uploadedFile,
        int? imageGalleryPicId,
        CancellationToken cancellationToken = default)
    {
        if (uploadedFile is { Length: > 0 } || imageGalleryPicId is not int picId)
        {
            return null;
        }

        var photo = await photos.GetByIdAsync(picId, cancellationToken);
        return photo is null ? "That gallery photo was not found." : null;
    }
}
