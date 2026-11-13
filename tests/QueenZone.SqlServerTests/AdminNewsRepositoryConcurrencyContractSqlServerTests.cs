using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

public sealed class AdminNewsRepositoryConcurrencyContractSqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneNewsConcurrency_{Guid.NewGuid():N}";
    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source) { InitialCatalog = databaseName }.ConnectionString;
        }
    }

    private QueenZoneDbContext CreateContext() => new(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(ConnectionString).Options);
    private EmptySchemaContext CreateSchemaContext() => new(new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options);
    private static AdminNewsDraft Draft(string title) => new(title, null, "Excerpt", "Body", new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), null);

    public async Task InitializeAsync()
    {
        LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();
        await using var schema = CreateSchemaContext();
        await schema.Database.EnsureCreatedAsync();
        await schema.Database.ExecuteSqlRawAsync(LegacyNewsArchiveSchema.CreateTableSql);
    }

    [Fact]
    public async Task Update_in_first_context_rejects_second_context_stale_update()
    {
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        IAdminNewsRepository first = new EfAdminNewsRepository(firstContext);
        IAdminNewsRepository second = new EfAdminNewsRepository(secondContext);
        var id = await first.CreateDraftAsync(Draft("Original"), "editor");
        var original = (await second.GetByIdAsync(id))!.UpdatedAt;
        Assert.NotNull(original);
        await first.UpdateAsync(id, Draft("First writer"), "first", original);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => second.UpdateAsync(id, Draft("Second writer"), "second", original));
        var stored = await second.GetByIdAsync(id);
        Assert.Equal("First writer", stored!.Title);
        Assert.False(stored.IsPublished);
        Assert.NotEqual(original, stored.UpdatedAt);
    }

    [Fact]
    public async Task Publish_in_first_context_rejects_second_context_stale_lifecycle_writes()
    {
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        IAdminNewsRepository first = new EfAdminNewsRepository(firstContext);
        IAdminNewsRepository second = new EfAdminNewsRepository(secondContext);
        var id = await first.CreateDraftAsync(Draft("First title"), "editor");
        var original = (await second.GetByIdAsync(id))!.UpdatedAt;
        Assert.NotNull(original);
        await first.PublishAsync(id, "first", original);
        Func<Task>[] staleOperations = [
            () => second.PublishAsync(id, "second", original),
            () => second.UnpublishAsync(id, "second", original),
            () => second.DeleteAsync(id, "second", original),
            () => second.UpdateAsync(id, Draft("Stale title"), "second", original)];
        foreach (var operation in staleOperations)
        {
            await Assert.ThrowsAsync<OptimisticConcurrencyException>(operation);
            var stored = await second.GetByIdAsync(id);
            Assert.Equal("First title", stored!.Title);
            Assert.True(stored.IsPublished);
            Assert.NotEqual(original, stored.UpdatedAt);
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var schema = CreateSchemaContext();
            await schema.Database.EnsureDeletedAsync();
        }
        finally
        {
            LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();
        }
    }

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
