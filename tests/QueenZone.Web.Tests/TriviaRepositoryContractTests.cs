using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class TriviaRepositoryContractTests
{
    protected abstract ITriviaRepository Repository { get; }
    protected static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Writes_RoundTrip_AllFields_AndPublication()
    {
        var id = await Repository.CreateAsync(new("Fact", false, "Albums", "Easy", "Book"));
        var row = await Repository.GetByIdAsync(id);
        Assert.NotNull(row);
        Assert.Equal("Fact", row.Text);
        Assert.Equal("Albums", row.Category);
        Assert.Equal("Easy", row.Difficulty);
        Assert.Equal("Book", row.Source);
        Assert.False(row.IsPublished);
        Assert.NotEqual(default, row.CreatedAt);
        Assert.Null(await Repository.GetRandomPublishedAsync());
        await Repository.SetPublishedAsync(id, true);
        Assert.True((await Repository.GetByIdAsync(id))!.IsPublished);
        Assert.Equal(id, (await Repository.GetRandomPublishedAsync())!.Id);
        await Repository.UpdateAsync(id, new("Edited", false, "Songs", "Hard", "Interview"));
        row = await Repository.GetByIdAsync(id);
        Assert.Equal("Edited", row!.Text);
        Assert.Equal("Songs", row.Category);
        Assert.Equal("Hard", row.Difficulty);
        Assert.Equal("Interview", row.Source);
        Assert.False(row.IsPublished);
        Assert.Null(await Repository.GetRandomPublishedAsync());
        await Repository.DeleteAsync(id);
        Assert.Null(await Repository.GetByIdAsync(id));
    }

    [Fact]
    public async Task All_IncludesBothPublicationStates_NewestFirst()
    {
        await Repository.CreateAsync(new("First", true));
        await Repository.CreateAsync(new("Second", false));
        var rows = await Repository.GetAllAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Second", "First" }, rows.Select(row => row.Text));
    }

    [Fact]
    public async Task MissingWrites_Throw()
    {
        Assert.Null(await Repository.GetByIdAsync(404));
        Assert.Equal("Trivia fact 404 was not found.", (await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.UpdateAsync(404, new("Missing", true)))).Message);
        Assert.Equal("Trivia fact 404 was not found.", (await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.DeleteAsync(404))).Message);
        Assert.Equal("Trivia fact 404 was not found.", (await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SetPublishedAsync(404, true))).Message);
    }
}

public sealed class InMemoryTriviaRepositoryContractTests : TriviaRepositoryContractTests
{
    protected override ITriviaRepository Repository { get; } = new InMemoryTriviaRepository(Array.Empty<TriviaFactItem>());
}

public sealed class EfTriviaRepositoryContractTests : TriviaRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override ITriviaRepository Repository { get; }

    public EfTriviaRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        Repository = new EfTriviaRepository(context);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
