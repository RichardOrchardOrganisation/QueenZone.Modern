using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class FanPerformanceSubmissionRepositoryContractTests
{
    protected abstract IFanPerformanceSubmissionRepository Repository { get; }
    protected abstract Task SetTimestampsAsync(Guid id, DateTimeOffset submittedAt, DateTimeOffset? reviewedAt);
    protected static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid OtherMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static MemberAccount Member(Guid id) => new() { Id = id, DisplayName = id == MemberId ? "Fan" : "Other", Email = $"{id:N}@example.test", NormalizedEmail = $"{id:N}@EXAMPLE.TEST", CreatedAt = new DateTime(2026, 9, 1) };
    protected static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private Task<FanPerformanceSubmission> Create(Guid? member = null) => Repository.CreateAsync(new(
        member ?? MemberId, "Title", "Somebody to Love", "Band", "Description", "pending/song.mp3", "song.mp3", 1024, "audio/mpeg", 120, Now,
        FanPerformanceSubmissionRights.DeclarationVersion));

    [Fact]
    public async Task Create_AndReview_RoundTripFieldsAndAudit()
    {
        var row = await Create();
        row = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, row.Status);
        Assert.Equal("Fan", row.SubmitterDisplayName);
        Assert.Equal("Title", row.Title);
        Assert.Equal("Somebody to Love", row.CoveredSong);
        Assert.Equal("Band", row.PerformedBy);
        Assert.Equal("pending/song.mp3", row.BlobPath);
        Assert.Equal(1024, row.FileSizeBytes);
        Assert.Equal(120, row.DurationSeconds);
        Assert.Equal(Now, row.RightsDeclaredAt);
        Assert.NotEmpty(await Repository.GetAuditLogsAsync(row.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.UpdateStatusAsync(row.Id, FanPerformanceSubmissionStatus.Rejected, "editor", null, " "));
        Assert.Equal(FanPerformanceSubmissionStatus.Pending, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Repository.UpdateStatusAsync(row.Id, FanPerformanceSubmissionStatus.UnderReview, "editor", "checking", null);
        Assert.Contains(await Repository.GetAuditLogsAsync(row.Id), entry => entry.Action == FanPerformanceSubmissionStatus.UnderReview);
        await Repository.UpdateReviewMetadataAsync(row.Id, new("Edited", "New Band", "New description", "Liar"), "editor");
        row = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal("Edited", row.Title);
        Assert.Equal("New Band", row.PerformedBy);
        Assert.Equal("New description", row.Description);
        Assert.Equal("Liar", row.CoveredSong);
        await Repository.PromoteAsync(row.Id, 42, "editor", "approved");
        row = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(FanPerformanceSubmissionStatus.Approved, row.Status);
        Assert.Equal(42, row.PromotedStageId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.UpdateStatusAsync(row.Id, FanPerformanceSubmissionStatus.Pending, "editor", null, null));
    }

    [Fact]
    public async Task Queues_FilterStatuses_PageAndClamp()
    {
        var first = await Create();
        var second = await Create();
        await Repository.UpdateStatusAsync(second.Id, FanPerformanceSubmissionStatus.UnderReview, "editor", null, null);
        var third = await Create(OtherMemberId);
        await Repository.UpdateStatusAsync(third.Id, FanPerformanceSubmissionStatus.NeedsInfo, "editor", "More details", null);
        var rejected = await Create();
        await Repository.UpdateStatusAsync(rejected.Id, FanPerformanceSubmissionStatus.Rejected, "editor", null, "Reason");
        var withdrawn = await Create();
        await Repository.UpdateStatusAsync(withdrawn.Id, FanPerformanceSubmissionStatus.Withdrawn, "editor", null, null);
        var approved = await Create();
        await Repository.PromoteAsync(approved.Id, 1, "editor", null);
        Assert.Equal(new[] { third.Id, second.Id, first.Id }, (await Repository.GetPendingAsync(1, 10)).Select(row => row.Id));
        Assert.Equal(third.Id, Assert.Single(await Repository.GetPendingAsync(0, 0)).Id);
        Assert.Equal(second.Id, Assert.Single(await Repository.GetPendingAsync(2, 1)).Id);
        var page = await Repository.GetBySubmitterAsync(MemberId, 2, 1);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(withdrawn.Id, Assert.Single(page.Items).Id);
        Assert.Equal(3, (await Repository.GetDashboardCountsAsync(Now)).Pending);
        Assert.Empty(await Repository.GetTopContributorsThisMonthAsync(new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero), 10));
    }

    [Fact]
    public async Task Credits_UseNewestApprovedContributor_AndPurgeOnlyClosedBlobs()
    {
        var first = await Create();
        await Repository.PromoteAsync(first.Id, 42, "editor", null);
        var second = await Create(OtherMemberId);
        await Repository.PromoteAsync(second.Id, 42, "editor", null);
        var credits = await Repository.GetApprovedContributorCreditsAsync([42, 999]);
        Assert.Single(credits);
        Assert.Equal(OtherMemberId, credits[42].MemberId);
        Assert.Empty(await Repository.GetApprovedContributorCreditsAsync([]));
        var pending = await Create();
        var rejected = await Create();
        var closed = await Repository.UpdateStatusAsync(rejected.Id, FanPerformanceSubmissionStatus.Rejected, "editor", null, "Reason");
        var cutoff = closed!.ReviewedAt!.Value;
        Assert.Equal(rejected.Id, Assert.Single(await Repository.GetEligibleForPendingBlobPurgeAsync(cutoff)).Id);
        Assert.Empty(await Repository.GetEligibleForPendingBlobPurgeAsync(cutoff.AddTicks(-1)));
        await Repository.ClearPendingBlobPathAsync(rejected.Id);
        Assert.Equal(string.Empty, (await Repository.GetByIdAsync(rejected.Id))!.BlobPath);
        Assert.Empty(await Repository.GetEligibleForPendingBlobPurgeAsync(cutoff));
        Assert.Equal("pending/song.mp3", (await Repository.GetByIdAsync(pending.Id))!.BlobPath);
    }

    [Fact]
    public async Task MissingRows_ReturnNullOrEmpty()
    {
        var id = Guid.NewGuid();
        Assert.Null(await Repository.GetByIdAsync(id));
        Assert.Null(await Repository.UpdateStatusAsync(id, FanPerformanceSubmissionStatus.Rejected, "editor", null, "Reason"));
        Assert.Null(await Repository.UpdateReviewMetadataAsync(id, new("Title", null, null, null), "editor"));
        Assert.Null(await Repository.PromoteAsync(id, 42, "editor", null));
        Assert.Empty(await Repository.GetAuditLogsAsync(id));
        await Repository.ClearPendingBlobPathAsync(id);
    }
    [Fact]
    public async Task Queues_CapPageSizeAtOneHundred()
    {
        for (var index = 0; index < 101; index++)
        {
            await Create();
        }
        Assert.Equal(100, (await Repository.GetPendingAsync(1, 999)).Count);
        Assert.Single(await Repository.GetPendingAsync(2, 999));
        var page = await Repository.GetBySubmitterAsync(MemberId, 1, 999);
        Assert.Equal(101, page.TotalCount);
        Assert.Equal(100, page.Items.Count);
        Assert.Single((await Repository.GetBySubmitterAsync(MemberId, 2, 999)).Items);
    }

    [Fact]
    public async Task DashboardAndContributors_UseSuppliedDateWindows()
    {
        var stale = await Create();
        await SetTimestampsAsync(stale.Id, Now.AddDays(-10), null);
        var recent = await Create();
        await SetTimestampsAsync(recent.Id, Now, null);
        var approved = await Create(OtherMemberId);
        await Repository.PromoteAsync(approved.Id, 42, "editor", null);
        await SetTimestampsAsync(approved.Id, Now.AddDays(-2), Now);
        var rejected = await Create();
        await Repository.UpdateStatusAsync(rejected.Id, FanPerformanceSubmissionStatus.Rejected, "editor", null, "Reason");
        await SetTimestampsAsync(rejected.Id, Now.AddDays(-31), Now);
        var counts = await Repository.GetDashboardCountsAsync(Now);
        Assert.Equal(new SubmissionTypeCounts(2, 1, 2, 1, 0, 2), counts.Queue);
        Assert.Equal(1, counts.StalePendingCount);
        Assert.Equal(10, counts.OldestOpenAgeDays);
        Assert.Equal(new SubmissionContributor(MemberId, "Fan", 2), Assert.Single(await Repository.GetTopContributorsThisMonthAsync(Now.AddDays(-30), 1)));
    }

    [Fact]
    public async Task Purge_IncludesWithdrawn_AndUsesFixedCutoffInclusively()
    {
        var withdrawn = await Create();
        await Repository.UpdateStatusAsync(withdrawn.Id, FanPerformanceSubmissionStatus.Withdrawn, "editor", null, null);
        await SetTimestampsAsync(withdrawn.Id, Now.AddDays(-30), Now);
        Assert.Empty(await Repository.GetEligibleForPendingBlobPurgeAsync(Now.AddTicks(-1)));
        Assert.Equal(withdrawn.Id, Assert.Single(await Repository.GetEligibleForPendingBlobPurgeAsync(Now)).Id);
        await Repository.ClearPendingBlobPathAsync(withdrawn.Id);
        Assert.Empty(await Repository.GetEligibleForPendingBlobPurgeAsync(Now));
    }

}

public sealed class InMemoryFanPerformanceSubmissionRepositoryContractTests : FanPerformanceSubmissionRepositoryContractTests
{
    private readonly InMemoryFanPerformanceSubmissionRepository repository = new(id => Member(id));
    protected override IFanPerformanceSubmissionRepository Repository => repository;
    protected override Task SetTimestampsAsync(Guid id, DateTimeOffset submittedAt, DateTimeOffset? reviewedAt)
    {
        repository.SetTimestamps(id, submittedAt, reviewedAt);
        return Task.CompletedTask;
    }
}

public sealed class EfFanPerformanceSubmissionRepositoryContractTests : FanPerformanceSubmissionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override IFanPerformanceSubmissionRepository Repository { get; }

    public EfFanPerformanceSubmissionRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.MemberAccounts.AddRange(Member(MemberId), Member(OtherMemberId));
        context.SaveChanges();
        Repository = new EfFanPerformanceSubmissionRepository(context);
    }

    protected override async Task SetTimestampsAsync(Guid id, DateTimeOffset submittedAt, DateTimeOffset? reviewedAt)
    {
        var row = await context.FanPerformanceSubmissions.SingleAsync(row => row.Id == id);
        row.SubmittedAt = submittedAt;
        row.ReviewedAt = reviewedAt;
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
