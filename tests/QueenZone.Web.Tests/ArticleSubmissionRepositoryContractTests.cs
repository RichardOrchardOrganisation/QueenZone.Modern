using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class ArticleSubmissionRepositoryContractTests
{
    protected abstract IArticleSubmissionRepository Repository { get; }
    protected static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid OtherMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static MemberAccount Member(Guid id) => new() { Id = id, DisplayName = id == MemberId ? "Fan" : "Other", Email = $"{id:N}@example.test", NormalizedEmail = $"{id:N}@EXAMPLE.TEST", CreatedAt = new DateTime(2026, 9, 1) };
    protected static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private ArticleSubmissionDraft Draft(Guid? id = null, Guid? member = null, string? body = null) =>
        new(id, member ?? MemberId, "Article", "Excerpt", body ?? new string('a', 300), "cover.webp", "Queen");

    [Fact]
    public async Task Draft_Updates_AndSubmissionEnforcesOwnershipAndMinimumBody()
    {
        var row = await Repository.UpsertDraftAsync(Draft(body: "short"));
        Assert.Equal(ArticleSubmissionStatus.Draft, row.Status);
        Assert.Equal("Fan", (await Repository.GetByIdAsync(row.Id))!.AuthorDisplayName);
        Assert.Null(await Repository.SubmitForReviewAsync(row.Id, OtherMemberId));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SubmitForReviewAsync(row.Id, MemberId));
        Assert.Equal("Article body must contain at least 300 characters of visible text (currently 5).", error.Message);
        Assert.Equal(ArticleSubmissionStatus.Draft, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Repository.UpsertDraftAsync(Draft(row.Id) with { Title = "Edited", CoverImageBlobPath = null });
        row = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal("Edited", row.Title);
        Assert.Equal("cover.webp", row.CoverImageBlobPath);
        var submitted = await Repository.SubmitForReviewAsync(row.Id, MemberId);
        Assert.Equal(ArticleSubmissionStatus.Submitted, submitted!.Status);
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Null(await Repository.SubmitForReviewAsync(row.Id, MemberId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.UpsertDraftAsync(Draft(row.Id)));
        await Repository.UpdateStatusAsync(row.Id, ArticleSubmissionStatus.RequiresRevision, "editor", "revise", null);
        await Repository.UpsertDraftAsync(Draft(row.Id));
        Assert.Equal(ArticleSubmissionStatus.Submitted, (await Repository.SubmitForReviewAsync(row.Id, MemberId))!.Status);
        Assert.Null(await Repository.GetByIdAsync(Guid.NewGuid()));
        Assert.Null(await Repository.SubmitForReviewAsync(Guid.NewGuid(), MemberId));
        Assert.Null(await Repository.UpdateStatusAsync(Guid.NewGuid(), ArticleSubmissionStatus.Rejected, "editor", null, "reason"));
    }

    [Fact]
    public async Task Publication_PersistsEditorialFields_AndFiltersLists()
    {
        var row = await Repository.UpsertDraftAsync(Draft());
        await Repository.SubmitForReviewAsync(row.Id, MemberId);
        await Repository.UpdateStatusAsync(row.Id, ArticleSubmissionStatus.ApprovedForPublishing, "editor", "approved", null);
        Assert.Equal(row.Id, Assert.Single(await Repository.GetPendingAsync(1, 10)).Id);
        await Repository.UpdateStatusAsync(row.Id, ArticleSubmissionStatus.Published, "editor", "published", null,
            new("custom-slug", "New excerpt", "Live", "new.webp"));
        row = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(ArticleSubmissionStatus.Published, row.Status);
        Assert.Equal("custom-slug", row.Slug);
        Assert.Equal("New excerpt", row.Excerpt);
        Assert.Equal("Live", row.Tags);
        Assert.Equal("new.webp", row.CoverImageBlobPath);
        Assert.NotNull(row.PublishedAt);
        Assert.Equal(row.Id, Assert.Single(await Repository.GetPublishedAsync()).Id);
        Assert.Empty(await Repository.GetPendingAsync(1, 10));
        Assert.Equal(row.Id, Assert.Single((await Repository.GetDraftsForMemberAsync(MemberId)).Items).Id);
    }

    [Fact]
    public async Task Queues_FilterByOwnerAndReviewability_AndPage()
    {
        var first = await Repository.UpsertDraftAsync(Draft());
        await Repository.SubmitForReviewAsync(first.Id, MemberId);
        var second = await Repository.UpsertDraftAsync(Draft());
        await Repository.SubmitForReviewAsync(second.Id, MemberId);
        await Repository.UpdateStatusAsync(second.Id, ArticleSubmissionStatus.UnderReview, "editor", null, null);
        await Repository.UpsertDraftAsync(Draft(member: OtherMemberId));
        Assert.Equal(second.Id, Assert.Single(await Repository.GetPendingAsync(0, 0)).Id);
        Assert.Equal(first.Id, Assert.Single(await Repository.GetPendingAsync(2, 1)).Id);
        var page = await Repository.GetDraftsForMemberAsync(MemberId, 2, 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(2, (await Repository.GetDashboardCountsAsync(Now)).Pending);
        Assert.Empty(await Repository.GetTopContributorsThisMonthAsync(new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero), 10));
    }
    [Fact]
    public async Task Queue_ReportsVisibleWordCount()
    {
        var row = await Repository.UpsertDraftAsync(Draft(body: "<p>" + new string('a', 300) + "</p>"));
        await Repository.SubmitForReviewAsync(row.Id, MemberId);
        Assert.Equal(1, Assert.Single(await Repository.GetPendingAsync(1, 10)).WordCountEstimate);
    }

    [Fact]
    public async Task Drafts_WithEqualUnsubmittedDates_OrderById()
    {
        var ids = new List<Guid>();
        for (var index = 0; index < 8; index++)
        {
            ids.Add((await Repository.UpsertDraftAsync(Draft())).Id);
        }
        Assert.Equal(ids.Order(), (await Repository.GetDraftsForMemberAsync(MemberId)).Items.Select(row => row.Id));
    }

    [Fact]
    public async Task Queues_CapPageSizeAtOneHundred()
    {
        for (var index = 0; index < 101; index++)
        {
            var row = await Repository.UpsertDraftAsync(Draft());
            await Repository.SubmitForReviewAsync(row.Id, MemberId);
        }
        Assert.Equal(100, (await Repository.GetPendingAsync(1, 999)).Count);
        Assert.Single(await Repository.GetPendingAsync(2, 999));
        var page = await Repository.GetDraftsForMemberAsync(MemberId, 1, 999);
        Assert.Equal(101, page.TotalCount);
        Assert.Equal(100, page.Items.Count);
        Assert.Single((await Repository.GetDraftsForMemberAsync(MemberId, 2, 999)).Items);
        Assert.Single((await Repository.GetDraftsForMemberAsync(MemberId, 0, 0)).Items);
    }

    [Fact]
    public async Task RejectionAndRevision_LeaveReviewAndPublishedLists()
    {
        var rejected = await Repository.UpsertDraftAsync(Draft());
        await Repository.SubmitForReviewAsync(rejected.Id, MemberId);
        await Repository.UpdateStatusAsync(rejected.Id, ArticleSubmissionStatus.Rejected, "editor", "checked", "Reason");
        var row = (await Repository.GetByIdAsync(rejected.Id))!;
        Assert.Equal(ArticleSubmissionStatus.Rejected, row.Status);
        Assert.Equal("Reason", row.RejectionReason);
        Assert.Equal("editor", row.ReviewerEmail);
        Assert.Equal("checked", row.ReviewNotes);
        Assert.Null(row.PublishedAt);
        var revision = await Repository.UpsertDraftAsync(Draft());
        await Repository.SubmitForReviewAsync(revision.Id, MemberId);
        await Repository.UpdateStatusAsync(revision.Id, ArticleSubmissionStatus.RequiresRevision, "editor", "Revise", null);
        Assert.Empty(await Repository.GetPendingAsync(1, 10));
        Assert.Empty(await Repository.GetPublishedAsync());
        Assert.Equal(2, (await Repository.GetDraftsForMemberAsync(MemberId)).TotalCount);
        var contributors = await Repository.GetTopContributorsThisMonthAsync(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), 1);
        Assert.Equal(new SubmissionContributor(MemberId, "Fan", 2), Assert.Single(contributors));
    }

}

public sealed class InMemoryArticleSubmissionRepositoryContractTests : ArticleSubmissionRepositoryContractTests
{
    protected override IArticleSubmissionRepository Repository { get; } = new InMemoryArticleSubmissionRepository(id => Member(id));
}

public sealed class EfArticleSubmissionRepositoryContractTests : ArticleSubmissionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override IArticleSubmissionRepository Repository { get; }

    public EfArticleSubmissionRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.MemberAccounts.AddRange(Member(MemberId), Member(OtherMemberId));
        context.SaveChanges();
        Repository = new EfArticleSubmissionRepository(context);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
