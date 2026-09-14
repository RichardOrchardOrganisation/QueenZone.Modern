using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Routing;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed partial class MySubmissionsPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly InMemoryBlobStorageBackend blobBackend = new();

    public MySubmissionsPageTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                        MemberAuthenticationSchemes.ExternalCookie, _ => { });

                services.RemoveAll<IBlobUploadService>();
                services.AddSingleton<IBlobUploadService>(_ =>
                    new AzureBlobUploadService(blobBackend, Options.Create(new BlobUploadOptions())));
            });
        });
    }

    [Fact]
    public async Task Get_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/account/my-submissions");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_ShowsOnlyCurrentMembersSubmissions_AcrossTabs()
    {
        var owner = await CreateSignedInMemberClientAsync(
            email: "mysubs-owner@example.com",
            displayName: "Owner Fan",
            subject: "google-mysubs-owner",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var other = await CreateSignedInMemberClientAsync(
            email: "mysubs-other@example.com",
            displayName: "Other Fan",
            subject: "google-mysubs-other",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        await SubmitPhotoAsync(owner, "Owner exclusive photo");
        await SubmitNewsAsync(owner, "https://example.com/owner-exclusive-news-story", "Owner news");
        await SubmitArticleAsync(owner, "Owner exclusive article");
        await SubmitTriviaAsync(owner, "Owner exclusive trivia fact about Queen.");
        await SubmitFanPerformanceAsync(owner, "Owner exclusive performance");

        await SubmitPhotoAsync(other, "Other member photo secret");
        await SubmitNewsAsync(other, "https://example.com/other-member-news-secret", "Other news");
        await SubmitArticleAsync(other, "Other member article secret");
        await SubmitTriviaAsync(other, "Other member trivia secret about a rumour.");
        await SubmitFanPerformanceAsync(other, "Other member performance secret");

        var ownerPhotos = await owner.GetStringAsync("/account/my-submissions?tab=photos");
        Assert.Contains("Owner exclusive photo", ownerPhotos);
        Assert.DoesNotContain("Other member photo secret", ownerPhotos);
        Assert.Contains("qz-status-badge", ownerPhotos);

        var ownerNews = await owner.GetStringAsync("/account/my-submissions?tab=news");
        Assert.Contains("owner-exclusive-news-story", ownerNews);
        Assert.DoesNotContain("other-member-news-secret", ownerNews);

        var ownerArticles = await owner.GetStringAsync("/account/my-submissions?tab=articles");
        Assert.Contains("Owner exclusive article", ownerArticles);
        Assert.Contains("Continue editing", ownerArticles);
        Assert.DoesNotContain("Other member article secret", ownerArticles);

        var ownerTrivia = await owner.GetStringAsync("/account/my-submissions?tab=trivia");
        Assert.Contains("Owner exclusive trivia fact about Queen.", ownerTrivia);
        Assert.DoesNotContain("Other member trivia secret about a rumour.", ownerTrivia);

        var ownerPerformances = await owner.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Owner exclusive performance", ownerPerformances);
        Assert.DoesNotContain("Other member performance secret", ownerPerformances);
        Assert.Contains("/submit/fan-performance", ownerPerformances);

        var otherPhotos = await other.GetStringAsync("/account/my-submissions?tab=photos");
        Assert.Contains("Other member photo secret", otherPhotos);
        Assert.DoesNotContain("Owner exclusive photo", otherPhotos);
    }

    [Fact]
    public async Task Get_NewsTab_ResolvesPromotedArticlesInSingleBatch()
    {
        var firstArticle = new NewsItem(
            1002,
            "First promoted story",
            "First excerpt",
            "First body",
            new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc),
            null,
            true,
            "first-promoted-story");
        var secondArticle = new NewsItem(
            1003,
            "Second promoted story",
            "Second excerpt",
            "Second body",
            new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc),
            null,
            true,
            "second-promoted-story");
        var trackingNews = new TrackingNewsRepository(new FixedNewsRepository([firstArticle, secondArticle]));
        using var testFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INewsRepository>();
                services.AddSingleton<INewsRepository>(trackingNews);
            }));
        const string email = "mysubs-news-batch@example.com";
        var client = await CreateSignedInMemberClientAsync(
            email,
            "News Batch Fan",
            "google-mysubs-news-batch",
            new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            },
            testFactory);

        await SubmitNewsAsync(client, "https://example.com/first-promoted-story", "First suggestion");
        await SubmitNewsAsync(client, "https://example.com/second-promoted-story", "Second suggestion");

        var member = await testFactory.Services
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync(email);
        Assert.NotNull(member);
        var suggestionRepository = testFactory.Services.GetRequiredService<INewsSuggestionRepository>();
        var suggestions = await suggestionRepository.GetBySubmitterAsync(member.Id);
        Assert.Equal(2, suggestions.Items.Count);
        await suggestionRepository.PromoteAsync(
            suggestions.Items[0].Id,
            firstArticle.Id,
            "admin@test.local",
            null);
        await suggestionRepository.PromoteAsync(
            suggestions.Items[1].Id,
            secondArticle.Id,
            "admin@test.local",
            null);

        var page = await client.GetStringAsync("/account/my-submissions?tab=news");

        Assert.Equal(1, trackingNews.GetByIdsCallCount);
        Assert.Equal(0, trackingNews.GetByIdCallCount);
        Assert.Equal([firstArticle.Id, secondArticle.Id], trackingNews.LastRequestedIds.Order());
        Assert.Contains(NewsRoutes.GetNewsDetailPath(firstArticle), page);
        Assert.Contains(NewsRoutes.GetNewsDetailPath(secondArticle), page);
    }

    [Fact]
    public async Task Get_ArticleDraft_LinksToSubmitArticleIdPath()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "mysubs-draft-link@example.com",
            displayName: "Draft Link Fan",
            subject: "google-mysubs-draft-link",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var draftId = await SubmitArticleAsync(client, "Draft link target article");
        var page = await client.GetStringAsync("/account/my-submissions?tab=articles");

        Assert.Contains($"/submit/article/{draftId:D}", page);
        Assert.Contains("Continue editing", page);

        var editPage = await client.GetStringAsync($"/submit/article/{draftId:D}");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/submit/article/{draftId:D}")).StatusCode);
        Assert.Contains("Draft link target article", editPage);
    }

    [Fact]
    public async Task Get_FanPerformanceApproved_ShowsLivePublicLink_NotReviewNotes()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "mysubs-approved-perf@example.com",
            displayName: "Approved Perf Fan",
            subject: "google-mysubs-approved-perf",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var submissionId = await SubmitFanPerformanceAsync(client, "Approved live performance");
        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        await repository.PromoteAsync(submissionId, 187, "admin@test.local", "internal approve note");

        var page = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Approved live performance", page);
        Assert.Contains("View live performance", page);
        Assert.Contains(FanPerformanceRoutes.GetPublicPath(187), page);
        Assert.DoesNotContain("internal approve note", page);
    }

    [Fact]
    public async Task Get_FanPerformanceRejected_ShowsRejectionReason_NotReviewNotes()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "mysubs-rejected-perf@example.com",
            displayName: "Rejected Perf Fan",
            subject: "google-mysubs-rejected-perf",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var submissionId = await SubmitFanPerformanceAsync(client, "Rejected live performance");
        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        await repository.UpdateStatusAsync(
            submissionId,
            FanPerformanceSubmissionStatus.Rejected,
            "admin@test.local",
            "internal reject note",
            "Not a Queen cover");

        var page = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Rejected live performance", page);
        Assert.Contains("Not a Queen cover", page);
        Assert.DoesNotContain("internal reject note", page);
        Assert.DoesNotContain("View live performance", page);
    }

    [Fact]
    public async Task Get_FanPerformanceNeedsInfo_ShowsReviewNotesAsk()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "mysubs-needsinfo-perf@example.com",
            displayName: "NeedsInfo Perf Fan",
            subject: "google-mysubs-needsinfo-perf",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var submissionId = await SubmitFanPerformanceAsync(client, "Needs info performance");
        var repository = factory.Services.GetRequiredService<IFanPerformanceSubmissionRepository>();
        await repository.UpdateStatusAsync(
            submissionId,
            FanPerformanceSubmissionStatus.NeedsInfo,
            "admin@test.local",
            "Please name the Queen song",
            null);

        var page = await client.GetStringAsync("/account/my-submissions?tab=performances");
        Assert.Contains("Needs info performance", page);
        Assert.Contains("Please name the Queen song", page);
        Assert.Contains("Needs info", page);
        Assert.DoesNotContain("View live performance", page);
    }

    private async Task SubmitPhotoAsync(HttpClient client, string title)
    {
        var formPage = await client.GetStringAsync("/submit/photo");
        await using var png = await CreatePngAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent("Queen"), "SuggestedCategory");
        var fileContent = new StreamContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "PhotoFile", "shot.png");

        var response = await client.PostAsync("/submit/photo", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task SubmitNewsAsync(HttpClient client, string url, string title)
    {
        var formPage = await client.GetStringAsync("/submit/news");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["StoryUrl"] = url,
            ["Title"] = title,
            ["Notes"] = "Notes",
        });

        var response = await client.PostAsync("/submit/news", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task SubmitTriviaAsync(HttpClient client, string text)
    {
        var formPage = await client.GetStringAsync("/submit/trivia");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Text"] = text,
            ["Category"] = "Band",
        });

        var response = await client.PostAsync("/submit/trivia", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<Guid> SubmitFanPerformanceAsync(HttpClient client, string title)
    {
        var formPage = await client.GetStringAsync("/submit/fan-performance");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent("Bohemian Rhapsody"), "CoveredSong");
        content.Add(new StringContent("Owner Fan"), "PerformedBy");
        content.Add(new StringContent("true"), "RightsDeclarationAccepted");
        var bytes = new byte[200];
        Mp3DurationTests.CreateMpeg1Layer3Header(9).CopyTo(bytes.AsSpan());
        var fileContent = new StreamContent(new MemoryStream(bytes));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "AudioFile", "cover.mp3");

        var response = await client.PostAsync("/submit/fan-performance", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
    }

    private async Task<Guid> SubmitArticleAsync(HttpClient client, string title)
    {
        var formPage = await client.GetStringAsync("/submit/article");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Title"] = title,
            ["Excerpt"] = "Excerpt",
            ["Body"] = new string('a', EfArticleSubmissionRepository.MinBodyVisibleChars),
            ["Tags"] = "queen",
            ["action"] = "save",
        });

        var response = await client.PostAsync("/submit/article", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var match = DraftIdRegex().Match(body);
        Assert.True(match.Success, "Draft id was not found after save.");
        return Guid.Parse(match.Groups["id"].Value);
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(
        string email,
        string displayName,
        string subject,
        WebApplicationFactoryClientOptions? options = null,
        WebApplicationFactory<Program>? sourceFactory = null)
    {
        var client = (sourceFactory ?? factory).CreateClient(options ?? new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callbackResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callbackResponse.StatusCode}");

        return client;
    }

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(80, 60, new Rgba32(40, 120, 200));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();

    [GeneratedRegex("""name="DraftId"[^>]*value="(?<id>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex DraftIdRegex();

    private sealed class TrackingNewsRepository(INewsRepository inner) : INewsRepository
    {
        public int GetByIdCallCount { get; private set; }

        public int GetByIdsCallCount { get; private set; }

        public IReadOnlyList<int> LastRequestedIds { get; private set; } = [];

        public Task<IReadOnlyList<NewsItem>> GetLatestAsync(
            int count,
            CancellationToken cancellationToken = default) =>
            inner.GetLatestAsync(count, cancellationToken);

        public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
            int page,
            int pageSize,
            NewsArchiveFilter filter = default,
            CancellationToken cancellationToken = default) =>
            inner.GetArchivePageAsync(page, pageSize, filter, cancellationToken);

        public Task<int> GetPublishedCountAsync(
            NewsArchiveFilter filter = default,
            CancellationToken cancellationToken = default) =>
            inner.GetPublishedCountAsync(filter, cancellationToken);

        public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
            inner.GetArchiveYearRangeAsync(cancellationToken);

        public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            GetByIdCallCount++;
            return inner.GetByIdAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default)
        {
            GetByIdsCallCount++;
            LastRequestedIds = ids.ToArray();
            return inner.GetByIdsAsync(ids, cancellationToken);
        }

        public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetPublishedSitemapEntriesAsync(cancellationToken);

        public Task<NewsSearchPage> SearchAsync(
            string query,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchAsync(query, page, pageSize, cancellationToken);
    }
}
