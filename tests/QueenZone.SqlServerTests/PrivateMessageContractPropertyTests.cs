using Microsoft.EntityFrameworkCore;
using QueenZone.ContractTests;
using QueenZone.Data;
using QueenZone.Data.Configurations;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

public sealed class PrivateMessageContractPropertyTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneSqlServerTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfPrivateMessageRepository repository = null!;

    private string ConnectionString
    {
        get
        {
            var connection = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        var scratchOptions = new DbContextOptionsBuilder<ScratchSchemaDbContext>()
            .UseSqlServer(ConnectionString).Options;
        await using (var scratch = new ScratchSchemaDbContext(scratchOptions))
        {
            await scratch.Database.EnsureCreatedAsync();
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfPrivateMessageRepository(dbContext);
    }

    [Fact]
    public Task Generated_repository_matches_LINQ_model() =>
        PrivateMessageContractProperty.RunAsync(repository, AddMemberAsync, iterations: 5);

    private async Task AddMemberAsync(MemberAccount member)
    {
        dbContext.MemberAccounts.Add(member);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
        finally
        {
            await dbContext.DisposeAsync();
        }
    }

    private sealed class ScratchSchemaDbContext(DbContextOptions<ScratchSchemaDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MemberAccountConfiguration());
            modelBuilder.ApplyConfiguration(new PrivateConversationEntityConfiguration());
            modelBuilder.ApplyConfiguration(new PrivateConversationParticipantEntityConfiguration());
            modelBuilder.ApplyConfiguration(new PrivateMessageEntityConfiguration());
            modelBuilder.ApplyConfiguration(new PrivateMessageReportEntityConfiguration());
            modelBuilder.ApplyConfiguration(new PrivateMessageReportAuditLogEntityConfiguration());
            modelBuilder.ApplyConfiguration(new MemberMessageBlockEntityConfiguration());
            modelBuilder.Entity<PrivateMessageEntity>().Property(message => message.SortKey).UseIdentityColumn(1, 1);
        }
    }
}
