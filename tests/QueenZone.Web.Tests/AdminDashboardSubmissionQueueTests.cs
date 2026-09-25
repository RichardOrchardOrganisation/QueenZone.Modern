using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin;

namespace QueenZone.Web.Tests;

/// <summary>
/// Tests for the submission queue tiles on the admin dashboard (issue #291).
/// </summary>
public sealed class AdminDashboardSubmissionQueueTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;

    public AdminDashboardSubmissionQueueTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    // ── In-memory repo: photos ──────────────────────────────────────────────

    [Fact]
    public async Task PhotoRepo_GetDashboardCounts_ReturnsZerosWhenEmpty()
    {
        var repo = new InMemoryPhotoSubmissionRepository();
        var counts = await repo.GetDashboardCountsAsync(DateTimeOffset.UtcNow);

        Assert.Equal(SubmissionTypeCounts.Empty, counts);
    }

    [Fact]
    public async Task PhotoRepo_GetDashboardCounts_CountsPendingCorrectly()
    {
        var member = SampleMember();
        var repo = new InMemoryPhotoSubmissionRepository(id => id == member.Id ? member : null);
        var utcNow = DateTimeOffset.UtcNow;

        await repo.CreateAsync(SamplePhotoSubmission(member.Id), default);
        await repo.CreateAsync(SamplePhotoSubmission(member.Id), default);

        var counts = await repo.GetDashboardCountsAsync(utcNow);

        Assert.Equal(2, counts.Pending);
        Assert.Equal(2, counts.ReceivedToday);
        Assert.Equal(2, counts.ReceivedThisWeek);
    }

    [Fact]
    public async Task PhotoRepo_GetTopContributors_ReturnsEmpty_WhenNoSubmissions()
    {
        var repo = new InMemoryPhotoSubmissionRepository();
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var contributors = await repo.GetTopContributorsThisMonthAsync(monthStart, 5);

        Assert.Empty(contributors);
    }

    [Fact]
    public async Task PhotoRepo_GetTopContributors_GroupsByMember()
    {
        var member = SampleMember();
        var repo = new InMemoryPhotoSubmissionRepository(id => id == member.Id ? member : null);
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

        await repo.CreateAsync(SamplePhotoSubmission(member.Id), default);
        await repo.CreateAsync(SamplePhotoSubmission(member.Id), default);

        var contributors = await repo.GetTopContributorsThisMonthAsync(monthStart, 5);

        Assert.Single(contributors);
        Assert.Equal(member.DisplayName, contributors[0].DisplayName);
        Assert.Equal(2, contributors[0].Count);
    }

    // ── In-memory repo: news suggestions ───────────────────────────────────

    [Fact]
    public async Task NewsRepo_GetDashboardCounts_ReturnsZerosWhenEmpty()
    {
        var repo = new InMemoryNewsSuggestionRepository();
        var counts = await repo.GetDashboardCountsAsync(DateTimeOffset.UtcNow);

        Assert.Equal(SubmissionTypeCounts.Empty, counts);
    }

    [Fact]
    public async Task NewsRepo_GetDashboardCounts_CountsPendingCorrectly()
    {
        var member = SampleMember();
        var repo = new InMemoryNewsSuggestionRepository(id => id == member.Id ? member : null);
        var utcNow = DateTimeOffset.UtcNow;

        await repo.CreateAsync(SampleNewsSuggestion(member.Id), default);

        var counts = await repo.GetDashboardCountsAsync(utcNow);

        Assert.Equal(1, counts.Pending);
        Assert.Equal(1, counts.ReceivedToday);
    }

    [Fact]
    public async Task NewsRepo_GetTopContributors_GroupsByMember()
    {
        var member = SampleMember();
        var repo = new InMemoryNewsSuggestionRepository(id => id == member.Id ? member : null);
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

        await repo.CreateAsync(SampleNewsSuggestion(member.Id), default);
        await repo.CreateAsync(SampleNewsSuggestion(member.Id, "https://example.com/2"), default);

        var contributors = await repo.GetTopContributorsThisMonthAsync(monthStart, 5);

        Assert.Single(contributors);
        Assert.Equal(2, contributors[0].Count);
    }

    // ── In-memory repo: articles ────────────────────────────────────────────

    [Fact]
    public async Task ArticleRepo_GetDashboardCounts_ReturnsZerosWhenEmpty()
    {
        var repo = new InMemoryArticleSubmissionRepository();
        var counts = await repo.GetDashboardCountsAsync(DateTimeOffset.UtcNow);

        Assert.Equal(SubmissionTypeCounts.Empty, counts);
    }

    [Fact]
    public async Task ArticleRepo_GetDashboardCounts_CountsSubmittedAsPending()
    {
        var member = SampleMember();
        var repo = new InMemoryArticleSubmissionRepository(id => id == member.Id ? member : null);
        var utcNow = DateTimeOffset.UtcNow;

        var draft = await repo.UpsertDraftAsync(SampleArticleDraft(member.Id), default);
        await repo.SubmitForReviewAsync(draft.Id, member.Id, default);

        var counts = await repo.GetDashboardCountsAsync(utcNow);

        Assert.Equal(1, counts.Pending);
        Assert.Equal(1, counts.ReceivedToday);
    }

    [Fact]
    public async Task ArticleRepo_GetTopContributors_GroupsByMember()
    {
        var member = SampleMember();
        var repo = new InMemoryArticleSubmissionRepository(id => id == member.Id ? member : null);
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var draft1 = await repo.UpsertDraftAsync(SampleArticleDraft(member.Id, "Title One"), default);
        await repo.SubmitForReviewAsync(draft1.Id, member.Id, default);

        var draft2 = await repo.UpsertDraftAsync(SampleArticleDraft(member.Id, "Title Two"), default);
        await repo.SubmitForReviewAsync(draft2.Id, member.Id, default);

        var contributors = await repo.GetTopContributorsThisMonthAsync(monthStart, 5);

        Assert.Single(contributors);
        Assert.Equal(2, contributors[0].Count);
    }

    // ── In-memory repo: fan performances ───────────────────────────────────

    [Fact]
    public async Task FanPerformanceRepo_GetDashboardCounts_ReturnsZerosWhenEmpty()
    {
        var repo = new InMemoryFanPerformanceSubmissionRepository();
        var counts = await repo.GetDashboardCountsAsync(DateTimeOffset.UtcNow);

        Assert.Equal(FanPerformanceDashboardCounts.Empty, counts);
    }

    [Fact]
    public async Task FanPerformanceRepo_GetDashboardCounts_CountsPendingCorrectly()
    {
        var member = SampleMember();
        var repo = new InMemoryFanPerformanceSubmissionRepository(id => id == member.Id ? member : null);
        var utcNow = DateTimeOffset.UtcNow;

        await repo.CreateAsync(SampleFanPerformanceSubmission(member.Id));
        await repo.CreateAsync(SampleFanPerformanceSubmission(member.Id));

        var counts = await repo.GetDashboardCountsAsync(utcNow);

        Assert.Equal(2, counts.Pending);
        Assert.Equal(2, counts.ReceivedToday);
        Assert.Equal(2, counts.ReceivedThisWeek);
        Assert.Equal(0, counts.StalePendingCount);
    }

    [Fact]
    public async Task FanPerformanceRepo_GetDashboardCounts_CountsStaleOpenItems_WithDefaultSevenDays()
    {
        var member = SampleMember();
        var repo = new InMemoryFanPerformanceSubmissionRepository(id => id == member.Id ? member : null);
        var utcNow = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        var fresh = await repo.CreateAsync(SampleFanPerformanceSubmission(member.Id));
        var stalePending = await repo.CreateAsync(SampleFanPerformanceSubmission(member.Id));
        var staleNeedsInfo = await repo.CreateAsync(SampleFanPerformanceSubmission(member.Id));
        var staleApproved = await repo.CreateAsync(SampleFanPerformanceSubmission(member.Id));

        repo.SetTimestamps(fresh.Id, utcNow.AddDays(-6), null);
        repo.SetTimestamps(stalePending.Id, utcNow.AddDays(-7), null);
        await repo.UpdateStatusAsync(
            staleNeedsInfo.Id,
            FanPerformanceSubmissionStatus.NeedsInfo,
            "admin@test.local",
            "Please name the song",
            null);
        repo.SetTimestamps(staleNeedsInfo.Id, utcNow.AddDays(-10), utcNow.AddDays(-1));
        await repo.PromoteAsync(staleApproved.Id, 42, "admin@test.local", "internal");
        repo.SetTimestamps(staleApproved.Id, utcNow.AddDays(-20), utcNow.AddDays(-19));

        var counts = await repo.GetDashboardCountsAsync(utcNow);

        Assert.Equal(3, counts.Pending);
        Assert.Equal(2, counts.StalePendingCount);
        Assert.Equal(10, counts.OldestOpenAgeDays);

        var fallback = await repo.GetDashboardCountsAsync(utcNow, staleAfterDays: 0);
        Assert.Equal(counts.StalePendingCount, fallback.StalePendingCount);
        Assert.Equal(counts.OldestOpenAgeDays, fallback.OldestOpenAgeDays);
    }

    // ── SubmissionQueueStats helper ─────────────────────────────────────────

    [Fact]
    public void SubmissionQueueStats_Totals_AggregateAcrossTypes()
    {
        var counts = new SubmissionTypeCounts(
            Pending: 3, ReceivedToday: 1, ReceivedThisWeek: 2,
            ApprovedLast30Days: 4, RejectedLast30Days: 2, StillPendingFromLast30Days: 1);

        var stats = new SubmissionQueueStats(
            counts,
            counts,
            counts,
            new FanPerformanceDashboardCounts(counts, 0, null),
            []);

        Assert.Equal(16, stats.TotalApprovedLast30Days);
        Assert.Equal(8, stats.TotalRejectedLast30Days);
        Assert.Equal(4, stats.TotalStillPendingLast30Days);
        Assert.Equal(28, stats.TotalLast30Days);
    }

    // ── Dashboard page integration ──────────────────────────────────────────

    [Fact]
    public async Task AdminDashboard_RendersSubmissionQueueSection_WithEmptyData()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Email", AdminEmail);

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Submission queue", body);
        Assert.Contains("Photos", body);
        Assert.Contains("News suggestions", body);
        Assert.Contains("Articles", body);
        Assert.Contains("Trivia suggestions", body);
        Assert.Contains("Quiz question suggestions", body);
        Assert.Contains("/admin/photo-submissions", body);
        Assert.Contains("/admin/news-suggestions", body);
        Assert.Contains("/admin/articles", body);
        Assert.Contains("/admin/trivia-submissions", body);
        Assert.Contains("/admin/quiz-question-submissions", body);
        Assert.Contains("Fan performances", body);
        Assert.Contains("/admin/fan-performance-submissions", body);
        Assert.Equal(
            [
                "Help requests",
                "Reported messages",
                "Reported forum posts",
                "Fan performance reports",
                "Photos",
                "News suggestions",
                "Articles",
                "Trivia suggestions",
                "Quiz question suggestions",
                "Fan performances",
            ],
            ExtractQueueTileLabels(body));
        Assert.DoesNotContain("stale", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminDashboard_ShowsFanPerformanceStaleCount_WhenOpenItemOlderThanSevenDays()
    {
        var member = SampleMember();
        var repository = new InMemoryFanPerformanceSubmissionRepository(id => id == member.Id ? member : null);
        var stale = await repository.CreateAsync(SampleFanPerformanceSubmission(member.Id));
        repository.SetTimestamps(stale.Id, DateTimeOffset.UtcNow.AddDays(-8), null);

        var staleFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFanPerformanceSubmissionRepository>();
                services.AddSingleton<IFanPerformanceSubmissionRepository>(repository);
            });
        });

        var client = staleFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Email", AdminEmail);

        var body = await client.GetStringAsync("/admin");

        Assert.Contains("Fan performances", body);
        Assert.Contains("/admin/fan-performance-submissions", body);
        Assert.Contains("1 stale", body);
        Assert.Contains("oldest waiting 8 days", body);
    }

    [Fact]
    public async Task AdminDashboard_ShowsApprovalRateEmpty_WhenNoSubmissions()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Email", AdminEmail);

        var response = await client.GetAsync("/admin");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("No submissions in the last 30 days", body);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ExtractQueueTileLabels(string html) =>
        System.Text.RegularExpressions.Regex
            .Matches(html, """class="admin-dashboard__queue-tile-label">([^<]+)</span>""")
            .Select(match => match.Groups[1].Value)
            .ToArray();

    private static MemberAccount SampleMember() =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = "contributor@example.com",
            DisplayName = "Sample Contributor",
        };

    private static NewPhotoSubmission SamplePhotoSubmission(Guid memberId) =>
        new(
            Id: Guid.NewGuid(),
            SubmitterMemberId: memberId,
            Title: "Test Photo",
            Description: null,
            SuggestedCategory: "Queen",
            ApproximateYear: null,
            ApproximateDate: null,
            BlobPath: "originals/test.jpg",
            WebOptimizedBlobPath: "web/test.webp",
            ThumbnailBlobPath: "thumbs/test.webp",
            OriginalFileName: "test.jpg",
            FileSizeBytes: 1024,
            MimeType: "image/jpeg",
            ImageWidthPx: 800,
            ImageHeightPx: 600);

    private static NewsSuggestion SampleNewsSuggestion(Guid memberId, string url = "https://example.com/queen-news") =>
        new(
            Id: Guid.Empty,
            SubmitterMemberId: memberId,
            Url: url,
            UrlHash: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(url))),
            Title: "Sample News",
            Notes: null,
            Status: NewsSuggestionStatus.Pending,
            SubmittedAt: DateTimeOffset.UtcNow,
            ReviewedAt: null,
            ReviewerEmail: null,
            ReviewNotes: null,
            PromotedNewsId: null,
            DuplicateCandidateId: null,
            SubmitterDisplayName: null,
            SubmitterEmail: null);

    private static NewFanPerformanceSubmission SampleFanPerformanceSubmission(Guid memberId) =>
        new(
            memberId,
            "Test cover",
            "Reaching Out",
            "Sample Contributor",
            null,
            "members/test/cover.mp3",
            "cover.mp3",
            2048,
            "audio/mpeg",
            120,
            DateTimeOffset.UtcNow,
            FanPerformanceSubmissionRights.DeclarationVersion);

    private static ArticleSubmissionDraft SampleArticleDraft(Guid memberId, string title = "Test Article") =>
        new(
            Id: null,
            AuthorMemberId: memberId,
            Title: title,
            Excerpt: "A test excerpt.",
            Body: new string('x', 350),
            CoverImageBlobPath: null,
            Tags: null);
}
