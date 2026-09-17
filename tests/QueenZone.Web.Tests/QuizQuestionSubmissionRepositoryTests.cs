using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizQuestionSubmissionRepositoryTests
{
    private readonly InMemoryQuizQuestionSubmissionRepository repository = new();

    private static NewQuizQuestionSubmission SampleSubmission(string text = "Who was the lead singer of Queen?") =>
        new(
            Guid.NewGuid(),
            text,
            [new QuizQuestionSubmissionOptionDraft("Freddie Mercury", true), new QuizQuestionSubmissionOptionDraft("Brian May", false)],
            "From a 1985 interview.");

    [Fact]
    public async Task CreateAsync_starts_pending_and_appears_in_the_queue()
    {
        var created = await repository.CreateAsync(SampleSubmission());

        Assert.Equal(QuizQuestionSubmissionStatus.Pending, created.Status);
        var pending = await repository.GetPendingAsync(1, 10);
        Assert.Single(pending);
        Assert.Equal(created.Id, pending[0].Id);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_submission()
    {
        var invalid = new NewQuizQuestionSubmission(
            Guid.NewGuid(),
            "  ",
            [new QuizQuestionSubmissionOptionDraft("Only one", true)],
            null);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateAsync(invalid));
    }

    [Fact]
    public async Task ApproveAsync_applies_admin_edits_and_makes_it_available_in_the_bank()
    {
        var created = await repository.CreateAsync(SampleSubmission("Who wrote Bohemian Rhapsody?"));

        var edit = new QuizQuestionSubmissionEdit(
            "Who wrote Bohemian Rhapsody? (edited)",
            [new QuizQuestionSubmissionOptionDraft("Freddie Mercury", true), new QuizQuestionSubmissionOptionDraft("Roger Taylor", false)]);
        var approved = await repository.ApproveAsync(created.Id, edit, "editor@example.test", "fixed wording");

        Assert.NotNull(approved);
        Assert.Equal(QuizQuestionSubmissionStatus.Approved, approved!.Status);
        Assert.Equal("Who wrote Bohemian Rhapsody? (edited)", approved.QuestionText);
        Assert.Equal("editor@example.test", approved.ReviewerEmail);

        var available = await repository.GetApprovedAndAvailableAsync();
        Assert.Contains(available, item => item.Id == created.Id);
    }

    [Fact]
    public async Task ApproveAsync_rejects_an_invalid_edit_and_leaves_status_pending()
    {
        var created = await repository.CreateAsync(SampleSubmission());

        var invalidEdit = new QuizQuestionSubmissionEdit(
            "Edited text",
            [new QuizQuestionSubmissionOptionDraft("Only one option", true)]);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.ApproveAsync(created.Id, invalidEdit, "editor@example.test", null));

        var stored = await repository.GetByIdAsync(created.Id);
        Assert.Equal(QuizQuestionSubmissionStatus.Pending, stored!.Status);
    }

    [Fact]
    public async Task RejectAsync_requires_a_reason_and_records_it()
    {
        var created = await repository.CreateAsync(SampleSubmission());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RejectAsync(created.Id, "editor@example.test", "  ", null));

        var rejected = await repository.RejectAsync(created.Id, "editor@example.test", "Not a real fact.", "internal note");
        Assert.Equal(QuizQuestionSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Not a real fact.", rejected.RejectionReason);
    }

    [Fact]
    public async Task Cannot_review_a_submission_twice()
    {
        var created = await repository.CreateAsync(SampleSubmission());
        await repository.RejectAsync(created.Id, "editor@example.test", "First reason.", null);

        var edit = new QuizQuestionSubmissionEdit(
            created.QuestionText,
            created.Options.Select(o => new QuizQuestionSubmissionOptionDraft(o.Text, o.IsCorrect)).ToList());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ApproveAsync(created.Id, edit, "editor@example.test", null));
    }

    [Fact]
    public async Task MarkAddedToQuizAsync_removes_it_from_the_available_bank()
    {
        var created = await repository.CreateAsync(SampleSubmission());
        var edit = new QuizQuestionSubmissionEdit(
            created.QuestionText,
            created.Options.Select(o => new QuizQuestionSubmissionOptionDraft(o.Text, o.IsCorrect)).ToList());
        await repository.ApproveAsync(created.Id, edit, "editor@example.test", null);
        Assert.Single(await repository.GetApprovedAndAvailableAsync());

        var quizId = Guid.NewGuid();
        var updated = await repository.MarkAddedToQuizAsync(created.Id, quizId, "editor@example.test");

        Assert.Equal(quizId, updated!.AddedToQuizId);
        Assert.Empty(await repository.GetApprovedAndAvailableAsync());
    }

    [Fact]
    public async Task MarkAddedToQuizAsync_requires_an_approved_submission()
    {
        var created = await repository.CreateAsync(SampleSubmission());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.MarkAddedToQuizAsync(created.Id, Guid.NewGuid(), "editor@example.test"));
    }

    [Fact]
    public async Task GetBySubmitterAsync_returns_only_that_members_suggestions()
    {
        var owner = Guid.NewGuid();
        var ownerSubmission = new NewQuizQuestionSubmission(
            owner,
            "Owner question?",
            [new QuizQuestionSubmissionOptionDraft("A", true), new QuizQuestionSubmissionOptionDraft("B", false)],
            null);
        await repository.CreateAsync(ownerSubmission);
        await repository.CreateAsync(SampleSubmission("Someone else's question?"));

        var page = await repository.GetBySubmitterAsync(owner);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Owner question?", page.Items[0].QuestionText);
    }

    [Fact]
    public async Task Audit_log_records_submit_approve_and_reject()
    {
        var created = await repository.CreateAsync(SampleSubmission());
        var edit = new QuizQuestionSubmissionEdit(
            created.QuestionText,
            created.Options.Select(o => new QuizQuestionSubmissionOptionDraft(o.Text, o.IsCorrect)).ToList());
        await repository.ApproveAsync(created.Id, edit, "editor@example.test", null);

        var logs = repository.GetAuditLogs(created.Id);
        Assert.Contains(logs, log => log.Action == "Submitted");
        Assert.Contains(logs, log => log.Action == QuizQuestionSubmissionStatus.Approved);
    }
}
