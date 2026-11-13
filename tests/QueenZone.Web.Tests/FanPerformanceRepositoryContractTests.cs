using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class FanPerformanceRepositoryContractTests
{
    protected abstract IFanPerformanceRepository Repository { get; }

    [Fact]
    public async Task VisibleRows_RoundTripAndCount_UnknownIdReturnsNull()
    {
        var rows = await Repository.GetPageAsync(1, 10);
        Assert.Equal(new[] { 5, 6 }, rows.Select(row => row.Id).Order());
        Assert.Equal(2, await Repository.GetVisibleCountAsync());
        var row = await Repository.GetByIdAsync(5);
        Assert.NotNull(row);
        Assert.Equal("Liar", row.Title);
        Assert.Equal("Band", row.PerformedBy);
        Assert.Equal("Cover", row.Description);
        Assert.Equal("liar.mp3", row.AudioFileName);
        Assert.Equal(1024, row.FileSizeBytes);
        Assert.Equal(120, row.DurationSeconds);
        Assert.Equal(new DateTime(2026, 9, 1), row.DateAdded);
        Assert.Null(await Repository.GetByIdAsync(404));
    }
}

public sealed class InMemoryFanPerformanceRepositoryContractTests : FanPerformanceRepositoryContractTests
{
    protected override IFanPerformanceRepository Repository { get; } = new InMemoryFanPerformanceRepository(new SharedFanPerformanceStore(
        [new(5, "Liar", "Band", "Cover", "liar.mp3", 1024, new DateTime(2026, 9, 1), 120),
         new(6, "Queen", "Band", "Cover", "queen.mp3", 2048, new DateTime(2026, 9, 2), 240)]));
}

public sealed class EfFanPerformanceRepositoryContractTests : FanPerformanceRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override IFanPerformanceRepository Repository { get; }

    public EfFanPerformanceRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE StageList (
                Q_STAGE_ID INTEGER NOT NULL, TITLE TEXT NOT NULL, PERFORMED_BY TEXT NOT NULL,
                DESCRIPTION TEXT, URL TEXT NOT NULL, thesize TEXT, DATE_ADDED TEXT NOT NULL, DurationSeconds INTEGER);
            INSERT INTO StageList VALUES
                (5, 'Liar', 'Band', 'Cover', 'liar.mp3', '1024', '2026-09-01', 120),
                (6, 'Queen', 'Band', 'Cover', 'queen.mp3', '2048', '2026-09-02', 240);
            """);
        Repository = new EfFanPerformanceRepository(context, false,
            "SELECT * FROM StageList", "SELECT COUNT(*) AS Value FROM StageList",
            id => $"SELECT * FROM StageList WHERE Q_STAGE_ID = {id}");
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
