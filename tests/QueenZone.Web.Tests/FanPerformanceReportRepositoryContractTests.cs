using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class FanPerformanceReportRepositoryContractTests
{
    protected abstract IFanPerformanceReportRepository Repository { get; }
    protected static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid OtherMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static MemberAccount Member(Guid id) => new() { Id = id, DisplayName = id == MemberId ? "Fan" : "Other", Email = $"{id:N}@example.test", NormalizedEmail = $"{id:N}@EXAMPLE.TEST", CreatedAt = new DateTime(2026, 9, 1) };
    protected static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private Task<FanPerformanceReportCreateResult> Create(int stage = 1) => Repository.CreateAsync(new(stage, MemberId, "Reason", "Title", "Band"));

    [Fact]
    public async Task Create_DeduplicatesOpenReports_AndAllowsNewReportAfterResolution()
    {
        var created = await Create();
        Assert.Equal(FanPerformanceReportOutcome.Created, created.Outcome);
        var row = await Repository.GetByIdAsync(created.ReportId!.Value);
        Assert.Equal(FanPerformanceReportStatus.Open, row!.Status);
        Assert.Equal("Fan", row.ReporterDisplayName);
        Assert.Equal("Reason", row.Reason);
        Assert.Equal("Title", row.TitleSnapshot);
        Assert.Equal("Band", row.PerformedBySnapshot);
        Assert.NotEqual(default, row.CreatedAt);
        var duplicate = await Create();
        Assert.Equal(FanPerformanceReportOutcome.AlreadyReported, duplicate.Outcome);
        Assert.Equal(created.ReportId, duplicate.ReportId);
        Assert.Equal(1, await Repository.CountOpenAsync());
        await Repository.UpdateStatusAsync(row.Id, FanPerformanceReportStatus.Resolved, "editor");
        row = await Repository.GetByIdAsync(row.Id);
        Assert.Equal(FanPerformanceReportStatus.Resolved, row!.Status);
        Assert.Equal("editor", row.ReviewedBy);
        Assert.NotNull(row.ReviewedAt);
        Assert.Equal(0, await Repository.CountOpenAsync());
        Assert.Equal(FanPerformanceReportOutcome.Created, (await Create()).Outcome);
    }

    [Fact]
    public async Task Lists_FilterAndPage_AndMissingReturnsNull()
    {
        var first = await Create();
        var second = await Create(2);
        var third = await Create(3);
        await Repository.UpdateStatusAsync(first.ReportId!.Value, FanPerformanceReportStatus.Dismissed, "editor");
        var page = await Repository.ListAsync(null, 2, 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.ReportId, Assert.Single(page.Items).Id);
        Assert.Equal(third.ReportId, Assert.Single((await Repository.ListAsync(null, 0, 0)).Items).Id);
        Assert.Equal(first.ReportId, Assert.Single((await Repository.ListAsync(FanPerformanceReportStatus.Dismissed, 1, 10)).Items).Id);
        Assert.Equal(3, (await Repository.ListAsync("all", 1, 10)).TotalCount);
        Assert.Null(await Repository.GetByIdAsync(Guid.NewGuid()));
        Assert.Null(await Repository.UpdateStatusAsync(Guid.NewGuid(), FanPerformanceReportStatus.Resolved, "editor"));
    }
    [Fact]
    public async Task Lists_CapPageSizeAtTwoHundred()
    {
        for (var stage = 1; stage <= 201; stage++)
        {
            await Create(stage);
        }
        var page = await Repository.ListAsync("all", 1, 999);
        Assert.Equal(201, page.TotalCount);
        Assert.Equal(200, page.Items.Count);
        Assert.Single((await Repository.ListAsync("all", 2, 999)).Items);
        Assert.Equal(201, await Repository.CountOpenAsync());
    }

}

public sealed class InMemoryFanPerformanceReportRepositoryContractTests : FanPerformanceReportRepositoryContractTests
{
    protected override IFanPerformanceReportRepository Repository { get; } = new InMemoryFanPerformanceReportRepository(id => Member(id));
}

public sealed class EfFanPerformanceReportRepositoryContractTests : FanPerformanceReportRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext context;
    protected override IFanPerformanceReportRepository Repository { get; }

    public EfFanPerformanceReportRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.MemberAccounts.AddRange(Member(MemberId), Member(OtherMemberId));
        context.SaveChanges();
        Repository = new EfFanPerformanceReportRepository(context);
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
