using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class ContentApiFanPerformancesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ContentApiFanPerformancesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task FanPerformances_list_requires_no_auth_and_includes_duration()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<FanPerformanceDto>>();
        Assert.NotNull(payload);
        Assert.Equal(4, payload!.TotalCount);
        Assert.Equal(4, payload.Items.Count);
        var first = payload.Items[0];
        Assert.Equal(187, first.Id);
        Assert.Equal("Reaching Out", first.Title);
        Assert.Equal("Mike Ryde", first.PerformedBy);
        Assert.Equal(320, first.DurationSeconds);
        Assert.Equal("/fan-performances", first.DetailPath);
        Assert.Equal("/api/v1/content/fan-performances/187/audio", first.AudioPath);
        Assert.DoesNotContain(payload.Items, item => item.AudioPath.Contains("songfiles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FanPerformances_list_reads_stored_duration_without_opening_blobs()
    {
        using var isolated = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<IBlobUploadService>();
            services.AddSingleton<IBlobUploadService, ThrowOnReadBlobService>();
        });
        using var client = isolated.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<FanPerformanceDto>>();
        Assert.Equal(320, payload!.Items[0].DurationSeconds);
    }

    private sealed class ThrowOnReadBlobService : IBlobUploadService
    {
        public Task<BlobUploadResult> UploadAsync(Stream content, string originalFileName, string containerName,
            BlobUploadContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BlobContent?> OpenReadAsync(string containerName, string blobName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("List must not open audio blobs.");
    }

    [Fact]
    public async Task FanPerformances_list_clamps_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances?page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<FanPerformanceDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
        Assert.Equal(4, payload.Items.Count);
    }

    [Fact]
    public async Task FanPerformances_list_returns_empty_items_past_the_last_page()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances?page=9&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<FanPerformanceDto>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(4, payload.TotalCount);
    }

    [Fact]
    public async Task FanPerformance_detail_returns_duration_and_audio_path()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/187");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<FanPerformanceDto>();
        Assert.NotNull(item);
        Assert.Equal(187, item!.Id);
        Assert.Equal(320, item.DurationSeconds);
        Assert.Equal("/api/v1/content/fan-performances/187/audio", item.AudioPath);
        Assert.Contains("Rock Therapy", item.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FanPerformance_list_and_detail_include_contributor_credit_when_approved_submission_exists()
    {
        var member = await factory.Services.GetRequiredService<IMemberAccountRepository>()
            .CreateAsync(new MemberAccount
            {
                Id = Guid.NewGuid(),
                Email = "credit-fan@example.com",
                DisplayName = "Credit Fan",
                CreatedAt = DateTime.UtcNow,
            });
        var submissions = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        var created = await submissions.CreateAsync(new NewFanPerformanceSubmission(
            member.Id,
            "Credit cover",
            "Reaching Out",
            "Mike Ryde",
            null,
            "pending/credit.mp3",
            "credit.mp3",
            1024,
            "audio/mpeg",
            120,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion));
        await submissions.PromoteAsync(created.Id, 187, "admin@test.local", null);

        using var client = factory.CreateAnonymousClient();
        using var listResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances");
        var list = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<FanPerformanceDto>>();
        var credited = Assert.Single(list!.Items, item => item.Id == 187);
        Assert.Equal(member.Id, credited.ContributorMemberId);
        Assert.Equal("Credit Fan", credited.ContributorDisplayName);
        Assert.Equal("Mike Ryde", credited.PerformedBy);

        using var detailResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/187");
        var detail = await detailResponse.Content.ReadFromJsonAsync<FanPerformanceDto>();
        Assert.Equal(member.Id, detail!.ContributorMemberId);
        Assert.Equal("Credit Fan", detail.ContributorDisplayName);
        Assert.All(list.Items.Where(item => item.Id != 187), item =>
        {
            Assert.Null(item.ContributorMemberId);
            Assert.Null(item.ContributorDisplayName);
        });
    }

    [Fact]
    public async Task FanPerformance_detail_returns_problem_details_for_missing_id()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status404NotFound, problem.GetProperty("status").GetInt32());
        Assert.Contains("424242", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FanPerformance_audio_returns_unauthorized_without_bearer_token()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/187/audio");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FanPerformance_audio_streams_for_mobile_member_token()
    {
        await SeedSampleSongfileAsync();
        using var client = CreateBearerClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/187/audio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Reaching-Out.mp3", response.Content.Headers.ContentDisposition?.ToString());
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("ID3fake-audio"u8.ToArray(), body);
        Assert.Equal(body.LongLength, response.Content.Headers.ContentLength);
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.ETag?.Tag));
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task FanPerformance_audio_supports_http_range_requests()
    {
        await SeedSampleSongfileAsync();
        using var client = CreateBearerClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ContentApiEndpoints.RootPath}/fan-performances/187/audio");
        request.Headers.Range = new RangeHeaderValue(0, 3);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("ID3f"u8.ToArray(), await response.Content.ReadAsByteArrayAsync());
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.ETag?.Tag));
    }

    [Fact]
    public async Task FanPerformance_audio_returns_not_found_for_unknown_id()
    {
        using var client = CreateBearerClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/999999/audio");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FanPerformance_audio_does_not_expose_public_blob_location()
    {
        await SeedSampleSongfileAsync();
        using var client = CreateBearerClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/fan-performances/187/audio");
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("cdn2.queenzone.org", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob.core.windows.net", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn2.queenzone.org", body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateBearerClient()
    {
        var memberId = Guid.NewGuid();
        MemberBearerAccounts.Ensure(factory.Services, memberId, $"{memberId:N}@example.test", "Fan Stage Member");
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, "fanstage@example.com", "Fan Stage Member");
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedSampleSongfileAsync()
    {
        var backend = factory.Services.GetRequiredService<IBlobStorageBackend>();
        await using var audio = new MemoryStream(Encoding.ASCII.GetBytes("ID3fake-audio"));
        await backend.UploadAsync(
            SongFileUrl.ContainerName,
            "2014417798057369.mp3",
            audio,
            "audio/mpeg");
    }
}
