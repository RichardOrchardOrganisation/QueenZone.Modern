using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumAttachmentEndpointsTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly VariantWebApplicationFactory legacyBlobs;
    private readonly VariantWebApplicationFactory missingBlob;
    private readonly VariantWebApplicationFactory modernDownload;

    public ForumAttachmentEndpointsTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        legacyBlobs = variants.Get(WebHostVariants.TestingLegacyForumAttachmentMemoryBlobs);
        missingBlob = variants.Get(WebHostVariants.TestingLegacyForumAttachmentMissingBlob);
        modernDownload = variants.Get(WebHostVariants.TestingModernForumAttachmentDownload);
    }

    public Task InitializeAsync() => modernDownload.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

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
        var client = CreateMemberClient(legacyBlobs);

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
        var client = CreateMemberClient(missingBlob);

        var response = await client.GetAsync("/forum/attachment/legacy/42");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ModernDownload_IncrementsDownloadCountAndStreamsFile()
    {
        var attachmentId = FixedIdAttachmentRepository.ModernAttachmentId;
        var client = CreateMemberClient(modernDownload);

        var response = await client.GetAsync($"/forum/attachment/9001/{attachmentId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello attachment", body);
        Assert.Equal(1, modernDownload.FixedForumAttachment!.DownloadCount);
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
}
