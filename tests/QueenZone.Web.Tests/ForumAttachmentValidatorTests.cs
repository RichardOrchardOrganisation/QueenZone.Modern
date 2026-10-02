using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumAttachmentValidatorTests
{
    private readonly ForumAttachmentValidator validator = new(
        Options.Create(new ForumAttachmentOptions
        {
            MaxFilesPerPost = 5,
            MaxBytesPerFile = 20 * 1024 * 1024,
            MaxTotalBytesPerPost = 50 * 1024 * 1024,
        }),
        Options.Create(new BlobUploadOptions()));

    [Fact]
    public void Validate_RejectsMoreThanMaxFiles()
    {
        var files = Enumerable.Range(0, 6)
            .Select(i => CreateFile($"f{i}.pdf", 100, "application/pdf"))
            .ToList();

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("at most 5", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.AcceptedFiles);
    }

    [Fact]
    public void Validate_RejectsOversizedIndividualFile()
    {
        var files = new List<IFormFile>
        {
            CreateFile("big.pdf", (20 * 1024 * 1024) + 1, "application/pdf"),
        };

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("too large", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsWhenTotalExceedsLimit()
    {
        var files = new List<IFormFile>
        {
            CreateFile("a.pdf", 20 * 1024 * 1024, "application/pdf"),
            CreateFile("b.pdf", 20 * 1024 * 1024, "application/pdf"),
            CreateFile("c.pdf", 15 * 1024 * 1024, "application/pdf"),
        };

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("exceeds", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, result.AcceptedFiles.Count);
    }

    [Fact]
    public void Validate_RejectsDisallowedMimeTypes()
    {
        var files = new List<IFormFile>
        {
            CreateFile("payload.exe", 1024, "application/x-msdownload"),
        };

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsAllowedPdf()
    {
        var files = new List<IFormFile>
        {
            CreateFile("notes.pdf", 2048, "application/pdf"),
        };

        var result = validator.Validate(files);

        Assert.True(result.IsValid);
        Assert.Single(result.AcceptedFiles);
    }

    [Fact]
    public void Validate_RejectsPdfWhoseBytesAreHtml()
    {
        var files = new List<IFormFile>
        {
            CreateFile("notes.pdf", 32, "application/pdf", "<html>not a pdf</html>"u8.ToArray()),
        };

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Empty(result.AcceptedFiles);
        Assert.Contains(result.Errors, error => error.Contains("does not match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsTextWithoutATextSignature()
    {
        var files = new List<IFormFile>
        {
            CreateFile("notes.txt", 4, "text/plain", [0x68, 0x69, 0x00, 0x21]),
        };

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not recognized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsDisallowedTypeEvenWhenClientContentTypeIsAllowed()
    {
        var files = new List<IFormFile>
        {
            CreateFile("payload.exe", 16, "application/pdf", "%PDF-1.4"u8.ToArray()),
        };

        var result = validator.Validate(files);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    public void Validate_Tiff_RemainsDisallowed(string extension)
    {
        Assert.Equal("image/tiff", ForumAttachmentValidator.GuessContentType("scan" + extension));
        var result = validator.Validate([CreateFile("scan" + extension, 4, "image/tiff", [0x49, 0x49, 0x2A, 0x00])]);
        Assert.False(result.IsValid);
        Assert.Empty(result.AcceptedFiles);
        Assert.Contains(result.Errors, error => error.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GuessContentType_UnknownExtension_KeepsDisplayFallback() =>
        Assert.Equal("application/octet-stream", ForumAttachmentValidator.GuessContentType("payload.unknown"));

    [Fact]
    public void AllowedContentTypes_HaveSharedExtensionMappings()
    {
        string[] extensions = [".jpg", ".png", ".gif", ".webp", ".pdf", ".txt", ".zip", ".mp3", ".flac", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"];
        var mapped = extensions.Select(extension => BlobContentSniffer.GuessContentTypeFromExtension(extension)).ToArray();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepoPaths.Combine("src", "QueenZone.Web", "appsettings.json"))
            .Build();
        var configuredForum = configuration.GetSection(ForumAttachmentOptions.SectionName).Get<ForumAttachmentOptions>()!;
        var configuredStorage = configuration.GetSection(BlobUploadOptions.SectionName).Get<BlobUploadOptions>()!;
        var allowed = configuredForum.AllowedContentTypes
            .Concat(new ForumAttachmentOptions().AllowedContentTypes)
            .Concat(configuredStorage.Containers[BlobUploadContainers.Forum].AllowedContentTypes!)
            .Concat(new BlobUploadOptions().Containers[BlobUploadContainers.Forum].AllowedContentTypes!);
        foreach (var type in allowed.Distinct())
        {
            // Configuration also permits legacy MIME aliases; the table returns canonical types.
            var canonical = type == "application/x-zip-compressed" ? "application/zip" : type;
            Assert.Contains(mapped, candidate => candidate is not null && BlobUploadValidator.ContentTypesAgree(candidate, canonical));
        }
        foreach (var extension in extensions)
        {
            Assert.Equal(BlobContentSniffer.GuessContentTypeFromExtension(extension), ForumAttachmentValidator.GuessContentType("file" + extension));
        }
    }

    private static IFormFile CreateFile(string name, long length, string contentType, byte[]? content = null)
    {
        var bytes = content ?? new byte[Math.Max(length, 0)];
        if (content is null
            && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            && bytes.Length >= 5)
        {
            "%PDF-"u8.CopyTo(bytes);
        }

        var fileLength = content is null ? length : bytes.Length;
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, fileLength, "Attachments", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
