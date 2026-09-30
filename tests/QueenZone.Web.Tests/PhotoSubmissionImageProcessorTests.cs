using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class PhotoSubmissionImageProcessorTests
{
    [Fact]
    public void AllowedContentTypes_is_a_fixed_image_allowlist()
    {
        IReadOnlySet<string> allowed = PhotoSubmissionImageProcessor.AllowedContentTypes;
        Assert.Equal(
            ["image/jpeg", "image/png", "image/tiff", "image/webp"],
            allowed.OrderBy(type => type, StringComparer.Ordinal));
        Assert.Contains("IMAGE/JPEG", allowed);
    }

    [Fact]
    public async Task ProcessAsync_rejects_oversized_payload_before_blob_upload()
    {
        var bytes = new byte[PhotoSubmissionImageProcessor.MaxUploadBytes + 1];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        await using var source = new MemoryStream(bytes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PhotoSubmissionImageProcessor.ProcessAsync(source, "huge.jpg"));

        Assert.Contains("20", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_rejects_non_image_bytes()
    {
        await using var source = new MemoryStream("not-an-image"u8.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PhotoSubmissionImageProcessor.ProcessAsync(source, "note.txt"));

        Assert.Contains("JPEG", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_rejects_empty_stream_and_extension_mismatch()
    {
        await using var empty = new MemoryStream();
        var emptyEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PhotoSubmissionImageProcessor.ProcessAsync(empty, "empty.png"));
        Assert.Contains("required", emptyEx.Message);

        await using var png = await CreatePngAsync(40, 40);
        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PhotoSubmissionImageProcessor.ProcessAsync(png, "photo.jpg"));
        Assert.Contains("extension", mismatch.Message);

        png.Position = 0;
        var nonImageExt = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PhotoSubmissionImageProcessor.ProcessAsync(png, "photo.pdf"));
        Assert.Contains("extension", nonImageExt.Message);
    }

    [Fact]
    public async Task ProcessAsync_produces_web_and_square_thumb()
    {
        await using var source = await CreatePngAsync(2400, 1200);

        var result = await PhotoSubmissionImageProcessor.ProcessAsync(source, "stage.png");

        await using (result.Original)
        await using (result.WebOptimized)
        await using (result.Thumbnail)
        {
            using var web = await Image.LoadAsync(result.WebOptimized);
            using var thumb = await Image.LoadAsync(result.Thumbnail);

            Assert.Equal(2400, result.WidthPx);
            Assert.Equal(1200, result.HeightPx);
            Assert.True(web.Width <= PhotoSubmissionImageProcessor.WebMaxLongestSide);
            Assert.True(web.Height <= PhotoSubmissionImageProcessor.WebMaxLongestSide);
            Assert.Equal(PhotoSubmissionImageProcessor.ThumbSizePixels, thumb.Width);
            Assert.Equal(PhotoSubmissionImageProcessor.ThumbSizePixels, thumb.Height);
            Assert.True(result.OriginalSizeBytes > 0);
        }
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 40, 200));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
