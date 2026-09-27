using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizQuestionSubmissionRepositoryTests : QuizQuestionSubmissionRepositoryContractTests
{
    private readonly InMemoryQuizQuestionSubmissionRepository repository = new();
    private readonly Guid memberId = Guid.NewGuid();
    private readonly Guid otherMemberId = Guid.NewGuid();

    protected override IQuizQuestionSubmissionRepository Repository => repository;
    protected override Guid MemberId => memberId;
    protected override Guid OtherMemberId => otherMemberId;

    private static NewQuizQuestionSubmission SampleSubmission(string text = "Who was the lead singer of Queen?") =>
        new(
            Guid.NewGuid(),
            text,
            [new QuizQuestionSubmissionOptionDraft("Freddie Mercury", true), new QuizQuestionSubmissionOptionDraft("Brian May", false)],
            "From a 1985 interview.");

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
