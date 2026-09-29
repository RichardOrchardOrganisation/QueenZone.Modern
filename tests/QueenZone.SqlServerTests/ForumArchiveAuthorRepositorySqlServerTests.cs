using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfForumArchiveAuthorRepository"/> constructor
/// (<see cref="EfProductionSql.CreateForumArchiveAuthorSummarySql"/>) against a
/// scratch <c>dbo.ModernForumArchiveAuthorSummary</c> (#1672 / #1890). Column types
/// come from the committed EF migration
/// <c>20260914070000_AddModernForumArchiveAuthorSummary</c> /
/// <c>docs/sql/011-modern-forum-archive-author-summary.sql</c>: <c>LegacyUserId int</c>,
/// <c>DisplayName nvarchar(100)</c>, <c>MemberSince datetime2(0) NULL</c>,
/// <c>PostCount int</c>. This is a modern projection table, so there is no
/// read-only legacy-mirror probe.
/// </summary>
public sealed class ForumArchiveAuthorRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneForumArchiveAuthorTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfForumArchiveAuthorRepository repository = null!;

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
            await schema.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE dbo.ModernForumArchiveAuthorSummary
                (
                    LegacyUserId int NOT NULL,
                    DisplayName nvarchar(100) NOT NULL,
                    MemberSince datetime2(0) NULL,
                    PostCount int NOT NULL,
                    RefreshedAt datetime2(0) NOT NULL
                        CONSTRAINT DF_ModernForumArchiveAuthorSummary_RefreshedAt
                        DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_ModernForumArchiveAuthorSummary
                        PRIMARY KEY CLUSTERED (LegacyUserId)
                );
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfForumArchiveAuthorRepository(dbContext);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.ModernForumArchiveAuthorSummary
                (LegacyUserId, DisplayName, MemberSince, PostCount)
            VALUES
                (5001, N'john s stuart', '2010-01-01T00:00:00', 2),
                (5002, N'OnlyOne', NULL, 1);
            """);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task GetSummaryAsync_reads_precomputed_table_and_materializes_types()
    {
        var found = await repository.GetSummaryAsync(5001);
        Assert.NotNull(found);
        Assert.Equal(5001, found.LegacyUserId);
        Assert.Equal("john s stuart", found.DisplayName);
        Assert.Equal(new DateTime(2010, 1, 1), found.MemberSince);
        Assert.Equal(2, found.PostCount);

        var noSince = await repository.GetSummaryAsync(5002);
        Assert.NotNull(noSince);
        Assert.Equal("OnlyOne", noSince.DisplayName);
        Assert.Null(noSince.MemberSince);
        Assert.Equal(1, noSince.PostCount);

        Assert.Null(await repository.GetSummaryAsync(404));
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the summary table comes from InitializeAsync.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
