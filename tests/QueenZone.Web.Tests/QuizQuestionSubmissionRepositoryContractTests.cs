using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>Shared submission lifecycle for the route fake and SQLite EF implementation.</summary>
public abstract class QuizQuestionSubmissionRepositoryContractTests
{
    protected abstract IQuizQuestionSubmissionRepository Repository { get; }
    protected abstract Guid MemberId { get; }
    protected abstract Guid OtherMemberId { get; }

    private NewQuizQuestionSubmission Submission(Guid? memberId = null, string text = "Who sang for Queen?") =>
        new(memberId ?? MemberId, text,
            [new QuizQuestionSubmissionOptionDraft("Freddie Mercury", true), new QuizQuestionSubmissionOptionDraft("Brian May", false)],
            "From an interview.");

    private static QuizQuestionSubmissionEdit Edit(string text) =>
        new(text, [new QuizQuestionSubmissionOptionDraft("Freddie Mercury", true), new QuizQuestionSubmissionOptionDraft("Roger Taylor", false)]);

    [Fact]
    public async Task Create_StartsPending_AndAppearsInQueue()
    {
        var created = await Repository.CreateAsync(Submission());
        Assert.Equal(QuizQuestionSubmissionStatus.Pending, created.Status);
        Assert.Equal(2, created.Options.Count);
        Assert.Equal(created.Id, Assert.Single(await Repository.GetPendingAsync(1, 10)).Id);
        Assert.Equal(created.Id, (await Repository.GetByIdAsync(created.Id))!.Id);
    }

    [Fact]
    public async Task Create_RejectsInvalidSubmission()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.CreateAsync(new NewQuizQuestionSubmission(
            MemberId, "  ", [new QuizQuestionSubmissionOptionDraft("Only one", true)], null)));
    }

    [Fact]
    public async Task Approval_AppliesEditsAndMakesAvailable()
    {
        var created = await Repository.CreateAsync(Submission());
        var approved = await Repository.ApproveAsync(created.Id, Edit("Edited question?"), "editor@example.test", "fixed wording");
        Assert.Equal(QuizQuestionSubmissionStatus.Approved, approved!.Status);
        Assert.Equal("Edited question?", approved.QuestionText);
        Assert.Equal("editor@example.test", approved.ReviewerEmail);
        Assert.Contains(await Repository.GetApprovedAndAvailableAsync(), item => item.Id == created.Id);
        Assert.Empty(await Repository.GetPendingAsync(1, 10));
    }

    [Fact]
    public async Task InvalidApproval_PreservesPendingStatus()
    {
        var created = await Repository.CreateAsync(Submission());
        var invalid = new QuizQuestionSubmissionEdit("Edited", [new QuizQuestionSubmissionOptionDraft("Only one", true)]);
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.ApproveAsync(created.Id, invalid, "editor@example.test", null));
        Assert.Equal(QuizQuestionSubmissionStatus.Pending, (await Repository.GetByIdAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task Rejection_RequiresReason_AndRecordsIt()
    {
        var created = await Repository.CreateAsync(Submission());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RejectAsync(created.Id, "editor@example.test", "  ", null));
        Assert.Equal(QuizQuestionSubmissionStatus.Pending, (await Repository.GetByIdAsync(created.Id))!.Status);
        var rejected = await Repository.RejectAsync(created.Id, "editor@example.test", "Not accurate.", null);
        Assert.Equal(QuizQuestionSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Not accurate.", rejected.RejectionReason);
    }

    [Fact]
    public async Task ReviewedSubmission_CannotBeReviewedAgain()
    {
        var created = await Repository.CreateAsync(Submission());
        await Repository.RejectAsync(created.Id, "editor@example.test", "First reason.", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ApproveAsync(created.Id, Edit("Edited?"), "editor@example.test", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RejectAsync(created.Id, "editor@example.test", "Second reason.", null));
    }

    [Fact]
    public async Task AddedToQuiz_RequiresApproval_AndLeavesAvailableBank()
    {
        var created = await Repository.CreateAsync(Submission());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.MarkAddedToQuizAsync(created.Id, Guid.NewGuid(), "editor@example.test"));
        await Repository.ApproveAsync(created.Id, Edit(created.QuestionText), "editor@example.test", null);
        Assert.Single(await Repository.GetApprovedAndAvailableAsync());
        var quizId = Guid.NewGuid();
        var updated = await Repository.MarkAddedToQuizAsync(created.Id, quizId, "editor@example.test");
        Assert.Equal(quizId, updated!.AddedToQuizId);
        Assert.Empty(await Repository.GetApprovedAndAvailableAsync());
    }

    [Fact]
    public async Task BySubmitter_ExcludesOtherMembers()
    {
        await Repository.CreateAsync(Submission(text: "Mine?"));
        await Repository.CreateAsync(Submission(OtherMemberId, "Not mine?"));
        var page = await Repository.GetBySubmitterAsync(MemberId);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Mine?", Assert.Single(page.Items).QuestionText);
    }
}
