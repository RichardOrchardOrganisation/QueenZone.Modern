using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class CrosswordEntityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configures_modern_tables_limits_unique_keys_and_provider_concurrency(bool sqlServer)
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>();
        if (sqlServer)
        {
            options.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True;TrustServerCertificate=True");
        }
        else
        {
            options.UseSqlite("Data Source=:memory:");
        }
        using var db = new QueenZoneDbContext(options.Options);
        var puzzle = db.Model.FindEntityType(typeof(CrosswordEntity))!;
        var entry = db.Model.FindEntityType(typeof(CrosswordEntryEntity))!;
        Assert.Equal("Crosswords", puzzle.GetTableName());
        Assert.Equal("CrosswordEntries", entry.GetTableName());
        AssertLimit(puzzle, nameof(CrosswordEntity.Slug), 100);
        AssertLimit(puzzle, nameof(CrosswordEntity.Title), 200);
        AssertLimit(puzzle, nameof(CrosswordEntity.Description), 1000);
        AssertLimit(puzzle, nameof(CrosswordEntity.Difficulty), 20);
        AssertLimit(puzzle, nameof(CrosswordEntity.Style), 20);
        AssertLimit(puzzle, nameof(CrosswordEntity.BlockMask), 225);
        AssertLimit(puzzle, nameof(CrosswordEntity.SolutionRowsJson), 2000);
        AssertLimit(puzzle, nameof(CrosswordEntity.UpdatedByEmail), 320);
        AssertLimit(entry, nameof(CrosswordEntryEntity.Answer), 15);
        AssertLimit(entry, nameof(CrosswordEntryEntity.Clue), 500);
        AssertLimit(entry, nameof(CrosswordEntryEntity.Enumeration), 50);
        Assert.Equal(300, entry.FindProperty(nameof(CrosswordEntryEntity.Explanation))!.GetMaxLength());
        Assert.True(entry.FindProperty(nameof(CrosswordEntryEntity.Explanation))!.IsNullable);
        Assert.Contains(puzzle.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(CrosswordEntity.Slug)]));
        Assert.Contains(entry.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(CrosswordEntryEntity.CrosswordId), nameof(CrosswordEntryEntity.Number), nameof(CrosswordEntryEntity.Direction)]));
        var token = puzzle.FindProperty(nameof(CrosswordEntity.RowVersion))!;
        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(sqlServer ? ValueGenerated.OnAddOrUpdate : ValueGenerated.Never, token.ValueGenerated);
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(entry.GetForeignKeys()).DeleteBehavior);
        var progress = db.Model.FindEntityType(typeof(CrosswordProgressEntity))!;
        var completion = db.Model.FindEntityType(typeof(CrosswordCompletionEntity))!;
        Assert.Equal("CrosswordProgress", progress.GetTableName());
        Assert.Equal("CrosswordCompletions", completion.GetTableName());
        AssertLimit(progress, nameof(CrosswordProgressEntity.GridFingerprint), 64);
        AssertLimit(progress, nameof(CrosswordProgressEntity.Letters), 225);
        AssertLimit(progress, nameof(CrosswordProgressEntity.RevealedCellsJson), 2000);
        var progressToken = progress.FindProperty(nameof(CrosswordProgressEntity.RowVersion))!;
        Assert.True(progressToken.IsConcurrencyToken);
        Assert.Equal(sqlServer ? ValueGenerated.OnAddOrUpdate : ValueGenerated.Never, progressToken.ValueGenerated);
        foreach (var type in new[] { progress, completion })
        {
            Assert.Contains(type.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(["CrosswordId", "MemberId"]));
            var foreignKey = Assert.Single(type.GetForeignKeys());
            Assert.Equal(typeof(CrosswordEntity), foreignKey.PrincipalEntityType.ClrType);
            Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        }
    }

    [Fact]
    public async Task Sqlite_round_trip_preserves_crossword_metadata_entries_and_token()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var db = new QueenZoneDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        var puzzle = new CrosswordEntity
        {
            Id = Guid.NewGuid(),
            Slug = "meet-the-band",
            Title = "Meet the Band",
            Description = "A starter puzzle",
            Difficulty = "easy",
            Style = "british",
            Width = 5,
            Height = 5,
            BlockMask = ".....####################",
            Status = CrosswordStatus.Scheduled,
            PublishAt = now.AddDays(1),
            PublishedAt = now,
            CreatedAt = now,
            CreatedByMemberId = Guid.NewGuid(),
            UpdatedAt = now,
            UpdatedByEmail = "admin@test.local",
            RowVersion = Guid.NewGuid().ToByteArray(),
            Entries = [new CrosswordEntryEntity
            {
                Id = Guid.NewGuid(), Number = 1, Direction = CrosswordDirection.Across, Row = 0, Column = 0,
                Answer = "BRIAN", Clue = "Queen guitarist's given name", Enumeration = "(5)", Explanation = "Brian May."
            }]
        };
        db.Crosswords.Add(puzzle);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.Crosswords.Include(item => item.Entries).SingleAsync();
        Assert.Equal(puzzle.Id, loaded.Id);
        Assert.Equal(puzzle.Slug, loaded.Slug);
        Assert.Equal(puzzle.Title, loaded.Title);
        Assert.Equal(puzzle.Description, loaded.Description);
        Assert.Equal(puzzle.Difficulty, loaded.Difficulty);
        Assert.Equal(puzzle.Style, loaded.Style);
        Assert.Equal(puzzle.Width, loaded.Width);
        Assert.Equal(puzzle.Height, loaded.Height);
        Assert.Equal(puzzle.BlockMask, loaded.BlockMask);
        Assert.Equal(puzzle.SolutionRowsJson, loaded.SolutionRowsJson);
        Assert.Equal(puzzle.Status, loaded.Status);
        Assert.Equal(puzzle.PublishAt, loaded.PublishAt);
        Assert.Equal(puzzle.PublishedAt, loaded.PublishedAt);
        Assert.Equal(puzzle.CreatedAt, loaded.CreatedAt);
        Assert.Equal(puzzle.CreatedByMemberId, loaded.CreatedByMemberId);
        Assert.Equal(puzzle.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(puzzle.UpdatedByEmail, loaded.UpdatedByEmail);
        Assert.Equal(puzzle.RowVersion, loaded.RowVersion);
        var entry = Assert.Single(loaded.Entries);
        var expected = Assert.Single(puzzle.Entries);
        Assert.Equal(expected.Id, entry.Id);
        Assert.Equal(loaded.Id, entry.CrosswordId);
        Assert.Equal(expected.Number, entry.Number);
        Assert.Equal(expected.Direction, entry.Direction);
        Assert.Equal(expected.Row, entry.Row);
        Assert.Equal(expected.Column, entry.Column);
        Assert.Equal(expected.Answer, entry.Answer);
        Assert.Equal(expected.Clue, entry.Clue);
        Assert.Equal(expected.Enumeration, entry.Enumeration);
        Assert.Equal(expected.Explanation, entry.Explanation);
        Assert.Same(loaded, entry.Crossword);
    }

    [Fact]
    public async Task Sqlite_enforces_unique_slugs_and_entry_numbers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using var db = new QueenZoneDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var puzzle = MinimalPuzzle();
        db.Crosswords.Add(puzzle);
        await db.SaveChangesAsync();
        db.Crosswords.Add(MinimalPuzzle());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.CrosswordEntries.AddRange(
            new CrosswordEntryEntity { Id = Guid.NewGuid(), CrosswordId = puzzle.Id, Number = 1, Direction = CrosswordDirection.Down },
            new CrosswordEntryEntity { Id = Guid.NewGuid(), CrosswordId = puzzle.Id, Number = 1, Direction = CrosswordDirection.Down });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(5, 16)]
    public async Task Sqlite_enforces_size_constraints(int width, int height)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var puzzle = MinimalPuzzle();
        puzzle.Width = width;
        puzzle.Height = height;
        db.Crosswords.Add(puzzle);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static CrosswordEntity MinimalPuzzle() => new()
    {
        Id = Guid.NewGuid(),
        Slug = "unique-slug",
        Width = 5,
        Height = 5,
        RowVersion = Guid.NewGuid().ToByteArray()
    };

    private static void AssertLimit(IEntityType type, string field, int maximum)
    {
        var property = type.FindProperty(field)!;
        Assert.Equal(maximum, property.GetMaxLength());
        Assert.False(property.IsNullable);
    }
}
