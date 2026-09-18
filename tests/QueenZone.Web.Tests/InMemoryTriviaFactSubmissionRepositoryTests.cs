using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class InMemoryTriviaFactSubmissionRepositoryTests
{
    private readonly Guid memberId = Guid.NewGuid();
    private readonly InMemoryTriviaFactSubmissionRepository repository;

    public InMemoryTriviaFactSubmissionRepositoryTests()
    {
        repository = new InMemoryTriviaFactSubmissionRepository(_ => new MemberAccount
        {
            Id = memberId,
            Email = "trivia-mem@example.com",
            DisplayName = "Trivia Fan",
        });
    }

    [Fact]
    public async Task CreateAsync_persists_pending_row_and_audit()
    {
        var preferredId = Guid.NewGuid();
        var created = await repository.CreateAsync(NewSubmission(preferredId, "Roger Taylor played drums.", "Band"));

        Assert.Equal(preferredId, created.Id);
        Assert.Equal(TriviaFactSubmissionStatus.Pending, created.Status);
        Assert.Equal("Roger Taylor played drums.", created.Text);
        Assert.Equal("Band", created.Category);
        Assert.Equal("Trivia Fan", created.SubmitterDisplayName);

        var audit = repository.GetAuditLogs(created.Id);
        Assert.Single(audit);
        Assert.Equal("Submitted", audit[0].Action);
    }

    [Fact]
    public async Task CreateAsync_generates_id_and_normalizes_optional_fields()
    {
        var created = await repository.CreateAsync(new NewTriviaFactSubmission(
            memberId,
            "  A fact.  ",
            "  Albums  ",
            "  HARD  ",
            "  From a book.  "));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("A fact.", created.Text);
        Assert.Equal("Albums", created.Category);
        Assert.Equal(TriviaDifficulty.Hard, created.Difficulty);
        Assert.Equal("From a book.", created.SourceNote);
    }

    [Fact]
    public async Task GetPendingAsync_returns_newest_first_and_excludes_reviewed()
    {
        var older = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Older pending", "A"));
        var newer = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Newer pending", "B"));
        var approved = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Approved later", "C"));
        await repository.ApproveAsync(approved.Id, 9, "admin@test.local", "ok");

        var page = await repository.GetPendingAsync(1, 50);
        Assert.Equal(2, page.Count);
        Assert.Equal(newer.Id, page[0].Id);
        Assert.Equal(older.Id, page[1].Id);
        Assert.DoesNotContain(page, item => item.Id == approved.Id);
        Assert.All(page, item => Assert.Equal("Trivia Fan", item.SubmitterDisplayName));
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
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Mine", null));
        await repository.CreateAsync(new NewTriviaFactSubmission(otherMember, "Theirs", null, null, null));

        var mine = await repository.GetBySubmitterAsync(memberId);
        Assert.Single(mine.Items);
        Assert.Equal("Mine", mine.Items[0].Text);
        Assert.Equal(1, mine.TotalCount);
    }

    [Fact]
    public async Task ApproveAsync_and_RejectAsync_write_audit_and_block_second_review()
    {
        var approve = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Approve me", "Band"));
        var reject = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Reject me", "Band"));

        var approved = await repository.ApproveAsync(approve.Id, 42, "admin@test.local", "Looks solid");
        Assert.Equal(TriviaFactSubmissionStatus.Approved, approved!.Status);
        Assert.Equal(42, approved.PromotedTriviaId);
        Assert.Equal("Looks solid", approved.ReviewNotes);

        var rejected = await repository.RejectAsync(reject.Id, "admin@test.local", "Unsourced", "internal only");
        Assert.Equal(TriviaFactSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Unsourced", rejected.RejectionReason);
        Assert.Equal("internal only", rejected.ReviewNotes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ApproveAsync(approve.Id, 43, "admin@test.local", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(reject.Id, "admin@test.local", "again", null));

        Assert.Contains(repository.GetAuditLogs(approve.Id), log => log.Action == TriviaFactSubmissionStatus.Approved);
        Assert.Contains(repository.GetAuditLogs(reject.Id), log => log.Action == TriviaFactSubmissionStatus.Rejected);
    }

    [Fact]
    public async Task RejectAsync_requires_a_reason_and_does_not_corrupt_status()
    {
        var created = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Need a reason", null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(created.Id, "admin@test.local", "   ", null));
        Assert.Equal(TriviaFactSubmissionStatus.Pending, (await repository.GetByIdAsync(created.Id))!.Status);

        var rejected = await repository.RejectAsync(created.Id, "admin@test.local", "Unsourced", "internal only");
        Assert.Equal(TriviaFactSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Unsourced", rejected.RejectionReason);
        Assert.Equal("internal only", rejected.ReviewNotes);
    }

    [Fact]
    public async Task Approve_and_Reject_return_null_when_missing()
    {
        Assert.Null(await repository.ApproveAsync(Guid.NewGuid(), 1, "admin@test.local", null));
        Assert.Null(await repository.RejectAsync(Guid.NewGuid(), "admin@test.local", "Nope", null));
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetDashboardCounts_counts_pending_and_recent()
    {
        var utcNow = DateTimeOffset.UtcNow;
        await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Pending", null));
        var approved = await repository.CreateAsync(NewSubmission(Guid.NewGuid(), "Approved", null));
        await repository.ApproveAsync(approved.Id, 1, "admin@test.local", null);

        var counts = await repository.GetDashboardCountsAsync(utcNow);
        Assert.Equal(1, counts.Pending);
        Assert.Equal(2, counts.ReceivedToday);
        Assert.Equal(1, counts.ApprovedLast30Days);
        Assert.Equal(0, counts.RejectedLast30Days);
        Assert.Equal(1, counts.StillPendingFromLast30Days);
    }

    private NewTriviaFactSubmission NewSubmission(Guid id, string text, string? category) =>
        new(memberId, text, category, TriviaDifficulty.Easy, "Note", id);
}
