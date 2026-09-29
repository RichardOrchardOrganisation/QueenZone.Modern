using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfAdminFanPerformanceRepository"/> against a scratch
/// <c>Q_STAGE_T</c> (#1672 / #1888). The table matches the <c>queenzone_legacy_sync</c>
/// read-only dump of 2026-09-29 (see <see cref="LegacyFanPerformanceSchema"/>), including
/// <c>smallint</c> identity ids, <c>varchar(50) THESIZE</c>, and a nullable
/// <c>tinyint</c> <c>DISPLAY</c> that admin maps through
/// <c>CASE WHEN DISPLAY = 1 THEN CAST(1 AS bit)</c>. Covers list filters, paging,
/// get-by-id, insert/update/visibility/delete, and compare-and-swap concurrency.
/// There are no stored procedures — the repository uses inline SQL.
/// </summary>
public sealed class AdminFanPerformanceRepositorySqlServerTests : IAsyncLifetime
{
    private const string Editor = "editor@example.com";

    private static readonly DateTime BaseTime = new(2026, 8, 17, 10, 0, 0);

    private readonly string databaseName = $"QueenZoneAdminFanPerformanceTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfAdminFanPerformanceRepository repository = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync(LegacyFanPerformanceSchema.CreateTableSql);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfAdminFanPerformanceRepository(dbContext);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task GetPage_filters_orders_pages_and_materializes_legacy_columns()
    {
        var noSize = await InsertLegacyAsync("Unsized", "Unknown Band", null, "unsized.mp3", "abc",
            BaseTime.AddDays(-2), display: 1);
        var older = await CreateAsync("Liar", "Fan Band", "A cover.", "liar.mp3", 2048,
            BaseTime.AddDays(-1), isVisible: true, durationSeconds: 180);
        var tieLow = await CreateAsync("Red Special", "Brian", "Guitar.", "red.mp3", 1024,
            BaseTime, isVisible: true);
        var hidden = await CreateAsync("Hidden drum kit", "Private", "Not public.", "hidden.mp3", 99,
            BaseTime.AddDays(1), isVisible: false);
        var tieHigh = await CreateAsync("Brian at Wembley", "Live Band", "Stadium.", "wembley.mp3",
            5_120_835, BaseTime, isVisible: true);
        var nullDisplay = await InsertLegacyAsync("Null display", "Ghost", "Hidden by null.", "null.mp3",
            "10", BaseTime.AddDays(2), display: null);

        var all = await repository.GetPageAsync(new AdminFanPerformanceListFilter(), 1, 10);
        Assert.Equal(6, all.TotalCount);
        Assert.Equal([nullDisplay, hidden, tieHigh, tieLow, older, noSize], all.Items.Select(item => item.Id));
        Assert.Equal(1, all.Page);
        Assert.Equal(10, all.PageSize);

        var legacyItem = all.Items[^1];
        Assert.Equal("Unsized", legacyItem.Title);
        Assert.Equal(0, legacyItem.FileSizeBytes);
        Assert.True(legacyItem.IsVisible);
        Assert.Null(legacyItem.DurationSeconds);

        Assert.False(all.Items[0].IsVisible);
        Assert.False(all.Items[1].IsVisible);
        Assert.Equal(5_120_835, all.Items[2].FileSizeBytes);
        Assert.Equal(180, all.Items[4].DurationSeconds);

        var second = await repository.GetPageAsync(new AdminFanPerformanceListFilter(), 2, 1);
        Assert.Equal(6, second.TotalCount);
        Assert.Equal(hidden, Assert.Single(second.Items).Id);

        var visible = await repository.GetPageAsync(new AdminFanPerformanceListFilter(IsVisible: true), 1, 10);
        Assert.Equal([tieHigh, tieLow, older, noSize], visible.Items.Select(item => item.Id));
        Assert.All(visible.Items, item => Assert.True(item.IsVisible));

        var hiddenPage = await repository.GetPageAsync(new AdminFanPerformanceListFilter(IsVisible: false), 1, 10);
        Assert.Equal([nullDisplay, hidden], hiddenPage.Items.Select(item => item.Id));
        Assert.All(hiddenPage.Items, item => Assert.False(item.IsVisible));

        var byTitle = await repository.GetPageAsync(new AdminFanPerformanceListFilter(Search: " wembley "), 1, 10);
        Assert.Equal(tieHigh, Assert.Single(byTitle.Items).Id);
        var byPerformer = await repository.GetPageAsync(new AdminFanPerformanceListFilter(Search: "Brian"), 1, 10);
        Assert.Equal([tieHigh, tieLow], byPerformer.Items.Select(item => item.Id));
        var byDescription = await repository.GetPageAsync(new AdminFanPerformanceListFilter(Search: "cover"), 1, 10);
        Assert.Equal(older, Assert.Single(byDescription.Items).Id);

        var clamped = await repository.GetPageAsync(new AdminFanPerformanceListFilter(), 0, 0);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(1, clamped.PageSize);
        Assert.Equal(nullDisplay, Assert.Single(clamped.Items).Id);

        var loaded = await repository.GetByIdAsync(hidden);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsVisible);
        Assert.Equal("hidden.mp3", loaded.AudioFileName);
        Assert.Null(await repository.GetByIdAsync(404));
    }

    [Fact]
    public async Task Writes_round_trip_enforce_concurrency_and_hide_from_public_reads()
    {
        var id = await repository.CreateAsync(
            new AdminFanPerformanceCreateRequest(
                Title: "  Live Aid  ",
                PerformedBy: "  Test Band  ",
                Description: "  stage notes  ",
                AudioFileName: "  live-aid.mp3  ",
                FileSizeBytes: 4096,
                DateAdded: BaseTime,
                IsVisible: true,
                DurationSeconds: 240),
            Editor);

        var created = await repository.GetByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("Live Aid", created.Title);
        Assert.Equal("Test Band", created.PerformedBy);
        Assert.Equal("stage notes", created.Description);
        Assert.Equal("live-aid.mp3", created.AudioFileName);
        Assert.Equal(4096, created.FileSizeBytes);
        Assert.Equal(BaseTime, created.DateAdded);
        Assert.True(created.IsVisible);
        Assert.Equal(240, created.DurationSeconds);

        var publicRepo = new EfFanPerformanceRepository(dbContext);
        Assert.Equal(id, (await publicRepo.GetByIdAsync(id))!.Id);

        var token = created.ToConcurrencyToken();
        await repository.UpdateAsync(id,
            new AdminFanPerformanceUpdateRequest("Live Aid 1985", "Other Band", "", BaseTime.AddHours(1)),
            Editor, token);

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Live Aid 1985", updated.Title);
        Assert.Equal("Other Band", updated.PerformedBy);
        Assert.Equal(string.Empty, updated.Description);
        Assert.Equal(BaseTime.AddHours(1), updated.DateAdded);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.UpdateAsync(id,
            new AdminFanPerformanceUpdateRequest("Stale", "Other Band", "", BaseTime), Editor, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(404,
            new AdminFanPerformanceUpdateRequest("Missing", "x", "y", BaseTime), Editor));

        await repository.SetVisibilityAsync(id, false, Editor, expectedIsVisible: true);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            repository.SetVisibilityAsync(id, true, Editor, expectedIsVisible: true));
        Assert.False((await repository.GetByIdAsync(id))!.IsVisible);
        Assert.Null(await publicRepo.GetByIdAsync(id));

        await repository.DeleteAsync(id, Editor);
        Assert.Null(await repository.GetByIdAsync(id));
        await repository.DeleteAsync(id, Editor);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SetVisibilityAsync(id, true, Editor));
    }

    [Fact]
    public async Task Create_and_writes_require_editor_email()
    {
        var request = new AdminFanPerformanceCreateRequest(
            "Title", "Band", "Notes", "song.mp3", 1, BaseTime, true);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateAsync(request, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpdateAsync(
            1, new AdminFanPerformanceUpdateRequest("a", "b", "c", BaseTime), " "));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetVisibilityAsync(1, false, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.DeleteAsync(1, " "));
    }

    private Task<int> CreateAsync(
        string title,
        string performedBy,
        string description,
        string audioFileName,
        long fileSizeBytes,
        DateTime dateAdded,
        bool isVisible,
        int? durationSeconds = null) =>
        repository.CreateAsync(
            new AdminFanPerformanceCreateRequest(
                title, performedBy, description, audioFileName, fileSizeBytes, dateAdded, isVisible,
                durationSeconds),
            Editor);

    private async Task<int> InsertLegacyAsync(
        string title,
        string performedBy,
        string? description,
        string url,
        string theSize,
        DateTime dateAdded,
        int? display)
    {
        var ids = await dbContext.Database.SqlQueryRaw<int>(
            """
            INSERT INTO dbo.Q_STAGE_T
                (TITLE, PERFORMED_BY, DESCRIPTION, URL, THESIZE, DATE_ADDED, DISPLAY)
            OUTPUT CAST(INSERTED.Q_STAGE_ID AS int) AS Value
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})
            """,
            title,
            performedBy,
            description is string text ? text : DBNull.Value,
            url,
            theSize,
            dateAdded,
            display is int flag ? flag : DBNull.Value).ToListAsync();
        return ids.Single();
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy table comes from LegacyFanPerformanceSchema.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
