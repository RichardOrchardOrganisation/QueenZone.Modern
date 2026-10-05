using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfForumWriteRepositoryCategoryRaceTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;

    public EfForumWriteRepositoryCategoryRaceTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        CreateModernForumCategoryTable();
    }

    [Fact]
    public void IsLegacyForumIdUniqueViolation_RequiresSql2627AndConstraintName()
    {
        var matching = new DbUpdateException(
            "conflict",
            SqlExceptionFactory.Create(
                2627,
                $"Violation of UNIQUE KEY constraint '{EfForumWriteRepository.LegacyForumIdUniqueConstraintName}'."));
        Assert.True(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(matching));

        var nested = new DbUpdateException(
            "conflict",
            new InvalidOperationException(
                "wrapper",
                SqlExceptionFactory.Create(
                    2627,
                    $"Cannot insert duplicate key. The duplicate key value is (14). Constraint {EfForumWriteRepository.LegacyForumIdUniqueConstraintName}.")));
        Assert.True(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(nested));

        var otherConstraint = new DbUpdateException(
            "conflict",
            SqlExceptionFactory.Create(2627, "Violation of UNIQUE KEY constraint 'UQ_SomethingElse'."));
        Assert.False(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(otherConstraint));

        var uniqueIndex = new DbUpdateException(
            "conflict",
            SqlExceptionFactory.Create(
                2601,
                $"Cannot insert duplicate key row in object 'dbo.ModernForumCategory' with unique index '{EfForumWriteRepository.LegacyForumIdUniqueConstraintName}'."));
        Assert.False(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(uniqueIndex));

        var namedButWrongNumber = new DbUpdateException(
            $"conflict on {EfForumWriteRepository.LegacyForumIdUniqueConstraintName}",
            SqlExceptionFactory.Create(208, "Invalid object name."));
        Assert.False(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(namedButWrongNumber));

        var sqliteUnique = new DbUpdateException(
            "conflict",
            new InvalidOperationException("UNIQUE constraint failed: ModernForumCategory.LegacyForumId"));
        Assert.False(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(sqliteUnique));

        Assert.False(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(new DbUpdateException("conflict")));
        Assert.False(EfForumWriteRepository.IsLegacyForumIdUniqueViolation(
            new DbUpdateException("conflict", new TimeoutException("timeout"))));
    }

    [Fact]
    public async Task EnsureCategoryAsync_Forced2627_ReturnsExistingNewsCategory()
    {
        await SeedNamedCategoryAsync(1, "The Music");
        var interceptor = new ForceCategorySaveExceptionInterceptor
        {
            Exception = LegacyForumIdConflict(),
            Connection = connection,
            CompetingLegacyForumId = 2,
            CompetingName = NewsForumDiscussion.CategoryName,
        };
        await using var racingContext = CreateContext(interceptor);
        var repository = new EfForumWriteRepository(racingContext);

        var categoryId = await repository.EnsureCategoryAsync(
            NewsForumDiscussion.CategorySlug,
            NewsForumDiscussion.CategoryName);

        Assert.Equal(2, categoryId);
        Assert.Equal(1, interceptor.ForcedFailures);
        dbContext.ChangeTracker.Clear();
        var news = await dbContext.ModernForumCategories
            .SingleAsync(category => category.LegacyForumId == categoryId);
        Assert.Equal(NewsForumDiscussion.CategoryName, news.Name);
        Assert.False(NewsForumDiscussion.IsTheMusic(news.Name));
        Assert.Equal(2, await dbContext.ModernForumCategories.CountAsync());
    }

    [Fact]
    public async Task EnsureCategoryAsync_Forced2627WhenAnotherRowTookTheId_AllocatesNextIdOnce()
    {
        await SeedNamedCategoryAsync(1, "The Music");
        var interceptor = new ForceCategorySaveExceptionInterceptor
        {
            Exception = LegacyForumIdConflict(),
            Connection = connection,
            CompetingLegacyForumId = 2,
            CompetingName = "General",
        };
        await using var racingContext = CreateContext(interceptor);
        var repository = new EfForumWriteRepository(racingContext);

        var categoryId = await repository.EnsureCategoryAsync(
            NewsForumDiscussion.CategorySlug,
            NewsForumDiscussion.CategoryName);

        Assert.Equal(3, categoryId);
        Assert.Equal(1, interceptor.ForcedFailures);
        dbContext.ChangeTracker.Clear();
        var news = await dbContext.ModernForumCategories
            .SingleAsync(category => category.LegacyForumId == categoryId);
        Assert.Equal(NewsForumDiscussion.CategoryName, news.Name);
        Assert.False(NewsForumDiscussion.IsTheMusic(news.Name));
        Assert.Equal(
            "General",
            (await dbContext.ModernForumCategories.SingleAsync(category => category.LegacyForumId == 2)).Name);
    }

    [Fact]
    public async Task EnsureCategoryAsync_UnrelatedDbUpdateException_IsNotSwallowed()
    {
        await SeedNamedCategoryAsync(1, "The Music");
        var failure = new DbUpdateException(
            "save failed",
            SqlExceptionFactory.Create(547, "The INSERT statement conflicted with the FOREIGN KEY constraint"));
        var interceptor = new ForceCategorySaveExceptionInterceptor { Exception = failure };
        await using var racingContext = CreateContext(interceptor);
        var repository = new EfForumWriteRepository(racingContext);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => repository.EnsureCategoryAsync(
            NewsForumDiscussion.CategorySlug,
            NewsForumDiscussion.CategoryName));

        Assert.Same(failure, thrown);
        dbContext.ChangeTracker.Clear();
        Assert.DoesNotContain(
            await dbContext.ModernForumCategories.ToListAsync(),
            category => NewsForumDiscussion.MatchesNewsCategory(category.Name));
    }

    [Fact]
    public async Task EnsureCategoryAsync_RetryInsertUniqueFailure_IsNotSwallowed()
    {
        await SeedNamedCategoryAsync(1, "The Music");
        var failure = LegacyForumIdConflict();
        var interceptor = new ForceCategorySaveExceptionInterceptor
        {
            Exception = failure,
            MaxForcedFailures = 2,
        };
        await using var racingContext = CreateContext(interceptor);
        var repository = new EfForumWriteRepository(racingContext);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => repository.EnsureCategoryAsync(
            NewsForumDiscussion.CategorySlug,
            NewsForumDiscussion.CategoryName));

        Assert.Same(failure, thrown);
        Assert.Equal(2, interceptor.ForcedFailures);
        dbContext.ChangeTracker.Clear();
        Assert.DoesNotContain(
            await dbContext.ModernForumCategories.ToListAsync(),
            category => NewsForumDiscussion.MatchesNewsCategory(category.Name));
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private QueenZoneDbContext CreateContext(IInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options);

    private async Task SeedNamedCategoryAsync(int legacyForumId, string name)
    {
        dbContext.ModernForumCategories.Add(new ModernForumCategoryEntity
        {
            LegacyForumId = legacyForumId,
            Name = name,
            SortOrder = legacyForumId,
            LegacyPostCount = 0,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private void CreateModernForumCategoryTable()
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumCategory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyForumId INTEGER NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL,
                LegacyPostCount INTEGER NOT NULL,
                LastActivityAt TEXT NULL,
                IsSynthetic INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);
    }

    private static DbUpdateException LegacyForumIdConflict() =>
        new(
            "conflict",
            SqlExceptionFactory.Create(
                2627,
                $"Violation of UNIQUE KEY constraint '{EfForumWriteRepository.LegacyForumIdUniqueConstraintName}'. Cannot insert duplicate key in object 'dbo.ModernForumCategory'. The duplicate key value is (2)."));

    private sealed class ForceCategorySaveExceptionInterceptor : SaveChangesInterceptor
    {
        public required Exception Exception { get; init; }

        public SqliteConnection? Connection { get; init; }

        public int? CompetingLegacyForumId { get; init; }

        public string? CompetingName { get; init; }

        public int MaxForcedFailures { get; init; } = 1;

        public int ForcedFailures { get; private set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfCategoryInsert(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfCategoryInsert(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfCategoryInsert(DbContext? context)
        {
            if (ForcedFailures >= MaxForcedFailures
                || context is null
                || !context.ChangeTracker.Entries<ModernForumCategoryEntity>()
                    .Any(entry => entry.State == EntityState.Added))
            {
                return;
            }

            InsertCompetingRow();
            ForcedFailures++;
            throw Exception;
        }

        private void InsertCompetingRow()
        {
            if (Connection is null || CompetingLegacyForumId is null || string.IsNullOrWhiteSpace(CompetingName))
            {
                return;
            }

            var now = DateTime.UtcNow.ToString("o");
            using var command = Connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ModernForumCategory
                    (LegacyForumId, Name, Description, SortOrder, LegacyPostCount, IsSynthetic, ImportedAt, UpdatedAt)
                VALUES
                    ($id, $name, 'Competing insert', $id, 0, 0, $now, $now);
                """;
            command.Parameters.AddWithValue("$id", CompetingLegacyForumId.Value);
            command.Parameters.AddWithValue("$name", CompetingName);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }
    }
}
