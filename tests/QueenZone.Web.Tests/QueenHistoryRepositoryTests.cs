using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class QueenHistoryRepositoryTests
{
    [Fact]
    public async Task InMemoryRepositoryReturnsPublishedExactMatchesForMonthAndDay()
    {
        var repository = new InMemoryQueenHistoryRepository(
        [
            Event(1, "Lower importance", new DateTime(1973, 7, 13), 50),
            Event(2, "Higher importance", new DateTime(1985, 7, 13), 100),
            Event(3, "Hidden event", new DateTime(1990, 7, 13), 100, isPublished: false),
            Event(4, "Different day", new DateTime(1975, 10, 31), 100),
            Event(5, "Month precision", new DateTime(1976, 7, 13), 100, QueenHistoryDatePrecision.MonthYear),
        ]);

        var events = await repository.GetOnThisDayAsync(new DateOnly(2026, 7, 13), 3);

        Assert.Equal(["Higher importance", "Lower importance"], events.Select(item => item.Title));
    }

    [Fact]
    public async Task InMemoryRepositoryReturnsNearbyMatchesWhenExactDateIsEmpty()
    {
        var repository = new InMemoryQueenHistoryRepository(
        [
            Event(1, "Two days away", new DateTime(1985, 7, 13), 100),
            Event(2, "One day away", new DateTime(1947, 7, 16), 80),
            Event(3, "Outside window", new DateTime(1975, 7, 25), 100),
        ]);

        var events = await repository.GetAroundThisDayAsync(new DateOnly(2026, 7, 15), 7, 3);

        Assert.Equal(["One day away", "Two days away"], events.Select(item => item.Title));
    }

    [Fact]
    public void OnThisDaySqlServerQuery_UsesIndexedMonthDayColumn()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=localhost;Database=query-shape-only;TrustServerCertificate=True")
            .Options;
        using var dbContext = new QueenZoneDbContext(options);
        var sql = new EfQueenHistoryRepository(dbContext)
            .BuildOnThisDayQuery(new DateOnly(2026, 7, 13)).ToQueryString();

        Assert.Contains("[EventMonthDay] =", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DATEPART", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EfRepositoryReadsPublishedExactMatches()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new QueenZoneDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.QueenHistoryEvents.AddRange(
                Entity(1, "Live Aid", new DateTime(1985, 7, 13), 100),
                Entity(2, "Debut album", new DateTime(1973, 7, 13), 80),
                Entity(3, "Hidden", new DateTime(1974, 7, 13), 100, isPublished: false));
            await setupContext.SaveChangesAsync();
        }

        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfQueenHistoryRepository(dbContext);

        var sql = repository.BuildOnThisDayQuery(new DateOnly(2026, 7, 13)).ToQueryString();
        Assert.Contains("EventMonthDay", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("strftime", sql, StringComparison.OrdinalIgnoreCase);

        var events = await repository.GetOnThisDayAsync(new DateOnly(2026, 7, 13), 5);

        Assert.Equal(["Live Aid", "Debut album"], events.Select(item => item.Title));
    }

    private static QueenHistoryEvent Event(
        int id,
        string title,
        DateTime eventDate,
        int importance,
        QueenHistoryDatePrecision datePrecision = QueenHistoryDatePrecision.ExactDate,
        bool isPublished = true) =>
        new(
            id,
            title,
            "Summary",
            DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
            datePrecision,
            QueenHistoryEventCategory.Other,
            importance,
            QueenHistoryEventSourceType.Curated,
            $"test:{id}",
            null,
            isPublished);

    private static QueenHistoryEventEntity Entity(
        int id,
        string title,
        DateTime eventDate,
        int importance,
        bool isPublished = true) =>
        new()
        {
            Id = id,
            Title = title,
            Summary = "Summary",
            EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
            DatePrecision = QueenHistoryDatePrecision.ExactDate,
            Category = QueenHistoryEventCategory.Other,
            Importance = importance,
            SourceType = QueenHistoryEventSourceType.Curated,
            SourceKey = $"test:{id}",
            IsPublished = isPublished,
            CreatedAt = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
        };
}
