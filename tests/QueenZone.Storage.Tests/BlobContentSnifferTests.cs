using QueenZone.Storage;

namespace QueenZone.Storage.Tests;

public sealed class BlobContentSnifferTests
{
    [Fact]
    public void Detects_jpeg_png_gif_webp_and_pdf()
    {
        Assert.Equal("image/jpeg", BlobContentSniffer.TryDetectContentType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal(
            "image/png",
            BlobContentSniffer.TryDetectContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("image/gif", BlobContentSniffer.TryDetectContentType("GIF89a"u8.ToArray()));
        Assert.Equal(
            "image/webp",
            BlobContentSniffer.TryDetectContentType(
            [
                0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50
            ]));
        Assert.Equal("application/pdf", BlobContentSniffer.TryDetectContentType("%PDF"u8.ToArray()));
        Assert.Null(BlobContentSniffer.TryDetectContentType([0x00, 0x01, 0x02]));
    }

    [Fact]
    public void Detects_mpeg_from_id3_and_frame_sync()
    {
        Assert.Equal("audio/mpeg", BlobContentSniffer.TryDetectContentType("ID3"u8.ToArray()));
        Assert.Equal("audio/mpeg", BlobContentSniffer.TryDetectContentType([0xFF, 0xFB, 0x90, 0x00]));
        Assert.Equal("audio/mpeg", BlobContentSniffer.TryDetectContentType([0x00, 0xFF, 0xFB, 0x90]));
    }

    [Fact]
    public void Detects_flac_from_stream_marker()
    {
        Assert.Equal("audio/flac", BlobContentSniffer.TryDetectContentType("fLaC"u8.ToArray()));
    }

    [Fact]
    public void Rejects_non_audio_bytes_and_does_not_treat_jpeg_as_mpeg()
    {
        Assert.Equal("image/jpeg", BlobContentSniffer.TryDetectContentType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("text/plain", BlobContentSniffer.TryDetectContentType("not-an-mp3"u8.ToArray()));
        Assert.Equal("text/plain", BlobContentSniffer.TryDetectContentType("ftypM4A "u8.ToArray()));
        Assert.Null(BlobContentSniffer.TryDetectContentType([0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70]));
    }

    [Fact]
    public void Detects_zip_ole_and_plain_text_signatures()
    {
        Assert.Equal(
            "application/zip",
            BlobContentSniffer.TryDetectContentType([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00]));
        Assert.Equal(
            BlobContentSniffer.OleCompoundContentType,
            BlobContentSniffer.TryDetectContentType(
                [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00]));
        Assert.Equal("text/plain", BlobContentSniffer.TryDetectContentType("hello"u8.ToArray()));
        Assert.Equal("text/plain", BlobContentSniffer.TryDetectContentType([0x80, 0x41]));
        Assert.Null(BlobContentSniffer.TryDetectContentType([0x68, 0x69, 0x00]));
        Assert.Null(BlobContentSniffer.TryDetectContentType([0x81]));
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".JPEG", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".FLAC", "audio/flac")]
    [InlineData(".m4a", null)]
    [InlineData(".mp4", null)]
    [InlineData(".aac", null)]
    [InlineData(".exe", null)]
    public void GuessContentTypeFromExtension(string extension, string? expected) =>
        Assert.Equal(expected, BlobContentSniffer.GuessContentTypeFromExtension(extension));
}
