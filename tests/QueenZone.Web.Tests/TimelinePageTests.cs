using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;
using QueenZone.Web.Pages.Timeline;

namespace QueenZone.Web.Tests;

public sealed class TimelinePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TimelinePageTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task TimelinePageRendersOneDecadeAtATimeWithNavigableLinks()
    {
        using var client = factory.CreateClient();
        var current = await client.GetStringAsync("/timeline");
        Assert.Contains("id=\"event-10\"", current);
        Assert.DoesNotContain("id=\"event-5\"", current);

        var eighties = await client.GetStringAsync("/timeline?decade=1980s");
        Assert.Contains("id=\"event-5\"", eighties);
        Assert.DoesNotContain("id=\"event-10\"", eighties);
        Assert.Contains("href=\"/timeline?decade=2020s#decade-2020s\"", eighties);
    }

    [Fact]
    public async Task TimelinePageRendersEventsGroupedByDecade()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/timeline");

        Assert.Contains("Queen History", body);
        Assert.Contains("Five decades", body);
        Assert.Contains("1940s", body);
        Assert.Contains("1970s", body);
        Assert.Contains("1980s", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/timeline"), body);
    }

    [Fact]
    public async Task TimelinePageRendersCategoryFilterChips()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/timeline");

        Assert.Contains("All events", body);
        Assert.Contains("data-filter=\"music\"", body);
        Assert.Contains("data-filter=\"live\"", body);
        Assert.Contains("data-filter=\"milestone\"", body);
        Assert.Contains("data-filter=\"other\"", body);
    }

    [Fact]
    public async Task TimelinePageRendersDecadeRailJumpButtons()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/timeline");

        Assert.Contains("data-jump-decade=", body);
        Assert.Contains("Jump to", body);
    }

    [Fact]
    public async Task TimelinePageRendersEventRowsWithDataAttributes()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/timeline?decade=1980s");

        Assert.Contains("data-cat=\"live\"", body);
        Assert.Contains("data-year=", body);
        Assert.DoesNotContain("id=\"event-10\"", body);
        Assert.Contains("href=\"/timeline?decade=2020s#decade-2020s\"", body);
    }

    [Fact]
    public async Task TimelinePageRendersExpandableDetailPanels()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/timeline");

        Assert.Contains("aria-expanded=\"false\"", body);
        Assert.Contains("qz-htl-row__detail", body);
        Assert.Contains("qz-htl-row__standfirst", body);
    }
}

public sealed class TimelinePageModelTests
{
    [Fact]
    public void TimelineEventRowMapsDecadeCorrectly()
    {
        var e = CreateEvent(new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc));
        var row = new TimelineEventRow(e);
        Assert.Equal("1985", row.Year);
        Assert.Equal("1980s", row.Decade);
    }

    [Fact]
    public void TimelineEventRowMapsDecadeForNineties()
    {
        var e = CreateEvent(new DateTime(1991, 11, 24, 0, 0, 0, DateTimeKind.Utc));
        var row = new TimelineEventRow(e);
        Assert.Equal("1990s", row.Decade);
    }

    [Theory]
    [InlineData(QueenHistoryEventCategory.Concert, "live", "Live")]
    [InlineData(QueenHistoryEventCategory.Release, "music", "Release")]
    [InlineData(QueenHistoryEventCategory.Recording, "music", "Recording")]
    [InlineData(QueenHistoryEventCategory.Award, "milestone", "Award")]
    [InlineData(QueenHistoryEventCategory.Birthday, "milestone", "Birthday")]
    [InlineData(QueenHistoryEventCategory.SiteHistory, "milestone", "Archive")]
    [InlineData(QueenHistoryEventCategory.TVRadio, "other", "TV / Radio")]
    [InlineData(QueenHistoryEventCategory.Other, "other", "Other")]
    public void TimelineEventRowMapsCategory(QueenHistoryEventCategory category, string expectedCat, string expectedLabel)
    {
        var e = CreateEvent(new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc), category);
        var row = new TimelineEventRow(e);
        Assert.Equal(expectedCat, row.DisplayCategory);
        Assert.Equal(expectedLabel, row.DisplayLabel);
    }

    private static QueenHistoryEvent CreateEvent(
        DateTime eventDate,
        QueenHistoryEventCategory category = QueenHistoryEventCategory.Concert) =>
        new(1, "Title", "Summary", eventDate, QueenHistoryDatePrecision.ExactDate,
            category, 100, QueenHistoryEventSourceType.Curated, "test:1", null, true);
}

public sealed class GetAllPublishedTests
{
    [Fact]
    public async Task InMemoryRepositoryReturnsOnlyPublishedEvents()
    {
        var repository = new InMemoryQueenHistoryRepository(
        [
            Event(1, "Published A", isPublished: true),
            Event(2, "Published B", isPublished: true),
            Event(3, "Unpublished", isPublished: false),
        ]);

        var events = await repository.GetAllPublishedAsync();

        Assert.Equal(["Published A", "Published B"], events.Select(e => e.Title).OrderBy(t => t));
    }

    [Fact]
    public async Task InMemoryRepositoryIncludesAllDatePrecisionsWhenPublished()
    {
        var repository = new InMemoryQueenHistoryRepository(
        [
            Event(1, "Exact",  isPublished: true,  precision: QueenHistoryDatePrecision.ExactDate),
            Event(2, "Month",  isPublished: true,  precision: QueenHistoryDatePrecision.MonthYear),
            Event(3, "Year",   isPublished: true,  precision: QueenHistoryDatePrecision.YearOnly),
            Event(4, "Hidden", isPublished: false, precision: QueenHistoryDatePrecision.ExactDate),
        ]);

        var events = await repository.GetAllPublishedAsync();

        Assert.Equal(3, events.Count);
        Assert.DoesNotContain(events, e => e.Title == "Hidden");
    }

    [Fact]
    public async Task InMemoryRepositoryReturnsEmptyWhenNoPublishedEvents()
    {
        var repository = new InMemoryQueenHistoryRepository(
        [
            Event(1, "Hidden", isPublished: false),
        ]);

        var events = await repository.GetAllPublishedAsync();

        Assert.Empty(events);
    }

    [Fact]
    public async Task EfRepositoryReturnsOnlyPublishedEvents()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;

        await using (var ctx = new QueenZoneDbContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.QueenHistoryEvents.AddRange(
                Entity(1, "Live Aid", new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc), isPublished: true),
                Entity(2, "Hidden", new DateTime(1991, 11, 24, 0, 0, 0, DateTimeKind.Utc), isPublished: false));
            await ctx.SaveChangesAsync();
        }

        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfQueenHistoryRepository(dbContext);

        var events = await repository.GetAllPublishedAsync();

        Assert.Single(events);
        Assert.Equal("Live Aid", events[0].Title);
    }

    [Fact]
    public async Task EfRepositoryIncludesAllDatePrecisions()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;

        await using (var ctx = new QueenZoneDbContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.QueenHistoryEvents.AddRange(
                Entity(1, "Exact", new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc), isPublished: true, precision: QueenHistoryDatePrecision.ExactDate),
                Entity(2, "Month", new DateTime(1975, 10, 1, 0, 0, 0, DateTimeKind.Utc), isPublished: true, precision: QueenHistoryDatePrecision.MonthYear),
                Entity(3, "Hidden", new DateTime(1991, 11, 24, 0, 0, 0, DateTimeKind.Utc), isPublished: false, precision: QueenHistoryDatePrecision.ExactDate));
            await ctx.SaveChangesAsync();
        }

        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfQueenHistoryRepository(dbContext);

        var events = await repository.GetAllPublishedAsync();

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e.Title == "Hidden");
    }

    private static QueenHistoryEvent Event(
        int id,
        string title,
        bool isPublished = true,
        QueenHistoryDatePrecision precision = QueenHistoryDatePrecision.ExactDate) =>
        new(id, title, "Summary",
            new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc),
            precision,
            QueenHistoryEventCategory.Other,
            80,
            QueenHistoryEventSourceType.Curated,
            $"test:{id}",
            null,
            isPublished);

    private static QueenHistoryEventEntity Entity(
        int id,
        string title,
        DateTime eventDate,
        bool isPublished = true,
        QueenHistoryDatePrecision precision = QueenHistoryDatePrecision.ExactDate) =>
        new()
        {
            Id = id,
            Title = title,
            Summary = "Summary",
            EventDate = eventDate,
            DatePrecision = precision,
            Category = QueenHistoryEventCategory.Other,
            Importance = 80,
            SourceType = QueenHistoryEventSourceType.Curated,
            SourceKey = $"test:{id}",
            IsPublished = isPublished,
            CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
}
