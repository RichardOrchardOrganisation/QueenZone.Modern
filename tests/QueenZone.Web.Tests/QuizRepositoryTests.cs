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
