using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfMemberLookupRepository"/> constructor
/// (<see cref="EfProductionSql.CreateMemberLookupSql"/> /
/// <see cref="EfProductionSql.CreateMemberLookupByUserIdSql"/>) against a scratch
/// SQL Server <c>USERS_T</c> (#1672 / #1890). Column types come from the committed
/// <c>docs/db-schema.txt</c> dump (22 June 2026) and the slim <c>USERS_T</c> already
/// used by <see cref="PhotoRepositorySqlServerTests"/>: <c>USER_ID int</c>,
/// <c>USERNAME char(40) NULL</c>, <c>EMAIL varchar(100) NULL</c>. This environment
/// could not query <c>sys.columns</c> on <c>queenzone_legacy_sync</c>. The read-only
/// mirror probe is <c>EfMemberLookupRepositoryLegacyProbeTests</c> in
/// <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class MemberLookupRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneMemberLookupTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfMemberLookupRepository repository = null!;

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
                CREATE TABLE dbo.USERS_T
                (
                    USER_ID int NOT NULL PRIMARY KEY,
                    USERNAME char(40) NULL,
                    EMAIL varchar(100) NULL
                );
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfMemberLookupRepository(dbContext);

        // IDs are deliberately not username order so a sort-by-id bug fails the
        // ORDER BY USERNAME, USER_ID assertion. char(40) pads USERNAME on the right.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.USERS_T (USER_ID, USERNAME, EMAIL)
            VALUES
                (43, '  Mercury  ', 'freddie@example.com'),
                (42, '  Freddie  ', 'freddie@example.com'),
                (99, 'Other', 'other@example.com'),
                (20, 'SameName', 'shared@example.com'),
                (7, 'SameName', 'shared@example.com'),
                (5, NULL, 'noul@example.com');
            """);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Find_by_email_and_user_id_orders_matches_and_trims_char_username()
    {
        var first = await repository.FindByEmailAsync("freddie@example.com");
        Assert.NotNull(first);
        Assert.Equal(42, first.UserId);
        Assert.Equal("Freddie", first.Username);

        var all = await repository.FindAllByEmailAsync("freddie@example.com");
        Assert.Equal([42, 43], all.Select(match => match.UserId).ToArray());
        Assert.Equal(["Freddie", "Mercury"], all.Select(match => match.Username).ToArray());

        // Same username, different USER_ID: the secondary sort is USER_ID.
        var shared = await repository.FindAllByEmailAsync("shared@example.com");
        Assert.Equal([7, 20], shared.Select(match => match.UserId).ToArray());

        Assert.Empty(await repository.FindAllByEmailAsync("missing@example.com"));
        Assert.Null(await repository.FindByEmailAsync("missing@example.com"));

        var byId = await repository.FindByUserIdAsync(42);
        Assert.NotNull(byId);
        Assert.Equal("Freddie", byId.Username);
        Assert.Null(await repository.FindByUserIdAsync(999));

        // NULL USERNAME materializes as empty after Trim.
        var noName = await repository.FindByUserIdAsync(5);
        Assert.NotNull(noName);
        Assert.Equal(string.Empty, noName.Username);

        // Default SQL Server collation is CI; EMAIL = @email follows that.
        var caseInsensitive = await repository.FindByEmailAsync("FREDDIE@EXAMPLE.COM");
        Assert.NotNull(caseInsensitive);
        Assert.Equal(42, caseInsensitive.UserId);
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; USERS_T comes from InitializeAsync.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
