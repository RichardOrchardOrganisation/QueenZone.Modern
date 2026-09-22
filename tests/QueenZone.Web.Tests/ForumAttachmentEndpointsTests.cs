using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class ForumAttachmentEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumAttachmentEndpointsTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task LegacyDownload_RedirectsAnonymousVisitorsToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/forum/attachment/legacy/1002");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LegacyDownload_StreamsSignedInMembersWithAttachmentDisposition()
    {
        var testFactory = WithLegacyBlobs(factory);
        var client = CreateMemberClient(testFactory);

        var response = await client.GetAsync("/forum/attachment/legacy/1002");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("scan-bytes", body);
        Assert.Null(response.Headers.Location);
        var disposition = response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
        Assert.Contains("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anoto-setlist-scan.jpg", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn2.queenzone.org", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Contains("nosniff", nosniff!);
    }

    [Fact]
    public async Task LegacyAttachmentSeed_SkipsWhenSampleBlobAlreadyExists()
    {
        _ = factory.CreateClient();
        var seed = factory.Services.GetServices<IHostedService>()
            .OfType<SampleLegacyForumAttachmentSeedHostedService>()
            .Single();

        await seed.StartAsync(CancellationToken.None);

        var client = CreateMemberClient(factory);
        var response = await client.GetAsync("/forum/attachment/legacy/1002");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SampleLegacyForumAttachmentSeedHostedService.ScanBytes, body);
    }

    [Fact]
    public async Task LegacyDownload_StreamsSeededSampleForSignedInMembers()
    {
        var client = CreateMemberClient(factory);

        var response = await client.GetAsync("/forum/attachment/legacy/1002");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SampleLegacyForumAttachmentSeedHostedService.ScanBytes, body);
        Assert.Null(response.Headers.Location);
        var disposition = response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
        Assert.Contains("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anoto-setlist-scan.jpg", disposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyDownload_ReturnsNotFound_WhenBlobMissing()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "not-in-storage.bin", 10));
        var testFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IForumAttachmentRepository>();
                services.AddSingleton<IForumAttachmentRepository>(repo);
            });
        });
        var client = CreateMemberClient(testFactory);

        var response = await client.GetAsync("/forum/attachment/legacy/42");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ModernDownload_IncrementsDownloadCountAndStreamsFile()
    {
        var attachmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var memoryBlob = new MemoryBlobUploadService();
        await memoryBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("hello attachment")),
            "notes.txt",
            BlobUploadContainers.Forum,
            new BlobUploadContext { PreferredBlobName = "members/test/notes.txt" });

        var stored = new StoredForumAttachment(
            attachmentId,
            PostId: 1,
            LegacyPostId: 9001,
            OriginalFileName: "notes.txt",
            BlobPath: "members/test/notes.txt",
            ContainerName: BlobUploadContainers.Forum,
            FileSizeBytes: 16,
            MimeType: "text/plain",
            UploadedAt: DateTimeOffset.UtcNow,
            DownloadCount: 0);
        var fixedRepo = new FixedIdAttachmentRepository(stored);

        var testFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IForumAttachmentRepository>();
                services.AddSingleton<IForumAttachmentRepository>(fixedRepo);
                services.RemoveAll<IBlobUploadService>();
                services.AddSingleton<IBlobUploadService>(memoryBlob);
            });
        });

        var client = CreateMemberClient(testFactory);

        var response = await client.GetAsync($"/forum/attachment/9001/{attachmentId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello attachment", body);
        Assert.Equal(1, fixedRepo.DownloadCount);
        var disposition = response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
        Assert.Contains("notes.txt", disposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TopicPage_RendersMemberGatedAttachmentLink()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");

        Assert.Contains("/forum/attachment/legacy/1002", body);
        Assert.Contains("anoto-setlist-scan.jpg", body);
        Assert.DoesNotContain("cdn.queenzone.org/attachments/", body);
        Assert.DoesNotContain("cdn2.queenzone.org/attachments/", body);
        Assert.DoesNotContain("blob.core.windows.net/attachments/", body);
        Assert.Contains("Members only", body);
    }

    private static WebApplicationFactory<Program> WithLegacyBlobs(WebApplicationFactory<Program> sourceFactory)
    {
        var memoryBlob = new MemoryBlobUploadService();
        memoryBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("scan-bytes")),
            "anoto-setlist-scan.jpg",
            ForumAttachmentPaths.LegacyContainerName,
            new BlobUploadContext { PreferredBlobName = "anoto-setlist-scan.jpg" }).GetAwaiter().GetResult();
        memoryBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-notes")),
            "opera-side-two-notes.pdf",
            ForumAttachmentPaths.LegacyContainerName,
            new BlobUploadContext { PreferredBlobName = "opera-side-two-notes.pdf" }).GetAwaiter().GetResult();

        return sourceFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBlobUploadService>();
                services.AddSingleton<IBlobUploadService>(memoryBlob);
            });
        });
    }

    private static HttpClient CreateMemberClient(WebApplicationFactory<Program> sourceFactory)
    {
        var client = sourceFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Forum Attach Member");
        return client;
    }

    private sealed class FixedIdAttachmentRepository(StoredForumAttachment attachment) : IForumAttachmentRepository
    {
        public int DownloadCount { get; private set; }

        public Task AddAttachmentsAsync(
            int legacyPostId,
            IEnumerable<NewForumAttachment> attachments,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<StoredForumAttachment>> GetByLegacyPostIdsAsync(
            IReadOnlyCollection<int> legacyPostIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredForumAttachment>>(
                legacyPostIds.Contains(attachment.LegacyPostId) ? [attachment] : []);

        public Task<StoredForumAttachment?> GetAsync(
            int legacyPostId,
            Guid attachmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                legacyPostId == attachment.LegacyPostId && attachmentId == attachment.Id
                    ? attachment
                    : null);

        public Task<LegacyForumAttachmentLookup?> GetLegacyAsync(
            int legacyPostId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LegacyForumAttachmentLookup?>(null);

        public Task IncrementDownloadCountAsync(
            Guid attachmentId,
            CancellationToken cancellationToken = default)
        {
            if (attachmentId == attachment.Id)
            {
                DownloadCount += 1;
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>In-memory blob store for attachment download/upload tests.</summary>
internal sealed class MemoryBlobUploadService : IBlobUploadService
{
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> store = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BlobUploadResult> UploadAsync(
        Stream content,
        string originalFileName,
        string containerName,
        BlobUploadContext? context = null,
        CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var blobName = context?.PreferredBlobName
            ?? $"members/{Guid.NewGuid():N}/{Path.GetFileName(originalFileName)}";
        var contentType = ForumAttachmentValidator.GuessContentType(originalFileName);
        store[$"{containerName}/{blobName}"] = (buffer.ToArray(), contentType);
        return new BlobUploadResult
        {
            Container = containerName,
            BlobName = blobName,
            ContentType = contentType,
            SizeBytes = buffer.Length,
        };
    }

    public Task DeleteAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        store.Remove($"{containerName}/{blobName}");
        return Task.CompletedTask;
    }

    public Task<BlobContent?> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        if (!store.TryGetValue($"{containerName}/{blobName}", out var entry))
        {
            return Task.FromResult<BlobContent?>(null);
        }

        return Task.FromResult<BlobContent?>(new BlobContent
        {
            Stream = new MemoryStream(entry.Bytes),
            ContentType = entry.ContentType,
            ETag = $"\"{entry.Bytes.Length:x8}\"",
            ContentLength = entry.Bytes.LongLength,
        });
    }
}
