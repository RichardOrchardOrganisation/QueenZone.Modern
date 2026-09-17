using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfQuizQuestionSubmissionRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfQuizQuestionSubmissionRepository repository;
    private readonly Guid memberId = Guid.NewGuid();

    public EfQuizQuestionSubmissionRepositoryTests()
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
            Email = "quiz-question-ef@example.com",
            NormalizedEmail = "QUIZ-QUESTION-EF@EXAMPLE.COM",
            DisplayName = "EF Quiz Fan",
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();

        repository = new EfQuizQuestionSubmissionRepository(dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private NewQuizQuestionSubmission NewSubmission(string text = "Who was the lead singer of Queen?") =>
        new(
            memberId,
            text,
            [new QuizQuestionSubmissionOptionDraft("Freddie Mercury", true), new QuizQuestionSubmissionOptionDraft("Brian May", false)],
            "From an interview.");

    [Fact]
    public async Task CreateAsync_persists_pending_submission_options_and_audit()
    {
        var created = await repository.CreateAsync(NewSubmission());

        Assert.Equal(QuizQuestionSubmissionStatus.Pending, created.Status);
        Assert.Equal(2, created.Options.Count);

        var loaded = await repository.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("EF Quiz Fan", loaded!.SubmitterDisplayName);
        Assert.Equal("quiz-question-ef@example.com", loaded.SubmitterEmail);

        Assert.Single(dbContext.QuizQuestionSubmissionAuditLogs.Where(log => log.QuizQuestionSubmissionId == created.Id));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_submission()
    {
        var invalid = new NewQuizQuestionSubmission(
            memberId,
            "  ",
            [new QuizQuestionSubmissionOptionDraft("Only one", true)],
            null);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateAsync(invalid));
    }

    [Fact]
    public async Task GetPendingAsync_pages_and_only_returns_pending_rows()
    {
        var first = await repository.CreateAsync(NewSubmission("First pending?"));
        await repository.CreateAsync(NewSubmission("Second pending?"));
        await ApproveAsync(first.Id);

        var pending = await repository.GetPendingAsync(1, 10);

        Assert.Single(pending);
        Assert.Equal("Second pending?", pending[0].QuestionText);
    }

    [Fact]
    public async Task ApproveAsync_applies_edits_and_makes_it_available_in_the_bank()
    {
        var created = await repository.CreateAsync(NewSubmission("Original text?"));

        var edit = new QuizQuestionSubmissionEdit(
            "Edited text?",
            [new QuizQuestionSubmissionOptionDraft("A", true), new QuizQuestionSubmissionOptionDraft("B", false)]);
        var approved = await repository.ApproveAsync(created.Id, edit, "editor@example.test", "fixed wording");

        Assert.Equal(QuizQuestionSubmissionStatus.Approved, approved!.Status);
        Assert.Equal("Edited text?", approved.QuestionText);
        Assert.Contains(await repository.GetApprovedAndAvailableAsync(), item => item.Id == created.Id);
    }

    [Fact]
    public async Task ApproveAsync_returns_null_for_an_unknown_id() =>
        Assert.Null(await repository.ApproveAsync(
            Guid.NewGuid(),
            new QuizQuestionSubmissionEdit("Text", [new QuizQuestionSubmissionOptionDraft("A", true), new QuizQuestionSubmissionOptionDraft("B", false)]),
            "editor@example.test",
            null));

    [Fact]
    public async Task ApproveAsync_rejects_an_invalid_edit_and_leaves_status_pending()
    {
        var created = await repository.CreateAsync(NewSubmission());
        var invalidEdit = new QuizQuestionSubmissionEdit("Edited", [new QuizQuestionSubmissionOptionDraft("Only one", true)]);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.ApproveAsync(created.Id, invalidEdit, "editor@example.test", null));

        Assert.Equal(QuizQuestionSubmissionStatus.Pending, (await repository.GetByIdAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task RejectAsync_requires_a_reason_and_does_not_corrupt_status()
    {
        var created = await repository.CreateAsync(NewSubmission());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(created.Id, "editor@example.test", "  ", null));
        Assert.Equal(QuizQuestionSubmissionStatus.Pending, (await repository.GetByIdAsync(created.Id))!.Status);

        var rejected = await repository.RejectAsync(created.Id, "editor@example.test", "Not accurate.", "internal");
        Assert.Equal(QuizQuestionSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Not accurate.", rejected.RejectionReason);
    }

    [Fact]
    public async Task RejectAsync_returns_null_for_an_unknown_id() =>
        Assert.Null(await repository.RejectAsync(Guid.NewGuid(), "editor@example.test", "Reason.", null));

    [Fact]
    public async Task Cannot_review_a_submission_twice()
    {
        var created = await repository.CreateAsync(NewSubmission());
        await repository.RejectAsync(created.Id, "editor@example.test", "First reason.", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(created.Id, "editor@example.test", "Second reason.", null));
    }

    [Fact]
    public async Task MarkAddedToQuizAsync_removes_it_from_the_bank_and_requires_approval()
    {
        var created = await repository.CreateAsync(NewSubmission());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.MarkAddedToQuizAsync(created.Id, Guid.NewGuid(), "editor@example.test"));

        await ApproveAsync(created.Id);
        var quizId = Guid.NewGuid();
        var updated = await repository.MarkAddedToQuizAsync(created.Id, quizId, "editor@example.test");

        Assert.Equal(quizId, updated!.AddedToQuizId);
        Assert.Empty(await repository.GetApprovedAndAvailableAsync());
    }

    [Fact]
    public async Task MarkAddedToQuizAsync_returns_null_for_an_unknown_id() =>
        Assert.Null(await repository.MarkAddedToQuizAsync(Guid.NewGuid(), Guid.NewGuid(), "editor@example.test"));

    [Fact]
    public async Task GetBySubmitterAsync_returns_only_that_members_submissions()
    {
        var other = Guid.NewGuid();
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = other,
            Email = "other@example.com",
            NormalizedEmail = "OTHER@EXAMPLE.COM",
            DisplayName = "Other Fan",
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        await repository.CreateAsync(NewSubmission("Mine?"));
        await repository.CreateAsync(new NewQuizQuestionSubmission(
            other,
            "Not mine?",
            [new QuizQuestionSubmissionOptionDraft("A", true), new QuizQuestionSubmissionOptionDraft("B", false)],
            null));

        var page = await repository.GetBySubmitterAsync(memberId);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Mine?", page.Items[0].QuestionText);
    }

    [Fact]
    public async Task GetDashboardCountsAsync_reports_pending_and_recent_counts()
    {
        var created = await repository.CreateAsync(NewSubmission());
        await repository.CreateAsync(NewSubmission("Second?"));
        await ApproveAsync(created.Id);

        var counts = await repository.GetDashboardCountsAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, counts.Pending);
        Assert.Equal(1, counts.ApprovedLast30Days);
        Assert.Equal(2, counts.ReceivedToday);
    }

    private async Task ApproveAsync(Guid id)
    {
        var submission = await repository.GetByIdAsync(id);
        var edit = new QuizQuestionSubmissionEdit(
            submission!.QuestionText,
            submission.Options.Select(option => new QuizQuestionSubmissionOptionDraft(option.Text, option.IsCorrect)).ToList());
        await repository.ApproveAsync(id, edit, "editor@example.test", null);
    }
}
