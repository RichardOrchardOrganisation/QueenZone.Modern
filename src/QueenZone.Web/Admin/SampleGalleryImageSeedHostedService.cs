using QueenZone.Data;
using QueenZone.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web;

/// <summary>
/// Writes deterministic JPEG stand-ins for sample PIC originals into the in-memory
/// gallery blob store so local/Testing admin crop can read the same bytes save uses.
/// Registered only with in-memory data. Never runs against Azure Blob.
/// </summary>
public sealed class SampleGalleryImageSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SampleGalleryImageSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var galleryPhotoBlobService = scope.ServiceProvider.GetRequiredService<IGalleryPhotoBlobService>();
            foreach (var category in SamplePhotoData.CreateSeedCategories())
            {
                foreach (var item in category.Items)
                {
                    await SeedImageAsync(galleryPhotoBlobService, item, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sample gallery image seeding failed; gallery crop will 404 until blobs exist.");
        }
    }

    private static async Task SeedImageAsync(IGalleryPhotoBlobService galleryPhotoBlobService, PhotoItemSeed item, CancellationToken cancellationToken)
    {
        var blobUrl = PhotoImageUrl.ToBlobStorageUrl(item.Url);
        if (!PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
        {
            return;
        }

        await using var existing = await galleryPhotoBlobService.OpenReadAsync(
            container,
            blobName,
            cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var width = item.PictureWidth >= NewsArticleImageProcessor.MinCropWidth
            ? item.PictureWidth
            : 600;
        var height = item.PictureHeight >= NewsArticleImageProcessor.MinCropHeight
            ? item.PictureHeight
            : 400;
        await using var jpeg = await CreateJpegAsync(width, height);
        await galleryPhotoBlobService.UploadAsync(
            container,
            blobName,
            jpeg,
            "image/jpeg",
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<MemoryStream> CreateJpegAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var left = x < width / 2;
                    var top = y < height / 2;
                    row[x] = (left, top) switch
                    {
                        (true, true) => new Rgba32(200, 40, 40),
                        (false, true) => new Rgba32(40, 80, 200),
                        (true, false) => new Rgba32(40, 180, 80),
                        _ => new Rgba32(220, 180, 40),
                    };
                }
            }
        });

        var stream = new MemoryStream();
        await image.SaveAsync(stream, new JpegEncoder());
        stream.Position = 0;
        return stream;
    }
}
