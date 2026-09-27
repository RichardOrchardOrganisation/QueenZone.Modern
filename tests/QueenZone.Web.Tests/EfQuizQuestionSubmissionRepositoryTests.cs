using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfQuizQuestionSubmissionRepositoryTests : QuizQuestionSubmissionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfQuizQuestionSubmissionRepository repository;
    private readonly Guid memberId = Guid.NewGuid();
    private readonly Guid otherMemberId = Guid.NewGuid();

    protected override IQuizQuestionSubmissionRepository Repository => repository;
    protected override Guid MemberId => memberId;
    protected override Guid OtherMemberId => otherMemberId;

    public EfQuizQuestionSubmissionRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        dbContext.MemberAccounts.AddRange(new MemberAccount
        {
            Id = memberId,
            Email = "quiz-question-ef@example.com",
            NormalizedEmail = "QUIZ-QUESTION-EF@EXAMPLE.COM",
            DisplayName = "EF Quiz Fan",
            CreatedAt = DateTime.UtcNow,
        }, new MemberAccount
        {
            Id = otherMemberId,
            Email = "quiz-other-ef@example.com",
            NormalizedEmail = "QUIZ-OTHER-EF@EXAMPLE.COM",
            DisplayName = "Other EF Quiz Fan",
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
    public async Task ApproveAsync_returns_null_for_an_unknown_id() =>
        Assert.Null(await repository.ApproveAsync(
            Guid.NewGuid(),
            new QuizQuestionSubmissionEdit("Text", [new QuizQuestionSubmissionOptionDraft("A", true), new QuizQuestionSubmissionOptionDraft("B", false)]),
            "editor@example.test",
            null));

    [Fact]
    public async Task RejectAsync_returns_null_for_an_unknown_id() =>
        Assert.Null(await repository.RejectAsync(Guid.NewGuid(), "editor@example.test", "Reason.", null));

    [Fact]
    public async Task MarkAddedToQuizAsync_returns_null_for_an_unknown_id() =>
        Assert.Null(await repository.MarkAddedToQuizAsync(Guid.NewGuid(), Guid.NewGuid(), "editor@example.test"));

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
