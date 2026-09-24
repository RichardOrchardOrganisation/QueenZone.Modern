using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class EditorImageUploadEndpointsTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public EditorImageUploadEndpointsTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task UploadAsync_returns_unauthorized_when_not_authenticated()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()),
            RequestServices = factory.Services,
        };
        var antiforgery = factory.Services.GetRequiredService<IAntiforgery>();

        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file: null,
            container: null,
            new StubBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_returns_bad_request_for_missing_file()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file: null,
            container: null,
            new StubBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_returns_bad_request_for_disallowed_content_type()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        // Executable content is not in the forum allowlist.
        var file = CreateFormFile("payload.exe", "application/octet-stream", [0x4D, 0x5A, 0x90, 0x00]);
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: null,
            new StubBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_accepts_plain_text_as_file_attachment()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var stub = new StubBlobUploadService();
        var file = CreateFormFile("note.txt", "text/plain", "hello forum"u8.ToArray());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: BlobUploadContainers.Forum,
            stub,
            antiforgery,
            CancellationToken.None);

        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("file", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task UploadAsync_returns_bad_request_for_oversized_image()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var huge = new byte[EditorImageUploadEndpoints.MaxImageBytes + 1];
        huge[0] = 0xFF;
        huge[1] = 0xD8;
        huge[2] = 0xFF;
        var file = CreateFormFile("big.jpg", "image/jpeg", huge);
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: null,
            new StubBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_returns_bad_request_for_disallowed_container()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var file = CreateFormFile("x.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: "legacy-photos",
            new StubBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_returns_service_unavailable_when_storage_not_configured()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var file = CreateFormFile("x.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: null,
            new NullBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_returns_ok_with_proxy_url_and_uploads_full_plus_thumb()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var stub = new StubBlobUploadService();
        var file = CreateFormFile("x.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: BlobUploadContainers.Articles,
            stub,
            antiforgery,
            CancellationToken.None);

        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(BlobUploadContainers.Articles, stub.LastContainer);
        Assert.Equal(2, stub.UploadCount);

        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("url").GetString();
        var thumbUrl = doc.RootElement.GetProperty("thumbUrl").GetString();
        Assert.StartsWith("/ugc/articles/", url);
        Assert.EndsWith(".webp", url);
        Assert.StartsWith("/ugc/articles/", thumbUrl);
        Assert.Contains("-thumb.webp", thumbUrl);
    }

    [Theory]
    [InlineData(null, BlobUploadContainers.Forum)]
    [InlineData("", BlobUploadContainers.Forum)]
    [InlineData("articles", BlobUploadContainers.Articles)]
    [InlineData("ugc-articles", BlobUploadContainers.Articles)]
    [InlineData("forum", BlobUploadContainers.Forum)]
    public void ResolveContainer_accepts_area_alias_and_blob_name(string? container, string expected)
    {
        Assert.Equal(expected, EditorImageUploadEndpoints.ResolveContainer(container));
    }

    [Fact]
    public async Task UploadAsync_accepts_articles_area_alias()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var stub = new StubBlobUploadService();
        var file = CreateFormFile("x.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: "articles",
            stub,
            antiforgery,
            CancellationToken.None);

        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(BlobUploadContainers.Articles, stub.LastContainer);
        using var doc = JsonDocument.Parse(body);
        Assert.StartsWith("/ugc/articles/", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task UploadAsync_defaults_to_forum_container()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var stub = new StubBlobUploadService();
        var file = CreateFormFile("paste.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: null,
            stub,
            antiforgery,
            CancellationToken.None);

        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(BlobUploadContainers.Forum, stub.LastContainer);

        using var doc = JsonDocument.Parse(body);
        Assert.StartsWith("/ugc/forum/", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task UploadAsync_accepts_pdf_as_file_attachment()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var stub = new StubBlobUploadService();
        // %PDF-1.4 minimal header
        var pdf = "%PDF-1.4\n%âãÏÓ\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
        var file = CreateFormFile("notes.pdf", "application/pdf", pdf);
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: BlobUploadContainers.Forum,
            stub,
            antiforgery,
            CancellationToken.None);

        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("file", doc.RootElement.GetProperty("kind").GetString());
        Assert.StartsWith("/ugc/forum/", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal(1, stub.UploadCount);
    }

    [Fact]
    public async Task UploadAsync_returns_bad_request_when_image_bytes_invalid()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        // JPEG magic bytes but not a decodable image — processor rejects.
        var file = CreateFormFile("bad.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xD9]);
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: null,
            new StubBlobUploadService(),
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, await GetStatusCodeAsync(result));
    }

    [Fact]
    public async Task UploadAsync_cleans_up_when_blob_upload_throws()
    {
        var (httpContext, antiforgery) = CreateAuthenticatedHttpContext();
        var stub = new StubBlobUploadService { FailOnUploadNumber = 2 };
        var file = CreateFormFile("x.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: null,
            stub,
            antiforgery,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, await GetStatusCodeAsync(result));
        Assert.True(stub.DeleteCount >= 1);
    }

    [Fact]
    public async Task UploadAsync_uses_member_account_blob_prefix_when_nameidentifier_is_guid()
    {
        var memberId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, memberId.ToString("D")),
                    new Claim(ClaimTypes.Email, "member@example.com"),
                ],
                authenticationType: "Test")),
            RequestServices = factory.Services,
            Request = { Method = "POST" },
        };
        var stub = new StubBlobUploadService();
        var file = CreateFormFile("x.png", "image/png", await CreatePngBytesAsync());
        var result = await EditorImageUploadEndpoints.UploadAsync(
            httpContext,
            file,
            container: BlobUploadContainers.Forum,
            stub,
            new NoopAntiforgery(),
            CancellationToken.None);

        var (status, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("url").GetString();
        Assert.Contains($"/ugc/forum/members/{memberId:N}/", url);
    }

    [Fact]
    public async Task Http_route_rejects_anonymous_caller()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var content = await CreatePngFormAsync();
        var response = await client.PostAsync(EditorImageUploadEndpoints.Route, content);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Http_route_returns_ok_when_authenticated_with_stub_storage()
    {
        var stub = new StubBlobUploadService();
        await using var custom = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddSingleton<IBlobUploadService>(stub));
        });

        var client = custom.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, AdminHttpTestHelpers.AdminEmail);

        var token = await GetAntiforgeryTokenAsync(client);
        using var form = await CreatePngFormAsync(token);
        var response = await client.PostAsync(EditorImageUploadEndpoints.Route, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = doc.RootElement.GetProperty("url").GetString();
        Assert.StartsWith("/ugc/forum/", url);
        Assert.Equal(BlobUploadContainers.Forum, stub.LastContainer);
        Assert.Equal(2, stub.UploadCount);
    }

    [Fact]
    public async Task Http_route_accepts_articles_container_with_antiforgery()
    {
        var stub = new StubBlobUploadService();
        await using var custom = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddSingleton<IBlobUploadService>(stub));
        });

        var client = custom.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, AdminHttpTestHelpers.AdminEmail);

        var html = await client.GetStringAsync("/admin/articles/editor");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(html);
        using var form = await CreatePngFormAsync(token, container: "articles");
        using var request = new HttpRequestMessage(HttpMethod.Post, EditorImageUploadEndpoints.Route)
        {
            Content = form,
        };
        request.Headers.TryAddWithoutValidation(EditorImageUploadEndpoints.AntiforgeryHeaderName, token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = doc.RootElement.GetProperty("url").GetString();
        Assert.StartsWith("/ugc/articles/", url);
        Assert.Equal(BlobUploadContainers.Articles, stub.LastContainer);
        Assert.Equal(2, stub.UploadCount);
    }

    private (HttpContext HttpContext, IAntiforgery Antiforgery) CreateAuthenticatedHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Email, AdminHttpTestHelpers.AdminEmail),
                    new Claim(ClaimTypes.Name, AdminHttpTestHelpers.AdminEmail),
                ],
                authenticationType: "Test")),
            RequestServices = factory.Services,
        };
        httpContext.Request.Method = "POST";
        return (httpContext, new NoopAntiforgery());
    }

    /// <summary>Skips real antiforgery crypto for direct UploadAsync unit tests.</summary>
    private sealed class NoopAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) =>
            new("request", "cookie", "form", "header");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
            new("request", "cookie", "form", "header");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);

        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }

        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/admin/news");
        return AdminHttpTestHelpers.ExtractAntiforgeryToken(html);
    }

    private static async Task<MultipartFormDataContent> CreatePngFormAsync(
        string? token = null,
        string? container = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(await CreatePngBytesAsync());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "paste.png");
        if (!string.IsNullOrEmpty(token))
        {
            content.Add(new StringContent(token), "__RequestVerificationToken");
        }

        if (!string.IsNullOrEmpty(container))
        {
            content.Add(new StringContent(container), "container");
        }

        return content;
    }

    private static async Task<byte[]> CreatePngBytesAsync()
    {
        using var image = new Image<Rgba32>(32, 24);
        await using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static IFormFile CreateFormFile(string name, string contentType, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private async Task<int> GetStatusCodeAsync(IResult result)
    {
        var (status, _) = await ExecuteAsync(result);
        return status;
    }

    private async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext { RequestServices = factory.Services };
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (httpContext.Response.StatusCode, body);
    }

    private sealed class StubBlobUploadService : IBlobUploadService
    {
        public string? LastContainer { get; private set; }

        public int UploadCount { get; private set; }

        public int DeleteCount { get; private set; }

        public int? FailOnUploadNumber { get; init; }

        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default)
        {
            LastContainer = containerName;
            UploadCount++;
            if (FailOnUploadNumber is int failAt && UploadCount == failAt)
            {
                throw new BlobUploadException("Simulated upload failure.");
            }

            var blobName = context?.PreferredBlobName ?? originalFileName;
            return Task.FromResult(new BlobUploadResult
            {
                Container = containerName,
                BlobName = blobName,
                ContentType = UgcProxyPaths.WebpContentType,
                SizeBytes = content.CanSeek ? content.Length : 8,
                PublicUrl = $"https://cdn.test/{containerName}/{blobName}",
            });
        }

        public Task DeleteAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlobContent?>(null);
    }
}
