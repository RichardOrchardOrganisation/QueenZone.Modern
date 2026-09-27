using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

public sealed class HelpRequestRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneHelpRequestTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;

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
        var schemaOptions = new DbContextOptionsBuilder<HelpRequestSchemaContext>()
            .UseSqlServer(ConnectionString).Options;
        await using (var schema = new HelpRequestSchemaContext(schemaOptions))
        {
            await schema.Database.EnsureCreatedAsync();
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        var schemaOptions = new DbContextOptionsBuilder<HelpRequestSchemaContext>()
            .UseSqlServer(ConnectionString).Options;
        await using var schema = new HelpRequestSchemaContext(schemaOptions);
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task List_uses_sql_server_ordering_filter_and_pagination()
    {
        var repository = new EfHelpRequestRepository(dbContext);
        var time = DateTimeOffset.Parse("2026-08-17T10:00:00Z");
        var older = await repository.CreateAsync(Request("Older", time.AddDays(-1)));
        var tieSecond = await repository.CreateAsync(Request("Tie second", time,
            Guid.Parse("00000000-0000-0000-0000-000000000002")));
        var tieFirst = await repository.CreateAsync(Request("Tie first", time,
            Guid.Parse("00000000-0000-0000-0000-000000000001")));

        var first = await repository.ListAsync("all", 1, 1);
        var second = await repository.ListAsync(null, 2, 1);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(tieFirst.Id, Assert.Single(first.Items).Id);
        Assert.Equal(tieSecond.Id, Assert.Single(second.Items).Id);

        await repository.UpdateStatusAsync(tieFirst.Id, HelpRequestStatus.Resolved, null, null);
        var open = await repository.ListAsync(HelpRequestStatus.Open, 1, 10);
        Assert.Equal(2, open.TotalCount);
        Assert.Equal([tieSecond.Id, older.Id], open.Items.Select(item => item.Id));
        Assert.Equal(2, await repository.CountOpenAsync());
    }

    private static HelpRequest Request(string subject, DateTimeOffset submittedAt, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), HelpRequestTopic.Account, subject,
            "This is a sufficiently long help message.", "Guest User", "guest@example.com",
            "GUEST@EXAMPLE.COM", null, HelpRequestStatus.Open, submittedAt, null, null, null);

    // The production DbContext includes legacy tables that cannot be created in a blank database.
    private sealed class HelpRequestSchemaContext(DbContextOptions<HelpRequestSchemaContext> options)
        : DbContext(options)
    {
        public DbSet<HelpRequestEntity> HelpRequests => Set<HelpRequestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HelpRequestEntity>(entity =>
            {
                entity.ToTable("HelpRequests");
                entity.HasKey(request => request.Id);
                entity.Ignore(request => request.Member);
                entity.Property(request => request.Topic).HasMaxLength(50).IsRequired();
                entity.Property(request => request.Subject).HasMaxLength(200).IsRequired();
                entity.Property(request => request.Message).HasMaxLength(4000).IsRequired();
                entity.Property(request => request.Name).HasMaxLength(100).IsRequired();
                entity.Property(request => request.Email).HasMaxLength(256).IsRequired();
                entity.Property(request => request.NormalizedEmail).HasMaxLength(256).IsRequired();
                entity.Property(request => request.Status).HasMaxLength(50).IsRequired();
            });
        }
    }
}
