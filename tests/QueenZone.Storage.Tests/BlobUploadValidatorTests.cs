using QueenZone.Storage;

namespace QueenZone.Storage.Tests;

public sealed class BlobUploadValidatorTests
{
    private readonly BlobUploadValidator validator = new(new BlobUploadOptions());

    [Fact]
    public void Rejects_unknown_container()
    {
        var ex = Assert.Throws<BlobUploadException>(() => validator.EnsureKnownContainer("legacy-photos"));
        Assert.Contains("not a known UGC container", ex.Message);
    }

    [Fact]
    public void Accepts_canonical_containers()
    {
        foreach (var container in BlobUploadContainers.All)
        {
            validator.EnsureKnownContainer(container);
        }
    }

    [Fact]
    public void Rejects_empty_and_oversized_payloads()
    {
        Assert.Throws<BlobUploadException>(() =>
            validator.ValidateSize(0, BlobUploadContainers.Avatars));

        var max = validator.GetMaxBytes(BlobUploadContainers.Avatars);
        Assert.Throws<BlobUploadException>(() =>
            validator.ValidateSize(max + 1, BlobUploadContainers.Avatars));

        validator.ValidateSize(max, BlobUploadContainers.Avatars);
    }

    [Fact]
    public void Avatar_limit_is_stricter_than_default()
    {
        Assert.True(
            validator.GetMaxBytes(BlobUploadContainers.Avatars)
            < validator.GetMaxBytes(BlobUploadContainers.Forum));
    }

    [Fact]
    public void Rejects_disallowed_content_type_for_container()
    {
        // PDF allowed for forum, not avatars
        var pdfHeader = "%PDF-1.4"u8.ToArray();
        Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType("doc.pdf", pdfHeader, BlobUploadContainers.Avatars));

        var contentType = validator.ResolveAndValidateContentType(
            "doc.pdf",
            pdfHeader,
            BlobUploadContainers.Forum);
        Assert.Equal("application/pdf", contentType);
    }

    [Fact]
    public void Rejects_extension_content_mismatch()
    {
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var ex = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "not-a-png.png",
                jpegHeader,
                BlobUploadContainers.Photos));
        Assert.Contains("does not match extension", ex.Message);
    }

    [Fact]
    public void Accepts_matching_jpeg()
    {
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var contentType = validator.ResolveAndValidateContentType(
            "photo.jpg",
            jpegHeader,
            BlobUploadContainers.Articles);
        Assert.Equal("image/jpeg", contentType);
    }

    [Fact]
    public void Rejects_blank_container_and_unknown_content()
    {
        Assert.Throws<BlobUploadException>(() => validator.EnsureKnownContainer(" "));
        Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType("file.bin", [0x00, 0x01], BlobUploadContainers.Forum));
    }

    [Fact]
    public void Accepts_custom_container_from_options()
    {
        var custom = new BlobUploadValidator(new BlobUploadOptions
        {
            Containers =
            {
                ["custom-ugc"] = new BlobContainerPolicy
                {
                    MaxBytes = 100,
                    AllowedContentTypes = ["image/png"],
                },
            },
        });
        custom.EnsureKnownContainer("custom-ugc");
        Assert.Equal(100, custom.GetMaxBytes("custom-ugc"));
    }

    [Fact]
    public void Fan_performance_container_is_25_mb_audio_only()
    {
        Assert.Contains(BlobUploadContainers.All, name =>
            string.Equals(name, BlobUploadContainers.FanPerformances, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual("songfiles", BlobUploadContainers.FanPerformances);

        Assert.Equal(25 * 1024 * 1024, validator.GetMaxBytes(BlobUploadContainers.FanPerformances));
        var allowed = validator.GetAllowedContentTypes(BlobUploadContainers.FanPerformances);
        Assert.Equal(["audio/mpeg", "audio/mp3", "audio/flac", "audio/x-flac"], allowed);
        Assert.DoesNotContain(allowed, type => type.Contains("mp4", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(allowed, type => type.Contains("m4a", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(allowed, type => type.Contains("aac", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fan_performance_oversize_is_rejected_with_clear_message()
    {
        var max = validator.GetMaxBytes(BlobUploadContainers.FanPerformances);
        var ex = Assert.Throws<BlobUploadException>(() =>
            validator.ValidateSize(max + 1, BlobUploadContainers.FanPerformances));
        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BlobUploadContainers.FanPerformances, ex.Message);
        Assert.Contains(max.ToString(), ex.Message);
        validator.ValidateSize(max, BlobUploadContainers.FanPerformances);
    }

    [Fact]
    public void Accepts_sniffed_mpeg_and_flac_for_fan_performances()
    {
        Assert.Equal(
            "audio/mpeg",
            validator.ResolveAndValidateContentType(
                "cover.mp3",
                [0xFF, 0xFB, 0x90, 0x00],
                BlobUploadContainers.FanPerformances));
        Assert.Equal(
            "audio/mpeg",
            validator.ResolveAndValidateContentType(
                "tagged.mp3",
                "ID3"u8.ToArray(),
                BlobUploadContainers.FanPerformances));
        Assert.Equal(
            "audio/flac",
            validator.ResolveAndValidateContentType(
                "cover.flac",
                "fLaC"u8.ToArray(),
                BlobUploadContainers.FanPerformances));
        Assert.Equal(
            "audio/mpeg",
            validator.ResolveAndValidateContentType(
                "clip.mp3",
                [0xFF, 0xFB, 0x90, 0x00],
                BlobUploadContainers.Forum));
    }

    [Fact]
    public void Rejects_audio_extension_with_non_audio_bytes()
    {
        var fake = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "fake.mp3",
                "not-an-mp3"u8.ToArray(),
                BlobUploadContainers.FanPerformances));
        Assert.Contains("not recognized as audio", fake.Message, StringComparison.OrdinalIgnoreCase);

        var jpegAsMp3 = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "photo.mp3",
                [0xFF, 0xD8, 0xFF, 0xE0],
                BlobUploadContainers.FanPerformances));
        Assert.Contains("does not match extension", jpegAsMp3.Message);

        var m4a = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "clip.m4a",
                [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70],
                BlobUploadContainers.FanPerformances));
        Assert.Contains("Unable to determine content type", m4a.Message);
    }

    [Fact]
    public void Rejects_pdf_and_text_without_a_matching_signature()
    {
        var htmlPdf = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "notes.pdf",
                "<html>not a pdf</html>"u8.ToArray(),
                BlobUploadContainers.Forum));
        Assert.Contains("does not match extension", htmlPdf.Message);

        var binaryPdf = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "notes.pdf",
                [0x00, 0x01, 0x02, 0x03],
                BlobUploadContainers.Forum));
        Assert.Contains("not recognized", binaryPdf.Message, StringComparison.OrdinalIgnoreCase);

        var binaryText = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "notes.txt",
                [0x68, 0x69, 0x00, 0x21],
                BlobUploadContainers.Forum));
        Assert.Contains("not recognized", binaryText.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            "text/plain",
            validator.ResolveAndValidateContentType(
                "notes.txt",
                "hello forum"u8.ToArray(),
                BlobUploadContainers.Forum));
    }

    [Fact]
    public void Accepts_zip_and_office_packages_only_when_the_signature_matches()
    {
        Assert.Equal(
            "application/zip",
            validator.ResolveAndValidateContentType(
                "bundle.zip",
                [0x50, 0x4B, 0x03, 0x04],
                BlobUploadContainers.Forum));

        var fakeZip = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "bundle.zip",
                "<html></html>"u8.ToArray(),
                BlobUploadContainers.Forum));
        Assert.Contains("does not match extension", fakeZip.Message);

        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            validator.ResolveAndValidateContentType(
                "notes.docx",
                [0x50, 0x4B, 0x03, 0x04],
                BlobUploadContainers.Forum));
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            validator.ResolveAndValidateContentType(
                "sheet.xlsx",
                [0x50, 0x4B, 0x03, 0x04],
                BlobUploadContainers.Forum));

        var ole = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        Assert.Equal(
            "application/msword",
            validator.ResolveAndValidateContentType("legacy.doc", ole, BlobUploadContainers.Forum));
        Assert.Equal(
            "application/vnd.ms-excel",
            validator.ResolveAndValidateContentType("legacy.xls", ole, BlobUploadContainers.Forum));

        var fakeDoc = Assert.Throws<BlobUploadException>(() =>
            validator.ResolveAndValidateContentType(
                "legacy.doc",
                "<html>not a document</html>"u8.ToArray(),
                BlobUploadContainers.Forum));
        Assert.Contains("does not match extension", fakeDoc.Message);
    }

    [Fact]
    public void Audio_mpeg_and_mp3_aliases_agree()
    {
        Assert.True(BlobUploadValidator.ContentTypesAgree("audio/mpeg", "audio/mp3"));
        Assert.True(BlobUploadValidator.ContentTypesAgree("audio/flac", "audio/x-flac"));
        Assert.False(BlobUploadValidator.ContentTypesAgree("audio/mpeg", "audio/flac"));
        Assert.False(BlobUploadValidator.ContentTypesAgree("audio/mpeg", "audio/mp4"));
    }
}
