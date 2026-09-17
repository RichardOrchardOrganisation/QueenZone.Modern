using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizRepositoryTests
{
    private readonly InMemoryQuizRepository repository = new(new SharedQuizStore());

    private static AdminQuizDraft SampleDraft(string title = "Sample quiz") =>
        new(
            title,
            "Description",
            [
                new QuizQuestionDraft(
                    "Who was the lead singer?",
                    2,
                    [new QuizOptionDraft("Freddie Mercury", true), new QuizOptionDraft("Brian May", false)]),
                new QuizQuestionDraft(
                    "What year was Bohemian Rhapsody released?",
                    1,
                    [new QuizOptionDraft("1975", true), new QuizOptionDraft("1980", false), new QuizOptionDraft("1985", false)]),
            ]);

    [Fact]
    public async Task Create_starts_as_an_unpublished_draft()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());

        var quiz = await repository.GetByIdAsync(id);
        Assert.NotNull(quiz);
        Assert.False(quiz!.IsPublished);
        Assert.Null(quiz.PublishedAt);
        Assert.Equal(2, quiz.Questions.Count);
        Assert.Equal(0, quiz.AttemptCount);
    }

    [Fact]
    public async Task Publish_and_unpublish_toggle_visibility()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());

        await repository.PublishAsync(id);
        var published = await repository.GetByIdAsync(id);
        Assert.True(published!.IsPublished);
        Assert.NotNull(published.PublishedAt);

        await repository.UnpublishAsync(id);
        var unpublished = await repository.GetByIdAsync(id);
        Assert.False(unpublished!.IsPublished);
        Assert.NotNull(unpublished.PublishedAt);
    }

    [Fact]
    public async Task Republish_keeps_the_original_PublishedAt()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());
        await repository.PublishAsync(id);
        var firstPublishedAt = (await repository.GetByIdAsync(id))!.PublishedAt;

        await repository.UnpublishAsync(id);
        await repository.PublishAsync(id);

        Assert.Equal(firstPublishedAt, (await repository.GetByIdAsync(id))!.PublishedAt);
    }

    [Fact]
    public async Task Update_replaces_questions_and_options_while_no_results_exist()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());

        await repository.UpdateAsync(id, SampleDraft("Renamed quiz"));

        var updated = await repository.GetByIdAsync(id);
        Assert.Equal("Renamed quiz", updated!.Title);
        Assert.Equal(2, updated.Questions.Count);
    }

    [Fact]
    public async Task Zero_result_quiz_can_be_deleted()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());

        await repository.DeleteAsync(id);

        Assert.Null(await repository.GetByIdAsync(id));
    }

    [Fact]
    public async Task Recording_an_attempt_locks_editing_and_deletion()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());
        await repository.PublishAsync(id);

        await repository.RecordAttemptAsync(id, Guid.NewGuid(), score: 3, correctCount: 2, questionCount: 2);

        var quiz = await repository.GetByIdAsync(id);
        Assert.Equal(1, quiz!.AttemptCount);

        var updateEx = await Assert.ThrowsAsync<QuizException>(() =>
            repository.UpdateAsync(id, SampleDraft("Changed")));
        Assert.Equal(QuizException.HasResults, updateEx.Code);

        var deleteEx = await Assert.ThrowsAsync<QuizException>(() => repository.DeleteAsync(id));
        Assert.Equal(QuizException.HasResults, deleteEx.Code);

        // Publish state can still change after results exist.
        await repository.UnpublishAsync(id);
        Assert.False((await repository.GetByIdAsync(id))!.IsPublished);
    }

    [Fact]
    public async Task GetAllAsync_reports_attempt_counts_per_quiz()
    {
        var first = await repository.CreateAsync(SampleDraft("First"), Guid.NewGuid());
        var second = await repository.CreateAsync(SampleDraft("Second"), Guid.NewGuid());
        await repository.RecordAttemptAsync(first, Guid.NewGuid(), 3, 2, 2);
        await repository.RecordAttemptAsync(first, Guid.NewGuid(), 1, 1, 2);

        var all = await repository.GetAllAsync();

        Assert.Equal(2, all.Single(item => item.Id == first).AttemptCount);
        Assert.Equal(0, all.Single(item => item.Id == second).AttemptCount);
    }

    [Fact]
    public async Task GetPublishedAsync_only_returns_published_quizzes()
    {
        var draftId = await repository.CreateAsync(SampleDraft("Draft"), Guid.NewGuid());
        var publishedId = await repository.CreateAsync(SampleDraft("Published"), Guid.NewGuid());
        await repository.PublishAsync(publishedId);

        var published = await repository.GetPublishedAsync();

        Assert.Single(published);
        Assert.Equal(publishedId, published[0].Id);
        Assert.Equal(2, published[0].QuestionCount);
        Assert.DoesNotContain(published, item => item.Id == draftId);
    }

    [Fact]
    public async Task GetPublishedForPlayAsync_hides_correct_answer_and_hides_drafts()
    {
        var draftId = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());
        var publishedId = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());
        await repository.PublishAsync(publishedId);

        Assert.Null(await repository.GetPublishedForPlayAsync(draftId));

        var play = await repository.GetPublishedForPlayAsync(publishedId);
        Assert.NotNull(play);
        Assert.Equal(2, play!.Questions.Count);
        Assert.All(play.Questions, question => Assert.True(question.Options.Count is >= 2 and <= 4));
    }

    [Fact]
    public async Task SubmitAsync_scores_correctly_and_records_only_for_signed_in_members()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());
        await repository.PublishAsync(id);
        var play = await repository.GetPublishedForPlayAsync(id);
        var q1Correct = play!.Questions[0].Options.First(o => o.Text == "Freddie Mercury").Id;
        var q2Wrong = play.Questions[1].Options.First(o => o.Text == "1980").Id;

        var anonymousResult = await repository.SubmitAsync(
            id,
            memberAccountId: null,
            [new QuizAnswerSubmission(play.Questions[0].Id, q1Correct), new QuizAnswerSubmission(play.Questions[1].Id, q2Wrong)]);

        Assert.NotNull(anonymousResult);
        Assert.False(anonymousResult!.Recorded);
        Assert.Equal(2, anonymousResult.Score); // 2 points for Q1, 0 for Q2
        Assert.Equal(3, anonymousResult.MaxScore);
        Assert.Equal(1, anonymousResult.CorrectCount);
        Assert.True(anonymousResult.Answers.Single(a => a.QuestionId == play.Questions[0].Id).IsCorrect);
        Assert.False(anonymousResult.Answers.Single(a => a.QuestionId == play.Questions[1].Id).IsCorrect);
        Assert.Equal(0, (await repository.GetByIdAsync(id))!.AttemptCount);

        var memberId = Guid.NewGuid();
        var memberResult = await repository.SubmitAsync(
            id,
            memberId,
            [new QuizAnswerSubmission(play.Questions[0].Id, q1Correct), new QuizAnswerSubmission(play.Questions[1].Id, null)]);

        Assert.NotNull(memberResult);
        Assert.True(memberResult!.Recorded);
        Assert.Equal(2, memberResult.Score);
        Assert.Equal(1, (await repository.GetByIdAsync(id))!.AttemptCount);
    }

    [Fact]
    public async Task SubmitAsync_returns_null_for_unpublished_or_missing_quiz()
    {
        var draftId = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());

        Assert.Null(await repository.SubmitAsync(draftId, Guid.NewGuid(), []));
        Assert.Null(await repository.SubmitAsync(Guid.NewGuid(), Guid.NewGuid(), []));
    }

    [Fact]
    public async Task GetLeaderboardAsync_ranks_by_summed_score_and_includes_the_viewer_outside_the_top()
    {
        var id = await repository.CreateAsync(SampleDraft(), Guid.NewGuid());
        await repository.PublishAsync(id);
        var leader = Guid.NewGuid();
        var runnerUp = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        await repository.RecordAttemptAsync(id, leader, score: 10, correctCount: 2, questionCount: 2);
        await repository.RecordAttemptAsync(id, leader, score: 5, correctCount: 1, questionCount: 2);
        await repository.RecordAttemptAsync(id, runnerUp, score: 8, correctCount: 2, questionCount: 2);
        await repository.RecordAttemptAsync(id, viewer, score: 1, correctCount: 1, questionCount: 2);

        var result = await repository.GetLeaderboardAsync(QuizLeaderboardScope.AllTime, viewer, top: 2);

        Assert.Equal(2, result.Top.Count);
        Assert.Equal(leader, result.Top[0].MemberAccountId);
        Assert.Equal(15, result.Top[0].Score);
        Assert.Equal(2, result.Top[0].AttemptCount);
        Assert.Equal(runnerUp, result.Top[1].MemberAccountId);
        Assert.Equal(3, result.TotalMembers);
        Assert.NotNull(result.Viewer);
        Assert.Equal(3, result.Viewer!.Rank);
        Assert.Equal(1, result.Viewer.Score);
    }

    [Fact]
    public async Task GetLeaderboardAsync_week_scope_excludes_attempts_from_before_this_week()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-08T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)); // Tuesday
        var timedRepository = new InMemoryQuizRepository(new SharedQuizStore(), clock);
        var id = await timedRepository.CreateAsync(SampleDraft(), Guid.NewGuid());
        await timedRepository.PublishAsync(id);
        var lastWeekMember = Guid.NewGuid();
        var thisWeekMember = Guid.NewGuid();
        await timedRepository.RecordAttemptAsync(id, lastWeekMember, score: 4, correctCount: 2, questionCount: 2);

        clock.UtcNow = DateTimeOffset.Parse("2026-09-15T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture); // next Tuesday
        await timedRepository.RecordAttemptAsync(id, thisWeekMember, score: 6, correctCount: 2, questionCount: 2);

        var weekly = await timedRepository.GetLeaderboardAsync(QuizLeaderboardScope.Week, null, top: 10);
        var allTime = await timedRepository.GetLeaderboardAsync(QuizLeaderboardScope.AllTime, null, top: 10);

        Assert.Single(weekly.Top);
        Assert.Equal(thisWeekMember, weekly.Top[0].MemberAccountId);
        Assert.Equal(2, allTime.Top.Count);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Fact]
    public async Task Unknown_quiz_operations_throw_not_found()
    {
        var missingId = Guid.NewGuid();

        Assert.Null(await repository.GetByIdAsync(missingId));
        await Assert.ThrowsAsync<QuizException>(() => repository.PublishAsync(missingId));
        await Assert.ThrowsAsync<QuizException>(() => repository.UnpublishAsync(missingId));
        await Assert.ThrowsAsync<QuizException>(() => repository.UpdateAsync(missingId, SampleDraft()));
        await Assert.ThrowsAsync<QuizException>(() => repository.DeleteAsync(missingId));
    }
}
