using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>Exercises the production admin CTE and SQL Server OFFSET/FETCH over legacy NEWS_T.</summary>
public sealed class AdminNewsPagingSqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneAdminNewsPaging_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using var schema = new EmptySchemaContext(new DbContextOptionsBuilder<EmptySchemaContext>()
            .UseSqlServer(ConnectionString).Options);
        await schema.Database.EnsureCreatedAsync();
        // Legacy types are taken from NEWS_T on the SQL Express mirror. NEWS_ID has no key:
        // duplicate physical rows are resolved by the production latest-row CTE.
        await schema.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.NEWS_T (
                NEWS_ID int NOT NULL, TITLE varchar(150) NULL, EXCERPT varchar(800) NULL,
                ARTICLE varchar(max) NULL, [DATE] smalldatetime NOT NULL, DISPLAY tinyint NOT NULL,
                SOURCE_URL varchar(500) NULL, SLUG nvarchar(200) NULL,
                CREATED_AT datetime2 NULL, UPDATED_AT datetime2 NULL, EDITOR_EMAIL nvarchar(256) NULL,
                USER_ID int NULL, TYPE smallint NULL, QUEEN_ONLINE tinyint NULL,
                IMAGE_BLOB_KEY nvarchar(500) NULL, IMAGE_GALLERY_PIC_ID int NULL, FORUM_TOPIC_ID int NULL);
            INSERT INTO dbo.NEWS_T (NEWS_ID, TITLE, EXCERPT, ARTICLE, [DATE], DISPLAY) VALUES
                (1, 'First old', '', '', '2024-01-01', 1),
                (1, 'First latest', '', '', '2026-01-01', 0),
                (2, 'Second', '', '', '2026-02-01', 1),
                (3, 'Third', '', '', '2026-02-01', 0),
                (4, 'Fourth', '', '', '2025-01-01', 1);
            """);
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (dbContext is not null)
        {
            await dbContext.DisposeAsync();
        }
        await using var schema = new EmptySchemaContext(new DbContextOptionsBuilder<EmptySchemaContext>()
            .UseSqlServer(ConnectionString).Options);
        await schema.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task GetPage_deduplicates_orders_ties_and_pages_drafts_with_sql_server()
    {
        var repository = new EfAdminNewsRepository(dbContext);
        var first = await repository.GetPageAsync(1, 2);
        var second = await repository.GetPageAsync(2, 2);
        var beyond = await repository.GetPageAsync(3, 2);

        Assert.Equal(4, first.TotalCount);
        Assert.Equal([3, 2], first.Items.Select(item => item.Id));
        Assert.Equal([1, 4], second.Items.Select(item => item.Id));
        Assert.Empty(beyond.Items);
        Assert.Equal("First latest", second.Items[0].Title);
        Assert.False(first.Items[0].IsPublished);
    }

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
