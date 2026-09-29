using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfMemberLookupRepository"/> constructor
/// (<see cref="EfProductionSql.CreateMemberLookupSql"/> /
/// <see cref="EfProductionSql.CreateMemberLookupByUserIdSql"/>) against a scratch
/// SQL Server <c>USERS_T</c> (#1672 / #1890). Column types were verified against
/// the 2026-09-29 read-only catalog dump of <c>queenzone_legacy_sync</c>
/// (<c>sys.columns</c>): <c>USER_ID int IDENTITY(1,1)</c> with
/// <c>PK_USERS_T</c>, <c>USERNAME char(40) NULL</c>, <c>EMAIL varchar(100) NULL</c>,
/// both text columns <c>SQL_Latin1_General_CP1_CI_AS</c>. Only the columns the
/// production SQL reads are created (plus the identity PK so inserts work).
/// Unused dump columns that are nullable — and therefore omitted, not required
/// for inserts — include <c>COUNTRY</c>, <c>PICTURE_HEIGHT</c>,
/// <c>PICTURE_WIDTH</c>, <c>LAST_LOGIN</c>, <c>UPLOADED</c>, <c>DOWNLOADED</c>,
/// and <c>VIEW_ADS tinyint NULL default 1</c>.
/// The read-only mirror probe is <c>EfMemberLookupRepositoryLegacyProbeTests</c>
/// in <c>QueenZone.Web.Tests</c>.
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
                    USER_ID int IDENTITY(1,1) NOT NULL,
                    USERNAME char(40) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
                    EMAIL varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
                    CONSTRAINT PK_USERS_T PRIMARY KEY CLUSTERED (USER_ID)
                );

                CREATE NONCLUSTERED INDEX USERS_T25
                    ON dbo.USERS_T (EMAIL)
                    WITH (FILLFACTOR = 90);

                CREATE NONCLUSTERED INDEX USERS_T29
                    ON dbo.USERS_T (USERNAME, USER_ID)
                    WITH (FILLFACTOR = 90);
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfMemberLookupRepository(dbContext);

        // Freddie is USER_ID 43 and Mercury is 42 so ORDER BY USERNAME, USER_ID
        // is not the same as ORDER BY USER_ID. char(40) pads USERNAME on the right.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            SET IDENTITY_INSERT dbo.USERS_T ON;
            INSERT INTO dbo.USERS_T (USER_ID, USERNAME, EMAIL)
            VALUES
                (42, '  Mercury  ', 'freddie@example.com'),
                (43, '  Freddie  ', 'freddie@example.com'),
                (99, 'Other', 'other@example.com'),
                (20, 'SameName', 'shared@example.com'),
                (7, 'SameName', 'shared@example.com'),
                (5, NULL, 'noul@example.com'),
                (11, 'Brian', 'brian@example.com');
            SET IDENTITY_INSERT dbo.USERS_T OFF;
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
        Assert.Equal(43, first.UserId);
        Assert.Equal("Freddie", first.Username);

        var all = await repository.FindAllByEmailAsync("freddie@example.com");
        Assert.Equal([43, 42], all.Select(match => match.UserId).ToArray());
        Assert.Equal(["Freddie", "Mercury"], all.Select(match => match.Username).ToArray());

        // Same username, different USER_ID: the secondary sort is USER_ID.
        var shared = await repository.FindAllByEmailAsync("shared@example.com");
        Assert.Equal([7, 20], shared.Select(match => match.UserId).ToArray());

        Assert.Empty(await repository.FindAllByEmailAsync("missing@example.com"));
        Assert.Null(await repository.FindByEmailAsync("missing@example.com"));

        var byId = await repository.FindByUserIdAsync(43);
        Assert.NotNull(byId);
        Assert.Equal("Freddie", byId.Username);
        Assert.Null(await repository.FindByUserIdAsync(999));

        // NULL USERNAME materializes as empty after Trim.
        var noName = await repository.FindByUserIdAsync(5);
        Assert.NotNull(noName);
        Assert.Equal(string.Empty, noName.Username);
    }

    [Fact]
    public async Task Char40_username_trims_and_email_lookup_is_case_insensitive()
    {
        // Mirror USERNAME is char(40): SQL Server right-pads 'Brian' to 40 bytes.
        var storedLength = await dbContext.Database
            .SqlQueryRaw<int>("SELECT DATALENGTH(USERNAME) AS [Value] FROM dbo.USERS_T WHERE USER_ID = 11")
            .SingleAsync();
        Assert.Equal(40, storedLength);

        var byId = await repository.FindByUserIdAsync(11);
        Assert.NotNull(byId);
        Assert.Equal("Brian", byId.Username);

        // Leading/trailing spaces in the inserted literal are kept, then char(40) pads;
        // production code Trims on read.
        var padded = await repository.FindByUserIdAsync(43);
        Assert.Equal("Freddie", padded!.Username);

        // EMAIL is CI_AS and not unique; a case-different lookup is the same row.
        var mixedCase = await repository.FindByEmailAsync("BRIAN@EXAMPLE.COM");
        Assert.NotNull(mixedCase);
        Assert.Equal(11, mixedCase.UserId);
        Assert.Equal("Brian", mixedCase.Username);
        Assert.Equal(
            (await repository.FindByEmailAsync("brian@example.com"))!.UserId,
            mixedCase.UserId);
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; USERS_T comes from InitializeAsync.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
