using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class MemberAvatarImageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ProducesSquareWebpFullAndThumb()
    {
        await using var source = await CreatePngAsync(100, 40);
        var processed = await MemberAvatarImageProcessor.ProcessAsync(source, "shot.png");

        await using (processed.FullImage)
        await using (processed.Thumbnail)
        {
            Assert.True(processed.FullImage.Length > 0);
            Assert.True(processed.Thumbnail.Length > 0);

            processed.FullImage.Position = 0;
            using var full = await Image.LoadAsync(processed.FullImage);
            Assert.Equal(MemberAvatarPaths.FullSizePixels, full.Width);
            Assert.Equal(MemberAvatarPaths.FullSizePixels, full.Height);

            processed.Thumbnail.Position = 0;
            using var thumb = await Image.LoadAsync(processed.Thumbnail);
            Assert.Equal(MemberAvatarPaths.ThumbSizePixels, thumb.Width);
            Assert.Equal(MemberAvatarPaths.ThumbSizePixels, thumb.Height);
        }
    }

    [Fact]
    public async Task ProcessAsync_RejectsOversizedPayload()
    {
        await using var oversized = new MemoryStream(new byte[MemberAvatarPaths.MaxUploadBytes + 10]);
        var ex = await Assert.ThrowsAsync<MemberAvatarRejectedException>(() =>
            MemberAvatarImageProcessor.ProcessAsync(oversized, "big.png"));
        Assert.Contains("MB", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_RejectsNonImageBytes()
    {
        await using var junk = new MemoryStream("hello"u8.ToArray());
        var ex = await Assert.ThrowsAsync<MemberAvatarRejectedException>(() =>
            MemberAvatarImageProcessor.ProcessAsync(junk, "hello.png"));
        Assert.Contains("JPEG", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 30));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
