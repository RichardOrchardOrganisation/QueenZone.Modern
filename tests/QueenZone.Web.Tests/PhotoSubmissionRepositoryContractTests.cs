using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class PhotoSubmissionRepositoryContractTests
{
    protected abstract IPhotoSubmissionRepository Repository { get; }
    protected abstract Task SeedMemberAsync(MemberAccount member);
    private const string Editor = "editor@example.test";

    private async Task<Guid> MemberAsync(string name = "Fan")
    {
        var id = Guid.NewGuid();
        await SeedMemberAsync(new MemberAccount
        {
            Id = id,
            DisplayName = name,
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@EXAMPLE.TEST",
            CreatedAt = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        });
        return id;
    }

    private Task<PhotoSubmission> CreateAsync(Guid member, Guid? id = null) => Repository.CreateAsync(new(
        member, "Wembley", "Stage", "Brian May", 1986, new DateOnly(1986, 7, 12),
        "original.jpg", "web.jpg", "thumb.jpg", "upload.jpg", 1024, "image/jpeg", 1920, 1080, id));

    private Task<PhotoSubmission?> StatusAsync(Guid id, string status, string? reason = null, string? category = null) =>
        Repository.UpdateStatusAsync(id, status, Editor, "Checked", reason, category);

    [Fact]
    public async Task Create_and_read_preserve_submission_and_member_identity()
    {
        var member = await MemberAsync();
        var id = Guid.NewGuid();
        var created = await CreateAsync(member, id);
        Assert.Equal(id, created.Id);
        Assert.Equal(PhotoSubmissionStatus.Pending, created.Status);
        Assert.NotEqual(default, created.SubmittedAt);
        var loaded = await Repository.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Wembley", loaded.Title);
        Assert.Equal("Brian May", loaded.SuggestedCategory);
        Assert.Equal(member, loaded.SubmitterMemberId);
        Assert.Equal("Fan", loaded.SubmitterDisplayName);
        Assert.Equal($"{member}@example.test", loaded.SubmitterEmail);
        Assert.Equal(PhotoSubmissionStatus.Pending, loaded.Status);
        Assert.NotEqual(Guid.Empty, (await CreateAsync(member)).Id);
        Assert.Null(await Repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Pending_and_member_pages_filter_order_and_clamp()
    {
        var member = await MemberAsync();
        var other = await MemberAsync("Other");
        var pending = await CreateAsync(member);
        var review = await CreateAsync(member);
        await StatusAsync(review.Id, PhotoSubmissionStatus.UnderReview);
        var info = await CreateAsync(other);
        await StatusAsync(info.Id, PhotoSubmissionStatus.NeedsInfo);
        var approved = await CreateAsync(member);
        await StatusAsync(approved.Id, PhotoSubmissionStatus.Approved);
        var rejected = await CreateAsync(member);
        await StatusAsync(rejected.Id, PhotoSubmissionStatus.Rejected, "Not usable");
        var open = new[] { pending, review, info }.OrderByDescending(p => p.SubmittedAt).ThenBy(p => p.Id).ToList();
        Assert.Equal(open.Select(p => p.Id), (await Repository.GetPendingAsync(1, 100)).Select(p => p.Id));
        Assert.Equal(open[0].Id, Assert.Single(await Repository.GetPendingAsync(0, 0)).Id);
        Assert.Equal(open[1].Id, Assert.Single(await Repository.GetPendingAsync(2, 1)).Id);
        var mine = new[] { pending, review, approved, rejected }.OrderByDescending(p => p.SubmittedAt).ThenBy(p => p.Id).ToList();
        var page = await Repository.GetBySubmitterAsync(member, 1, 2);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(mine.Take(2).Select(p => p.Id), page.Items.Select(p => p.Id));
        Assert.Equal(mine.Skip(2).Select(p => p.Id), (await Repository.GetBySubmitterAsync(member, 2, 2)).Items.Select(p => p.Id));
        Assert.Equal(mine[0].Id, Assert.Single((await Repository.GetBySubmitterAsync(member, 0, 0)).Items).Id);
        Assert.Empty((await Repository.GetBySubmitterAsync(Guid.NewGuid())).Items);
    }

    [Fact]
    public async Task Queue_page_size_is_capped_at_one_hundred()
    {
        var member = await MemberAsync();
        for (var i = 0; i < 101; i++)
        {
            await CreateAsync(member);
        }
        Assert.Equal(100, (await Repository.GetPendingAsync(1, 999)).Count);
        Assert.Single(await Repository.GetPendingAsync(2, 999));
        var mine = await Repository.GetBySubmitterAsync(member, 1, 999);
        Assert.Equal(101, mine.TotalCount);
        Assert.Equal(100, mine.Items.Count);
    }

    [Fact]
    public async Task Status_transitions_persist_and_reject_invalid_decisions()
    {
        var member = await MemberAsync();
        var submission = await CreateAsync(member);
        Assert.Equal(PhotoSubmissionStatus.UnderReview, (await StatusAsync(submission.Id, PhotoSubmissionStatus.UnderReview))!.Status);
        Assert.Equal(PhotoSubmissionStatus.NeedsInfo, (await StatusAsync(submission.Id, PhotoSubmissionStatus.NeedsInfo))!.Status);
        await StatusAsync(submission.Id, PhotoSubmissionStatus.Approved, category: "Queen");
        var approved = (await Repository.GetByIdAsync(submission.Id))!;
        Assert.Equal(PhotoSubmissionStatus.Approved, approved.Status);
        Assert.Equal("Queen", approved.ApprovedCategory);
        Assert.Equal(Editor, approved.ReviewerEmail);
        Assert.NotNull(approved.ReviewedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => StatusAsync(submission.Id, PhotoSubmissionStatus.UnderReview));
        var fallback = await CreateAsync(member);
        await StatusAsync(fallback.Id, PhotoSubmissionStatus.Approved);
        Assert.Equal("Brian May", (await Repository.GetByIdAsync(fallback.Id))!.ApprovedCategory);
        var rejected = await CreateAsync(member);
        await StatusAsync(rejected.Id, PhotoSubmissionStatus.Rejected, "Too blurred");
        var loaded = (await Repository.GetByIdAsync(rejected.Id))!;
        Assert.Equal(PhotoSubmissionStatus.Rejected, loaded.Status);
        Assert.Equal("Too blurred", loaded.RejectionReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => StatusAsync(rejected.Id, PhotoSubmissionStatus.Approved));
        Assert.Null(await StatusAsync(Guid.NewGuid(), PhotoSubmissionStatus.UnderReview));
    }

    [Fact]
    public async Task Reject_without_reason_throws_and_leaves_persisted_status_pending()
    {
        var row = await CreateAsync(await MemberAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StatusAsync(row.Id, PhotoSubmissionStatus.Rejected));
        Assert.Equal("A rejection reason is required.", error.Message);
        Assert.Equal(PhotoSubmissionStatus.Pending, (await Repository.GetByIdAsync(row.Id))!.Status);
    }

    [Fact]
    public async Task Promotion_records_published_identity_and_obeys_transitions()
    {
        var submission = await CreateAsync(await MemberAsync());
        await StatusAsync(submission.Id, PhotoSubmissionStatus.UnderReview);
        await Repository.PromoteAsync(submission.Id, 123, "Queen", Editor, "Published");
        var loaded = (await Repository.GetByIdAsync(submission.Id))!;
        Assert.Equal(PhotoSubmissionStatus.Approved, loaded.Status);
        Assert.Equal(123, loaded.PromotedPicId);
        Assert.Equal("Queen", loaded.ApprovedCategory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.PromoteAsync(submission.Id, 456, "Brian May", Editor, null));
        Assert.Equal(123, (await Repository.GetByIdAsync(submission.Id))!.PromotedPicId);
        Assert.Null(await Repository.PromoteAsync(Guid.NewGuid(), 123, "Queen", Editor, null));
    }

    [Fact]
    public async Task Dashboard_counts_reviewable_and_decided_rows_using_submission_dates()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(SubmissionTypeCounts.Empty, await Repository.GetDashboardCountsAsync(now));
        var member = await MemberAsync();
        var rows = new List<PhotoSubmission>();
        foreach (var status in new[] { PhotoSubmissionStatus.Pending, PhotoSubmissionStatus.UnderReview, PhotoSubmissionStatus.NeedsInfo,
                     PhotoSubmissionStatus.Approved, PhotoSubmissionStatus.Rejected })
        {
            var row = await CreateAsync(member);
            if (status != PhotoSubmissionStatus.Pending)
            {
                row = (await StatusAsync(row.Id, status, "Reason"))!;
            }
            rows.Add(row);
        }
        var today = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new SubmissionTypeCounts(3,
            rows.Count(r => r.SubmittedAt >= today), rows.Count(r => r.SubmittedAt >= today.AddDays(-6)),
            rows.Count(r => r.SubmittedAt >= now.AddDays(-30) && r.Status == PhotoSubmissionStatus.Approved),
            rows.Count(r => r.SubmittedAt >= now.AddDays(-30) && r.Status == PhotoSubmissionStatus.Rejected),
            rows.Count(r => r.SubmittedAt >= now.AddDays(-30) && r.Status is PhotoSubmissionStatus.Pending or PhotoSubmissionStatus.UnderReview or PhotoSubmissionStatus.NeedsInfo)),
            await Repository.GetDashboardCountsAsync(now));
        var contributors = await Repository.GetTopContributorsThisMonthAsync(today, 1);
        Assert.Equal(rows.Count(r => r.SubmittedAt >= today), contributors.Sum(c => c.Count));
        Assert.All(contributors, c => { Assert.Equal(member, c.MemberId); Assert.Equal("Fan", c.DisplayName); });
        Assert.Empty(await Repository.GetTopContributorsThisMonthAsync(new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero), 10));
    }
}

public sealed class InMemoryPhotoSubmissionRepositoryContractTests : PhotoSubmissionRepositoryContractTests
{
    private readonly Dictionary<Guid, MemberAccount> members = [];
    protected override IPhotoSubmissionRepository Repository { get; }
    public InMemoryPhotoSubmissionRepositoryContractTests() => Repository = new InMemoryPhotoSubmissionRepository(id => members.GetValueOrDefault(id));
    protected override Task SeedMemberAsync(MemberAccount member)
    {
        members.Add(member.Id, member);
        return Task.CompletedTask;
    }
}

public sealed class EfPhotoSubmissionRepositoryContractTests : PhotoSubmissionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext db;
    protected override IPhotoSubmissionRepository Repository { get; }
    public EfPhotoSubmissionRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        Repository = new EfPhotoSubmissionRepository(db);
    }
    protected override async Task SeedMemberAsync(MemberAccount member)
    {
        db.MemberAccounts.Add(member);
        await db.SaveChangesAsync();
    }
    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }
}
