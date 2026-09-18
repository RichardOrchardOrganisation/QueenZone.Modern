using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfTriviaFactSubmissionRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfTriviaFactSubmissionRepository repository;
    private readonly Guid memberId = Guid.NewGuid();

    public EfTriviaFactSubmissionRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = "trivia-ef@example.com",
            NormalizedEmail = "TRIVIA-EF@EXAMPLE.COM",
            DisplayName = "EF Trivia Fan",
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();

        repository = new EfTriviaFactSubmissionRepository(dbContext);
    }

    [Fact]
    public async Task CreateAsync_persists_pending_submission_and_audit()
    {
        var preferredId = Guid.NewGuid();
        var created = await repository.CreateAsync(NewSubmission(preferredId, "John Deacon designed the Deacy Amp.", "Band"));

        Assert.Equal(preferredId, created.Id);
        Assert.Equal(TriviaFactSubmissionStatus.Pending, created.Status);
        Assert.Equal("John Deacon designed the Deacy Amp.", created.Text);

        var loaded = await repository.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("EF Trivia Fan", loaded!.SubmitterDisplayName);
        Assert.Equal("trivia-ef@example.com", loaded.SubmitterEmail);

        Assert.Single(dbContext.TriviaFactSubmissionAuditLogs.Where(log => log.TriviaFactSubmissionId == created.Id));
    }

    [Fact]
    public async Task CreateAsync_generates_id_when_preferred_missing()
    {
        var created = await repository.CreateAsync(NewSubmission(null, "No preferred id", null));
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task GetPendingAsync_returns_newest_first_and_excludes_reviewed()
    {
        var pending = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Pending one", "A"));
        var later = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Pending two", "B"));
        var approved = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Approved", "C"));
        await repository.ApproveAsync(approved.Id, 7, "admin@test.local", "ok");

        var page = await repository.GetPendingAsync(1, 50);
        Assert.Equal(2, page.Count);
        Assert.Equal(later.Id, page[0].Id);
        Assert.Equal(pending.Id, page[1].Id);
        Assert.DoesNotContain(page, item => item.Id == approved.Id);
        Assert.All(page, item => Assert.Equal("EF Trivia Fan", item.SubmitterDisplayName));
    }

    [Fact]
    public async Task GetPendingAsync_clamps_page_and_page_size()
    {
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Only", null));
        var page = await repository.GetPendingAsync(0, 1000);
        Assert.Single(page);
    }

    [Fact]
    public async Task GetBySubmitterAsync_returns_only_owned_rows()
    {
        var otherMember = Guid.NewGuid();
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = otherMember,
            Email = "other-trivia@example.com",
            NormalizedEmail = "OTHER-TRIVIA@EXAMPLE.COM",
            DisplayName = "Other",
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Mine", null));
        await repository.CreateAsync(new NewTriviaFactSubmission(otherMember, "Theirs", null, null, null));

        var mine = await repository.GetBySubmitterAsync(memberId);
        Assert.Single(mine.Items);
        Assert.Equal("Mine", mine.Items[0].Text);
    }

    [Fact]
    public async Task GetPendingAsync_pages_without_earlier_rows()
    {
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Oldest pending", "A"));
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Middle pending", "B"));
        var newest = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Newest pending", "C"));

        var page1 = await repository.GetPendingAsync(1, 2);
        var page2 = await repository.GetPendingAsync(2, 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal(newest.Id, page1[0].Id);
        Assert.Single(page2);
        Assert.Equal("Oldest pending", page2[0].Text);
        Assert.DoesNotContain(page2, item => page1.Any(firstPage => firstPage.Id == item.Id));
    }

    [Fact]
    public async Task GetBySubmitterAsync_pages_without_earlier_rows()
    {
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Fact 0", null));
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Fact 1", null));
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Fact 2", null));

        var page1 = await repository.GetBySubmitterAsync(memberId, page: 1, pageSize: 2);
        var page2 = await repository.GetBySubmitterAsync(memberId, page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("Fact 2", page1.Items[0].Text);
        Assert.Single(page2.Items);
        Assert.Equal("Fact 0", page2.Items[0].Text);
        Assert.DoesNotContain(page2.Items, item => page1.Items.Any(firstPage => firstPage.Id == item.Id));
    }

    [Fact]
    public async Task ApproveAsync_publishes_link_and_writes_audit()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Approve me", "Band"));

        var approved = await repository.ApproveAsync(created.Id, 11, "admin@test.local", "Looks good");

        Assert.Equal(TriviaFactSubmissionStatus.Approved, approved!.Status);
        Assert.Equal(11, approved.PromotedTriviaId);
        Assert.Equal("Looks good", approved.ReviewNotes);
        Assert.Contains(
            dbContext.TriviaFactSubmissionAuditLogs,
            log => log.TriviaFactSubmissionId == created.Id && log.Action == TriviaFactSubmissionStatus.Approved);
    }

    [Fact]
    public async Task RejectAsync_stores_reason_and_internal_notes()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Reject me", null));

        var rejected = await repository.RejectAsync(created.Id, "admin@test.local", "Needs a source", "internal");

        Assert.Equal(TriviaFactSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Needs a source", rejected.RejectionReason);
        Assert.Equal("internal", rejected.ReviewNotes);
        Assert.Contains(
            dbContext.TriviaFactSubmissionAuditLogs,
            log => log.TriviaFactSubmissionId == created.Id && log.Action == TriviaFactSubmissionStatus.Rejected);
    }

    [Fact]
    public async Task ApproveAsync_rejects_illegal_transition()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Once", null));
        await repository.ApproveAsync(created.Id, 1, "admin@test.local", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(created.Id, "admin@test.local", "too late", null));
    }

    [Fact]
    public async Task RejectAsync_requires_a_reason_and_does_not_corrupt_status()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Need reason", null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(created.Id, "admin@test.local", "  ", null));
        Assert.Equal(TriviaFactSubmissionStatus.Pending, (await repository.GetByIdAsync(created.Id))!.Status);

        var rejected = await repository.RejectAsync(created.Id, "admin@test.local", "Needs a source", "internal");
        Assert.Equal(TriviaFactSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Needs a source", rejected.RejectionReason);
        Assert.Equal("internal", rejected.ReviewNotes);
    }

    [Fact]
    public async Task Missing_ids_return_null()
    {
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
        Assert.Null(await repository.ApproveAsync(Guid.NewGuid(), 1, "admin@test.local", null));
        Assert.Null(await repository.RejectAsync(Guid.NewGuid(), "admin@test.local", "Nope", null));
    }

    [Fact]
    public async Task GetDashboardCounts_counts_pending_and_recent()
    {
        var utcNow = DateTimeOffset.UtcNow;
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Pending", null));
        var approved = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Approved", null));
        await repository.ApproveAsync(approved.Id, 3, "admin@test.local", null);
        var rejected = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Rejected", null));
        await repository.RejectAsync(rejected.Id, "admin@test.local", "No", null);

        var counts = await repository.GetDashboardCountsAsync(utcNow);
        Assert.Equal(1, counts.Pending);
        Assert.Equal(3, counts.ReceivedToday);
        Assert.Equal(1, counts.ApprovedLast30Days);
        Assert.Equal(1, counts.RejectedLast30Days);
        Assert.Equal(1, counts.StillPendingFromLast30Days);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private NewTriviaFactSubmission NewSubmission(Guid? id, string text, string? category) =>
        new(memberId, text, category, TriviaDifficulty.Easy, "Note", id);
}
