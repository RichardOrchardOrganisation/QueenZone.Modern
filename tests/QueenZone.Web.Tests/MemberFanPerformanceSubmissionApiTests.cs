using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MemberFanPerformanceSubmissionApiTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;

    public MemberFanPerformanceSubmissionApiTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Submit_requires_bearer_token()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var response = await anonymous.PostAsync(
            MemberApiEndpoints.FanPerformanceSubmissionsPath,
            CreateMultipart("Needs a token"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Submit_uses_the_same_service_path_as_the_website()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(memberId, "Mobile Stage");
        using var response = await client.PostAsync(
            MemberApiEndpoints.FanPerformanceSubmissionsPath,
            CreateMultipart("App cover", coveredSong: "Reaching Out", performedBy: "Mobile Stage"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<FanPerformanceSubmissionCreatedDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, created!.Status);
        Assert.Equal("App cover", created.Title);
        Assert.Equal(
            $"{MemberApiEndpoints.FanPerformanceSubmissionsPath}/{created.Id:D}",
            response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFanPerformanceSubmissionRepository>();
        var stored = await repository.GetByIdAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(memberId, stored!.SubmitterMemberId);
        Assert.Equal("Reaching Out", stored.CoveredSong);
        Assert.Equal(BlobUploadContainers.FanPerformances, stored.BlobPath.StartsWith("members/", StringComparison.Ordinal)
            ? BlobUploadContainers.FanPerformances
            : stored.BlobPath);
        Assert.StartsWith($"members/{memberId:N}/", stored.BlobPath);
    }

    [Fact]
    public async Task Submit_rejects_missing_rights_declaration()
    {
        using var client = CreateBearerClient(Guid.NewGuid());
        using var response = await client.PostAsync(
            MemberApiEndpoints.FanPerformanceSubmissionsPath,
            CreateMultipart("No rights", rightsAccepted: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateBearerClient(Guid memberId, string displayName = "Stage Fan")
    {
        MemberBearerAccounts.Ensure(factory.Services, memberId, $"{memberId:N}@example.test", displayName);
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static MultipartFormDataContent CreateMultipart(
        string title,
        string coveredSong = "Somebody to Love",
        string performedBy = "Stage Fan",
        bool rightsAccepted = true)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent(coveredSong), "coveredSong");
        content.Add(new StringContent(performedBy), "performedBy");
        content.Add(new StringContent(rightsAccepted ? "true" : "false"), "rightsDeclarationAccepted");
        var file = new ByteArrayContent(CreateMpegPayload(400));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "audio", "cover.mp3");
        return content;
    }

    private static byte[] CreateMpegPayload(int length)
    {
        var bytes = new byte[Math.Max(length, 4)];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        return bytes;
    }
}
