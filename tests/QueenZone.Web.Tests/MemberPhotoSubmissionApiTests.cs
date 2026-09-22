using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class MemberPhotoSubmissionApiTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;

    public MemberPhotoSubmissionApiTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Submit_requires_bearer_token()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        await using var png = await CreatePngAsync();
        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var response = await client.PostAsync(
                MemberApiEndpoints.PhotoSubmissionsPath,
                CreateMultipart("Needs a token", png));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Submit_returns_created_id_and_pending_status()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(factory, memberId, "Photo Fan");
        await using var png = await CreatePngAsync();
        using var content = CreateMultipart(
            "Wembley crowd shot",
            png,
            description: "From the stands",
            suggestedCategory: "Queen",
            approximateYear: "1986",
            approximateDate: "1986-07-12",
            fileFieldName: "photo");

        using var response = await client.PostAsync(MemberApiEndpoints.PhotoSubmissionsPath, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PhotoSubmissionCreatedDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.Equal(PhotoSubmissionStatus.Pending, created.Status);
        Assert.Equal("Wembley crowd shot", created.Title);
        Assert.Equal(
            $"{MemberApiEndpoints.PhotoSubmissionsPath}/{created.Id:D}",
            response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPhotoSubmissionRepository>();
        var stored = await repository.GetByIdAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(memberId, stored!.SubmitterMemberId);
        Assert.Equal(PhotoSubmissionStatus.Pending, stored.Status);
        Assert.Equal("From the stands", stored.Description);
        Assert.Equal("Queen", stored.SuggestedCategory);
        Assert.Equal(1986, stored.ApproximateYear);
        Assert.Equal(new DateOnly(1986, 7, 12), stored.ApproximateDate);
    }

    [Fact]
    public async Task Submit_accepts_website_form_field_names()
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());
        await using var png = await CreatePngAsync();
        using var content = CreateMultipart(
            "Pascal case fields",
            png,
            fileFieldName: "PhotoFile",
            titleFieldName: "Title");

        using var response = await client.PostAsync(MemberApiEndpoints.PhotoSubmissionsPath, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PhotoSubmissionCreatedDto>(JsonOptions);
        Assert.Equal("Pascal case fields", created!.Title);
    }

    [Fact]
    public async Task Submit_does_not_advertise_or_accept_json_bodies()
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            new { title = "Not multipart" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Submit_returns_bad_request_when_photo_missing()
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("No file"), "title");

        using var response = await client.PostAsync(MemberApiEndpoints.PhotoSubmissionsPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("A photo file is required.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Submit_returns_bad_request_for_validation_errors()
    {
        using var client = CreateBearerClient(factory, Guid.NewGuid());
        await using var png = await CreatePngAsync();

        using var missingTitle = await client.PostAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            CreateMultipart("   ", png));
        Assert.Equal(HttpStatusCode.BadRequest, missingTitle.StatusCode);
        var missingTitleProblem = await missingTitle.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Title is required.", missingTitleProblem.GetProperty("detail").GetString());

        using var junk = new MemoryStream("not-image"u8.ToArray());
        using var invalidImage = await client.PostAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            CreateMultipart("Bad file", junk, fileName: "note.txt"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidImage.StatusCode);
        Assert.Equal("application/problem+json", invalidImage.Content.Headers.ContentType?.MediaType);
        var invalidProblem = await invalidImage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("JPEG", invalidProblem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_returns_too_many_requests_when_daily_quota_exceeded()
    {
        using var quotaFactory = CreateQuotaFactory(maxUploadsPerDay: 1);
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(quotaFactory, memberId);
        await using var first = await CreatePngAsync();
        using var ok = await client.PostAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            CreateMultipart("First of the day", first));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        await using var second = await CreatePngAsync();
        using var blocked = await client.PostAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            CreateMultipart("Second should fail", second));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.GetProperty("status").GetInt32());
        Assert.Contains("Daily upload limit reached", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_service_and_api_share_one_quota_bucket_for_the_same_member()
    {
        var quota = CreateQuotaService(maxUploadsPerDay: 1);
        using var quotaFactory = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<MemberUploadQuotaService>();
            services.AddSingleton(quota);
        });

        var memberId = Guid.NewGuid();
        await using var webPhoto = await CreatePngAsync();
        using (var scope = quotaFactory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PhotoSubmissionService>();
            var webResult = await service.SubmitAsync(
                memberId,
                "Submitted on the website",
                null,
                null,
                null,
                null,
                webPhoto,
                "web.png");
            Assert.True(webResult.Succeeded);
        }

        using var client = CreateBearerClient(quotaFactory, memberId);
        await using var mobilePhoto = await CreatePngAsync();
        using var response = await client.PostAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            CreateMultipart("Submitted from the app", mobilePhoto));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Daily upload limit reached (1 uploads per day)", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var principalKey = MemberUploadQuotaService.PrincipalKeyFromMemberId(memberId);
        Assert.False(quota.TryConsume(principalKey, 1, out var stillBlocked));
        Assert.Contains("Daily upload", stillBlocked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_returns_too_many_requests_when_uploads_are_disabled()
    {
        using var quotaFactory = CreateQuotaFactory(maxUploadsPerDay: 0);
        using var client = CreateBearerClient(quotaFactory, Guid.NewGuid());
        await using var png = await CreatePngAsync();

        using var response = await client.PostAsync(
            MemberApiEndpoints.PhotoSubmissionsPath,
            CreateMultipart("Disabled uploads", png));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Uploads are temporarily disabled.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("Daily upload limit reached (1 uploads per day). Try again tomorrow.")]
    [InlineData("Daily upload size limit reached (100 MB per day). Try again tomorrow.")]
    [InlineData("Upload exceeds the daily size limit (100 MB per day).")]
    [InlineData("Uploads are temporarily disabled.")]
    [InlineData("Daily upload limit reached.")]
    public void IsQuotaLimitError_matches_service_messages(string message)
    {
        Assert.True(MemberApiEndpoints.IsQuotaLimitError(message));
    }

    [Theory]
    [InlineData("Title is required.")]
    [InlineData("Photo must be a JPEG, PNG, WebP, or TIFF image.")]
    [InlineData("Photo must be 20 MB or smaller.")]
    [InlineData("A photo file is required.")]
    [InlineData("Sign in is required to submit a photo.")]
    [InlineData(null)]
    [InlineData("")]
    public void IsQuotaLimitError_rejects_validation_messages(string? message)
    {
        Assert.False(MemberApiEndpoints.IsQuotaLimitError(message));
    }

    private static QueenZoneWebApplicationFactory CreateQuotaFactory(int maxUploadsPerDay) =>
        QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<MemberUploadQuotaService>();
            services.AddSingleton(CreateQuotaService(maxUploadsPerDay));
        });

    private static MemberUploadQuotaService CreateQuotaService(int maxUploadsPerDay) =>
        new(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new UploadQuotaOptions
            {
                Enabled = true,
                MaxUploadsPerDay = maxUploadsPerDay,
                MaxBytesPerDay = 100L * 1024 * 1024,
            }));

    private static HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory source,
        Guid memberId,
        string displayName = "Photo Fan")
    {
        MemberBearerAccounts.Ensure(source.Services, memberId, $"{memberId:N}@example.test", displayName);
        using var scope = source.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = source.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static MultipartFormDataContent CreateMultipart(
        string title,
        Stream photo,
        string? description = null,
        string? suggestedCategory = null,
        string? approximateYear = null,
        string? approximateDate = null,
        string fileFieldName = "photo",
        string titleFieldName = "title",
        string fileName = "photo.png")
    {
        if (photo.CanSeek)
        {
            photo.Position = 0;
        }

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), titleFieldName);
        if (description is not null)
        {
            content.Add(new StringContent(description), "description");
        }

        if (suggestedCategory is not null)
        {
            content.Add(new StringContent(suggestedCategory), "suggestedCategory");
        }

        if (approximateYear is not null)
        {
            content.Add(new StringContent(approximateYear), "approximateYear");
        }

        if (approximateDate is not null)
        {
            content.Add(new StringContent(approximateDate), "approximateDate");
        }

        var bytes = new MemoryStream();
        photo.CopyTo(bytes);
        var file = new ByteArrayContent(bytes.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(
            fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "text/plain" : "image/png");
        content.Add(file, fileFieldName, fileName);
        return content;
    }

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(40, 40, new Rgba32(10, 20, 30));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
