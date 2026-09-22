using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class ForumApiAttachmentDownloadTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ForumApiAttachmentDownloadTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Legacy_image_without_thumb_streams_for_signed_in_member()
    {
        using var client = CreateBearerClient(WithLegacyBlobs(factory));

        using var response = await client.GetAsync(
            $"{ForumApiEndpoints.RootPath}/attachments/legacy/1002");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("scan-bytes", body);
        Assert.Null(response.Headers.Location);
        var disposition = response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
        Assert.Contains("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anoto-setlist-scan.jpg", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn2.queenzone.org", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_non_image_streams_for_signed_in_member()
    {
        using var client = CreateBearerClient(WithLegacyBlobs(factory));

        using var response = await client.GetAsync(
            $"{ForumApiEndpoints.RootPath}/attachments/legacy/1101");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("%PDF-notes", body);
        Assert.Null(response.Headers.Location);
        var disposition = response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
        Assert.Contains("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opera-side-two-notes.pdf", disposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signed_out_caller_gets_no_bytes()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        using var response = await client.GetAsync(
            $"{ForumApiEndpoints.RootPath}/attachments/legacy/1002");
        var body = await response.Content.ReadAsStringAsync();
        var location = response.Headers.Location?.OriginalString ?? string.Empty;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn2.queenzone.org", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anoto-setlist-scan.jpg", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn2.queenzone.org", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Modern_attachment_streams_for_signed_in_member()
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

        using var client = CreateBearerClient(testFactory);

        using var response = await client.GetAsync(
            $"{ForumApiEndpoints.RootPath}/attachments/9001/{attachmentId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello attachment", body);
        Assert.Equal(1, fixedRepo.DownloadCount);
        var disposition = response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
        Assert.Contains("notes.txt", disposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Topic_posts_keep_cookie_url_and_add_downloadUrl()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts?page=1&pageSize=15");
        var page = await response.Content.ReadFromJsonAsync<ApiPagedResponse<ForumPostDto>>();
        var image = page!.Items[0].Attachments[0];
        var notes = page.Items.Single(item => item.Id == 1101).Attachments[0];

        Assert.Equal("/forum/attachment/legacy/1002", image.Url);
        Assert.Equal("/api/v1/forum/attachments/legacy/1002", image.DownloadUrl);
        Assert.Equal("/forum/attachment/legacy/1101", notes.Url);
        Assert.Equal("/api/v1/forum/attachments/legacy/1101", notes.DownloadUrl);
        Assert.NotEqual(image.Url, image.DownloadUrl);
        Assert.StartsWith("/forum/attachment/", image.Url, StringComparison.Ordinal);
        Assert.StartsWith("/api/v1/forum/attachments/", image.DownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_IncludesBearerAttachmentRoutes()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(ApiV1.OpenApiPath);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = payload.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/v1/forum/attachments/legacy/{legacyPostId}", out var legacy));
        Assert.True(legacy.TryGetProperty("get", out var legacyGet));
        Assert.True(legacyGet.GetProperty("responses").TryGetProperty("200", out _));
        Assert.False(legacyGet.GetProperty("responses").TryGetProperty("302", out _));
        Assert.True(paths.TryGetProperty("/api/v1/forum/attachments/{legacyPostId}/{attachmentId}", out var modern));
        Assert.True(modern.TryGetProperty("get", out _));
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

    private HttpClient CreateBearerClient() => CreateBearerClient(factory);

    private static HttpClient CreateBearerClient(WebApplicationFactory<Program> source)
    {
        using var scope = source.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(Guid.NewGuid(), "attach@example.test", "Forum Attach Member");
        var client = source.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
