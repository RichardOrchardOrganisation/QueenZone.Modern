using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class SubmissionDashboardQueriesTests
{
    [Fact]
    public async Task CountsInMemory_UsesUtcDayWeekAndThirtyDayWindows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DashboardDbContext(new DbContextOptionsBuilder<DashboardDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-09-27T10:00:00+08:00");
        db.Rows.AddRange(
            new DashboardRow { Id = 1, SubmittedAt = now, IsOpen = true, IsStillPending = true },
            new DashboardRow { Id = 2, SubmittedAt = now.AddDays(-6), IsApproved = true },
            new DashboardRow { Id = 3, SubmittedAt = now.AddDays(-30), IsRejected = true },
            new DashboardRow { Id = 4, SubmittedAt = now.AddDays(-31), IsOpen = true },
            new DashboardRow { Id = 5, IsOpen = true });
        await db.SaveChangesAsync();

        var counts = await db.Rows.Select(row => new SubmissionCountRow
        {
            SubmittedAt = row.SubmittedAt,
            IsOpen = row.IsOpen,
            IsApproved = row.IsApproved,
            IsRejected = row.IsRejected,
            IsStillPending = row.IsStillPending,
        }).ToDashboardCountsAsync(now, aggregateInSql: false, CancellationToken.None);

        Assert.Equal(3, counts.Pending);
        Assert.Equal(1, counts.ReceivedToday);
        Assert.Equal(2, counts.ReceivedThisWeek);
        Assert.Equal(1, counts.ApprovedLast30Days);
        Assert.Equal(1, counts.RejectedLast30Days);
        Assert.Equal(1, counts.StillPendingFromLast30Days);
        Assert.True(db.Database.IsSqliteProvider());
    }

    [Fact]
    public async Task ContributorsInMemory_ExcludesOldAndUndatedRowsAndLimitsRank()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DashboardDbContext(new DbContextOptionsBuilder<DashboardDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var monthStart = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        var frequent = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        db.Rows.AddRange(
            new DashboardRow { Id = 1, MemberId = frequent, DisplayName = null, SubmittedAt = monthStart },
            new DashboardRow { Id = 2, MemberId = frequent, DisplayName = "Freddie", SubmittedAt = monthStart.AddDays(1) },
            new DashboardRow { Id = 3, MemberId = unknown, DisplayName = " ", SubmittedAt = monthStart.AddDays(2) },
            new DashboardRow { Id = 4, MemberId = Guid.NewGuid(), SubmittedAt = monthStart.AddDays(-1) },
            new DashboardRow { Id = 5, MemberId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var contributors = await db.Rows.Select(row => new SubmissionContributorRow
        {
            MemberId = row.MemberId,
            DisplayName = row.DisplayName,
            SubmittedAt = row.SubmittedAt,
        }).ToTopContributorsAsync(monthStart, maxCount: 2, aggregateInSql: false, CancellationToken.None);

        Assert.Equal(2, contributors.Count);
        Assert.Equal(frequent, contributors[0].MemberId);
        Assert.Equal("Freddie", contributors[0].DisplayName);
        Assert.Equal(2, contributors[0].Count);
        Assert.Equal("Unknown member", contributors[1].DisplayName);
    }

    private sealed class DashboardDbContext(DbContextOptions<DashboardDbContext> options) : DbContext(options)
    {
        public DbSet<DashboardRow> Rows => Set<DashboardRow>();
    }

    private sealed class DashboardRow
    {
        public int Id { get; set; }
        public Guid MemberId { get; set; }
        public string? DisplayName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public bool IsOpen { get; set; }
        public bool IsApproved { get; set; }
        public bool IsRejected { get; set; }
        public bool IsStillPending { get; set; }
    }
}
