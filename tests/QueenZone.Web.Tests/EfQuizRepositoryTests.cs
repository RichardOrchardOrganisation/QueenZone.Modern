using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class EfQuizRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfQuizRepository repository;
    private readonly Guid createdByMemberId = Guid.NewGuid();

    public EfQuizRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        repository = new EfQuizRepository(dbContext, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

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
    public async Task UpdateAsync_replaces_questions_and_options_against_a_real_ef_context()
    {
        var id = await repository.CreateAsync(SampleDraft(), createdByMemberId);

        await repository.UpdateAsync(id, SampleDraft("Renamed quiz"));

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Renamed quiz", updated!.Title);
        Assert.Equal(2, updated.Questions.Count);
        Assert.Equal("Who was the lead singer?", updated.Questions[0].Text);
        Assert.Equal(2, updated.Questions[0].Options.Count);
    }

    [Fact]
    public async Task UpdateAsync_rewrites_question_and_option_counts()
    {
        var id = await repository.CreateAsync(SampleDraft(), createdByMemberId);

        var created = await repository.GetByIdAsync(id);
        Assert.Equal(2, created!.Questions.Count);
        Assert.Equal(2, created.Questions[0].Options.Count);
        Assert.Equal(3, created.Questions[1].Options.Count);

        await repository.UpdateAsync(
            id,
            new AdminQuizDraft(
                "Trimmed quiz",
                "One question now",
                [
                    new QuizQuestionDraft(
                        "Which guitarist is known for the Red Special?",
                        1,
                        [
                            new QuizOptionDraft("Brian May", true),
                            new QuizOptionDraft("John Deacon", false),
                            new QuizOptionDraft("Roger Taylor", false),
                        ]),
                ]));

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Trimmed quiz", updated!.Title);
        var question = Assert.Single(updated.Questions);
        Assert.Equal("Which guitarist is known for the Red Special?", question.Text);
        Assert.Equal(3, question.Options.Count);
    }

    [Fact]
    public async Task UpdateAsync_can_be_called_repeatedly_on_the_same_quiz()
    {
        var id = await repository.CreateAsync(SampleDraft(), createdByMemberId);

        await repository.UpdateAsync(id, SampleDraft("First rename"));
        await repository.UpdateAsync(id, SampleDraft("Second rename"));

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Second rename", updated!.Title);
        Assert.Equal(2, updated.Questions.Count);
    }
}
