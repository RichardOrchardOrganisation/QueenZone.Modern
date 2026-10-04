using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>Applies the actual crossword migration to a disposable SQL Server database, never legacy or production rows.</summary>
public sealed class CrosswordMigrationSqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneCrosswordTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext db = null!;

    private string ConnectionString => new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
        ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
    {
        InitialCatalog = databaseName
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var empty = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>().UseSqlServer(ConnectionString).Options);
        await empty.Database.EnsureCreatedAsync();
        db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure()).Options);
        var operations = new AddCrosswords().UpOperations.Concat(new AddCrosswordDraftRows().UpOperations)
            .Concat(new AddCrosswordProgress().UpOperations).ToArray();
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model);
        foreach (var command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }

    public async Task DisposeAsync()
    {
        if (db is not null)
        {
            await db.DisposeAsync();
        }
        await using var empty = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>().UseSqlServer(ConnectionString).Options);
        await empty.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Migration_round_trip_and_server_rowversion_rejects_a_stale_writer()
    {
        var puzzle = new CrosswordEntity
        {
            Id = Guid.NewGuid(), Slug = "sql-crossword", Title = "SQL crossword", Description = "Migration fixture",
            Width = 5, Height = 5, Difficulty = "easy", Style = "british", BlockMask = ".....####################",
            Status = CrosswordStatus.Draft, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Entries = [new CrosswordEntryEntity
            {
                Id = Guid.NewGuid(), Number = 1, Direction = CrosswordDirection.Across, Row = 0, Column = 0,
                Answer = "BRIAN", Clue = "Queen guitarist's first name", Enumeration = "(5)", Explanation = "Brian May."
            }]
        };
        db.Crosswords.Add(puzzle);
        await db.SaveChangesAsync();
        Assert.NotEmpty(puzzle.RowVersion);
        await using var stale = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(ConnectionString).Options);
        var other = await stale.Crosswords.SingleAsync();
        var originalVersion = puzzle.RowVersion.ToArray();
        puzzle.Title = "Edited";
        await db.SaveChangesAsync();
        Assert.False(originalVersion.SequenceEqual(puzzle.RowVersion));
        other.Title = "Stale edit";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var loaded = await db.Crosswords.Include(item => item.Entries).SingleAsync();
        Assert.Equal("Edited", loaded.Title);
        var entry = Assert.Single(loaded.Entries);
        Assert.Equal("BRIAN", entry.Answer);
        Assert.Equal("(5)", entry.Enumeration);
        Assert.Equal("Brian May.", entry.Explanation);
        db.Crosswords.Remove(loaded);
        await db.SaveChangesAsync();
        Assert.Empty(await db.CrosswordEntries.ToListAsync());
    }

    [Fact]
    public async Task Catalog_import_and_draft_edits_use_retry_safe_transactions_and_server_rowversions()
    {
        var catalog = new EfCrosswordCatalogRepository(db, TimeProvider.System);
        var seeds = CrosswordSampleData.Load();
        var imported = await catalog.ImportAsync(seeds, Guid.NewGuid(), "sql-import");
        Assert.Equal(10, imported.Imported.Count);
        var repeated = await catalog.ImportAsync(seeds, Guid.NewGuid(), "sql-import");
        Assert.Empty(repeated.Imported);
        Assert.Equal(10, repeated.Skipped.Count);
        var original = (await catalog.GetAllAsync())[0];
        var edited = original.Seed with { Title = "SQL draft edit" };
        await catalog.SaveDraftAsync(original.Id, edited, original.RowVersion, "sql-editor");
        var saved = await catalog.GetByIdAsync(original.Id);
        Assert.NotNull(saved);
        Assert.Equal("SQL draft edit", saved.Seed.Title);
        Assert.False(original.RowVersion.SequenceEqual(saved.RowVersion));
        Assert.Equal(original.Seed.Grid.Rows, saved.Seed.Grid.Rows);
        Assert.Equal(original.Seed.Grid.Clues, saved.Seed.Grid.Clues);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            catalog.SaveDraftAsync(original.Id, original.Seed, original.RowVersion, "stale-editor"));
        Assert.Equal(2, (await catalog.GetAuditAsync(original.Id)).Count);
        await catalog.SetPublicationAsync(original.Id, CrosswordStatus.Published, null, saved.RowVersion, "sql-publisher");
        var published = (await catalog.GetByIdAsync(original.Id))!;
        Assert.True(CrosswordVisibility.IsListed(published, DateTimeOffset.UtcNow));
        await catalog.SetPublicationAsync(original.Id, CrosswordStatus.Archived, null, published.RowVersion, "sql-publisher");
        var archived = (await catalog.GetByIdAsync(original.Id))!;
        Assert.False(CrosswordVisibility.IsListed(archived, DateTimeOffset.UtcNow));
        Assert.True(CrosswordVisibility.IsPlayable(archived, DateTimeOffset.UtcNow));
        Assert.Equal(4, (await catalog.GetAuditAsync(original.Id)).Count);
        db.Crosswords.Remove(await db.Crosswords.SingleAsync(item => item.Id == original.Id));
        await db.SaveChangesAsync();
        Assert.Empty(await catalog.GetAuditAsync(original.Id));
    }

    [Fact]
    public async Task Progress_transactions_preserve_assists_first_completion_and_server_rowversion()
    {
        var catalog = new EfCrosswordCatalogRepository(db, TimeProvider.System);
        await catalog.ImportAsync([CrosswordSampleData.Load()[0]], Guid.NewGuid(), "sql-import", publish: true);
        var puzzle = Assert.Single(await catalog.GetAllAsync());
        var member = Guid.NewGuid();
        var repository = new EfCrosswordProgressRepository(db, TimeProvider.System, new());
        var now = DateTimeOffset.UtcNow;
        var write = new CrosswordProgressWrite(CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), 120, [], false, now);
        await repository.SaveAsync(puzzle.Id, member, write);
        var originalVersion = (await db.CrosswordProgress.AsNoTracking().SingleAsync()).RowVersion;
        Assert.NotEmpty(originalVersion);
        var cell = CrosswordPlayRules.SelectCells(puzzle.Seed.Grid, new("grid"))[0];
        await repository.MarkAssistanceAsync(puzzle.Id, member, [cell], true);
        var assistedVersion = (await db.CrosswordProgress.AsNoTracking().SingleAsync()).RowVersion;
        Assert.False(originalVersion.SequenceEqual(assistedVersion));
        await repository.SaveAsync(puzzle.Id, member, write with { ElapsedSeconds = 1, UpdatedAt = now.AddSeconds(-1) });
        var saved = (await repository.GetAsync(puzzle.Id, member))!;
        Assert.Equal(120, saved.ElapsedSeconds);
        Assert.Equal(new[] { cell }, saved.RevealedCells);
        Assert.True(saved.AutoCheckUsed);
        var solved = write with { Letters = string.Concat(puzzle.Seed.Grid.Rows), UpdatedAt = now.AddSeconds(1) };
        var first = await repository.CompleteAsync(puzzle.Id, member, solved);
        Assert.True(first.Correct);
        Assert.False(first.Completion!.Clean);
        Assert.False(first.Completion.RankingEligible);
        var repeat = await repository.CompleteAsync(puzzle.Id, member, solved with { ElapsedSeconds = 240, UpdatedAt = now.AddMinutes(1) });
        Assert.Equal(first.Completion, repeat.Completion);
        Assert.Single(await db.CrosswordCompletions.ToListAsync());
        db.Crosswords.Remove(await db.Crosswords.SingleAsync());
        await db.SaveChangesAsync();
        Assert.Empty(await db.CrosswordProgress.ToListAsync());
        Assert.Empty(await db.CrosswordCompletions.ToListAsync());
    }

    private sealed class EmptyContext(DbContextOptions<EmptyContext> options) : DbContext(options);
}
