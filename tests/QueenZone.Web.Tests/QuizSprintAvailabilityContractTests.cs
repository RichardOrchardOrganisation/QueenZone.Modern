using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class QuizSprintAvailabilityContractTests
{
    protected abstract IQuizRepository Repository { get; }

    [Fact]
    public async Task HasPublishedSprintQuestionsAsync_PublishUnpublishDelete_TracksAvailability()
    {
        Assert.False(await Repository.HasPublishedSprintQuestionsAsync());
        var id = await Repository.CreateAsync(new AdminQuizDraft("Availability", null,
            [new QuizQuestionDraft("Singer?", 1,
                [new QuizOptionDraft("Freddie", true), new QuizOptionDraft("Brian", false)])]), Guid.NewGuid());
        Assert.False(await Repository.HasPublishedSprintQuestionsAsync());
        await Repository.PublishAsync(id);
        Assert.True(await Repository.HasPublishedSprintQuestionsAsync());
        await Repository.UnpublishAsync(id);
        Assert.False(await Repository.HasPublishedSprintQuestionsAsync());
        await Repository.PublishAsync(id);
        await Repository.DeleteAsync(id);
        Assert.False(await Repository.HasPublishedSprintQuestionsAsync());
    }
}

public sealed class InMemoryQuizSprintAvailabilityTests : QuizSprintAvailabilityContractTests
{
    protected override IQuizRepository Repository { get; } = new InMemoryQuizRepository(new SharedQuizStore());
}

public sealed class EfQuizSprintAvailabilityTests : QuizSprintAvailabilityContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly QueenZoneDbContext context;
    protected override IQuizRepository Repository { get; }

    public EfQuizSprintAvailabilityTests()
    {
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        Repository = new EfQuizRepository(context, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
