using QueenZone.Data;

namespace QueenZone.Web.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ForumPostAttachmentTests
{
    [Fact]
    public void Url_UsesMemberGatedLegacyDownloadPath()
    {
        var attachment = new ForumPostAttachment(
            "setlist-scan.jpg",
            100_000,
            ForumAttachmentPaths.LegacyDownloadPath(42));
        Assert.Equal("/forum/attachment/legacy/42", attachment.Url);
    }

    [Theory]
    [InlineData("photo.JPG", "JPG")]
    [InlineData("document.pdf", "PDF")]
    [InlineData("archive.zip", "ZIP")]
    [InlineData("noextension", "")]
    public void Extension_ReturnsUppercaseExtensionWithoutDot(string fileName, string expected)
    {
        var attachment = new ForumPostAttachment(fileName, null, "/forum/attachment/legacy/1");
        Assert.Equal(expected, attachment.Extension);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(0L, "")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(2_560L, "2.5 KB")]
    [InlineData(1_048_576L, "1.0 MB")]
    [InlineData(2_621_440L, "2.5 MB")]
    public void FormattedSize_FormatsCorrectly(long? bytes, string expected)
    {
        var attachment = new ForumPostAttachment("file.jpg", bytes, "/x");
        Assert.Equal(expected, attachment.FormattedSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ReturnsNullWhenNoAttachment(string? attachment)
    {
        var result = ForumPostAttachment.Parse(attachment, "1024", 9);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ReturnsSingleAttachmentWithParsedSizeAndGatedUrl()
    {
        var result = ForumPostAttachment.Parse("setlist.jpg", "284712", 1002);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("setlist.jpg", result[0].FileName);
        Assert.Equal(284_712L, result[0].FileSizeBytes);
        Assert.Equal("/forum/attachment/legacy/1002", result[0].Url);
    }

    [Fact]
    public void Parse_HandlesNullFilesizeAsNullBytes()
    {
        var result = ForumPostAttachment.Parse("setlist.jpg", null, 7);
        Assert.NotNull(result);
        Assert.Null(result[0].FileSizeBytes);
    }

    [Fact]
    public void Parse_TrimsWhitespaceFromFilename()
    {
        var result = ForumPostAttachment.Parse("  setlist.jpg  ", "0", 1);
        Assert.NotNull(result);
        Assert.Equal("setlist.jpg", result[0].FileName);
    }

    [Fact]
    public void FromStored_BuildsDownloadPathAndImageThumb()
    {
        var stored = new StoredForumAttachment(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PostId: 10,
            LegacyPostId: 55,
            OriginalFileName: "cover.png",
            BlobPath: "members/abc/cover.webp",
            ContainerName: "ugc-forum",
            FileSizeBytes: 2048,
            MimeType: "image/png",
            UploadedAt: DateTimeOffset.Parse("2026-07-11T00:00:00Z"),
            DownloadCount: 0);

        var view = ForumPostAttachment.FromStored(stored);

        Assert.Equal("/forum/attachment/55/11111111-1111-1111-1111-111111111111", view.Url);
        Assert.True(view.IsImage);
        Assert.Equal("/ugc/forum/members/abc/cover.webp?size=thumb", view.ThumbnailUrl);
    }

    [Fact]
    public void LegacyContainerName_IsPrivateAttachmentsContainer()
    {
        Assert.Equal("attachments", ForumAttachmentPaths.LegacyContainerName);
        Assert.Equal("/forum/attachment/legacy/42", ForumAttachmentPaths.LegacyDownloadPath(42));
        Assert.DoesNotContain("cdn", ForumAttachmentPaths.LegacyDownloadPath(42), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob.core.windows.net", ForumAttachmentPaths.LegacyDownloadPath(42), StringComparison.OrdinalIgnoreCase);
    }
}
