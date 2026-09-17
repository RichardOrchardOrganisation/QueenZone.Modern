using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizCsvImporterTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;

    public QuizCsvImporterTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public void ReadDrafts_groups_rows_by_adjacent_quiz_and_question()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Freddie Mercury,true
            Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Brian May,false
            Queen Trivia,A quiz about Queen,What year was the band formed?,,hard,2,1970,true
            Queen Trivia,A quiz about Queen,What year was the band formed?,,hard,2,1973,false
            News of the World,,Which album features Bohemian Rhapsody?,,,1,A Night at the Opera,true
            News of the World,,Which album features Bohemian Rhapsody?,,,1,A Day at the Races,false
            """);

        var drafts = QuizCsvImporter.ReadDrafts(csvPath);

        Assert.Equal(2, drafts.Count);
        var first = drafts[0];
        Assert.Equal("Queen Trivia", first.Title);
        Assert.Equal("A quiz about Queen", first.Description);
        Assert.Equal(2, first.Questions.Count);
        Assert.Equal("Who was the lead singer?", first.Questions[0].Text);
        Assert.Equal("Band Members", first.Questions[0].Category);
        Assert.Equal("easy", first.Questions[0].Difficulty);
        Assert.Equal(2, first.Questions[0].Options.Count);
        Assert.True(first.Questions[0].Options[0].IsCorrect);
        Assert.Null(first.Questions[1].Category);
        Assert.Equal("hard", first.Questions[1].Difficulty);
        Assert.Equal(2, first.Questions[1].Points);

        var second = drafts[1];
        Assert.Equal("News of the World", second.Title);
        Assert.Null(second.Description);
        Assert.Single(second.Questions);
        Assert.Null(second.Questions[0].Category);
        Assert.Null(second.Questions[0].Difficulty);
    }

    [Fact]
    public void ReadDrafts_rejects_non_contiguous_quiz_rows()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Quiz A,,Question 1,,,1,Option 1,true
            Quiz A,,Question 1,,,1,Option 2,false
            Quiz B,,Question 1,,,1,Option 1,true
            Quiz B,,Question 1,,,1,Option 2,false
            Quiz A,,Question 2,,,1,Option 1,true
            Quiz A,,Question 2,,,1,Option 2,false
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => QuizCsvImporter.ReadDrafts(csvPath));
        Assert.Contains("not contiguous", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadDrafts_rejects_non_contiguous_question_rows()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Quiz A,,Question 1,,,1,Option 1,true
            Quiz A,,Question 2,,,1,Option 1,true
            Quiz A,,Question 1,,,1,Option 2,false
            Quiz A,,Question 2,,,1,Option 2,false
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => QuizCsvImporter.ReadDrafts(csvPath));
        Assert.Contains("not contiguous", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadDrafts_rejects_invalid_is_correct_value()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Quiz A,,Question 1,,,1,Option 1,maybe
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => QuizCsvImporter.ReadDrafts(csvPath));
        Assert.Contains("IsCorrect", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadDrafts_rejects_invalid_difficulty_value()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Quiz A,,Question 1,,impossible,1,Option 1,true
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => QuizCsvImporter.ReadDrafts(csvPath));
        Assert.Contains("Difficulty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_creates_unpublished_quizzes_with_questions_and_options()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Freddie Mercury,true
            Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Brian May,false
            """);
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var importer = new QuizCsvImporter(repository);

        var result = await importer.ImportAsync(csvPath);

        Assert.Equal(new QuizCsvImportResult(2, 1, 1, 2), result);

        var quizzes = await dbContext.Quizzes.AsNoTracking().ToListAsync();
        var quiz = Assert.Single(quizzes);
        Assert.Equal("Queen Trivia", quiz.Title);
        Assert.False(quiz.IsPublished);

        var detail = await repository.GetByIdAsync(quiz.Id);
        Assert.NotNull(detail);
        var question = Assert.Single(detail!.Questions);
        Assert.Equal("Band Members", question.Category);
        Assert.Equal("easy", question.Difficulty);
        Assert.Equal(2, question.Options.Count);
        Assert.Single(question.Options, option => option.IsCorrect);
    }

    [Fact]
    public async Task ImportAsync_rejects_a_question_without_exactly_one_correct_option_and_creates_nothing()
    {
        var csvPath = WriteCsv("""
            QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
            Queen Trivia,,Who was the lead singer?,,,1,Freddie Mercury,false
            Queen Trivia,,Who was the lead singer?,,,1,Brian May,false
            """);
        var repository = new EfQuizRepository(dbContext, TimeProvider.System);
        var importer = new QuizCsvImporter(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAsync(csvPath));

        Assert.Empty(await dbContext.Quizzes.AsNoTracking().ToListAsync());
    }

    private static string WriteCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine));
        return path;
    }
}
