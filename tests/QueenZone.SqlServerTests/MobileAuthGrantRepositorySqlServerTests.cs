using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs <see cref="EfMobileAuthGrantRepository.TryRotateRefreshTokenAsync"/> on SQL Server
/// under the production retrying execution strategy (#1929): revoke, store and link commit
/// together, a failed store rolls the revoke back, and concurrent rotations of one grant
/// produce exactly one winner.
/// </summary>
public sealed class MobileAuthGrantRepositorySqlServerTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid MemberId = Guid.NewGuid();

    private readonly string databaseName = $"QueenZoneMobileAuthTests_{Guid.NewGuid():N}";
    private readonly List<QueenZoneDbContext> contexts = [];

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new SqlConnectionStringBuilder(source) { InitialCatalog = databaseName }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var master = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        // Mirrors AddMobileAuthGrants + AddMobileAuthRefreshTokenReplacedByHash, minus the
        // MemberAccounts foreign key, which these tests don't exercise.
        await using var context = NewContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.MobileAuthRefreshTokens (
                Id uniqueidentifier NOT NULL CONSTRAINT PK_MobileAuthRefreshTokens PRIMARY KEY,
                TokenHash nvarchar(64) NOT NULL,
                MemberAccountId uniqueidentifier NOT NULL,
                ClientId nvarchar(100) NOT NULL,
                ExpiresAt datetime2 NOT NULL,
                CreatedAt datetime2 NOT NULL,
                RevokedAt datetime2 NULL,
                ReplacedByTokenHash nvarchar(64) NULL);
            CREATE UNIQUE INDEX IX_MobileAuthRefreshTokens_TokenHash ON dbo.MobileAuthRefreshTokens (TokenHash);
            CREATE INDEX IX_MobileAuthRefreshTokens_Member_Revoked ON dbo.MobileAuthRefreshTokens (MemberAccountId, RevokedAt);
            """);
    }

    public async Task DisposeAsync()
    {
        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }

        await using var cleanup = NewContext();
        await cleanup.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task TryRotate_RevokesStoresAndLinksInOneCommit()
    {
        var repository = new EfMobileAuthGrantRepository(NewContext());
        await repository.StoreRefreshTokenAsync(Grant("old"));

        Assert.True(await repository.TryRotateRefreshTokenAsync("old", Grant("new"), Now));
        Assert.False(await repository.TryRotateRefreshTokenAsync("old", Grant("again"), Now));

        var reader = new EfMobileAuthGrantRepository(NewContext());
        var old = await reader.FindRefreshTokenByHashAsync("old");
        Assert.Equal(Now, old!.RevokedAt);
        Assert.Equal("new", old.ReplacedByTokenHash);
        Assert.Null((await reader.FindRefreshTokenByHashAsync("new"))!.RevokedAt);
        Assert.Null(await reader.FindRefreshTokenByHashAsync("again"));
    }

    [Fact]
    public async Task TryRotate_RollsBackTheRevokeWhenTheStoreFails()
    {
        var repository = new EfMobileAuthGrantRepository(NewContext());
        await repository.StoreRefreshTokenAsync(Grant("keep"));
        await repository.StoreRefreshTokenAsync(Grant("taken"));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.TryRotateRefreshTokenAsync("keep", Grant("taken"), Now));

        var kept = await new EfMobileAuthGrantRepository(NewContext()).FindRefreshTokenByHashAsync("keep");
        Assert.Null(kept!.RevokedAt);
        Assert.Null(kept.ReplacedByTokenHash);
    }

    [Fact]
    public async Task TryRotate_ConcurrentRotationsOfOneGrantHaveOneWinner()
    {
        await new EfMobileAuthGrantRepository(NewContext()).StoreRefreshTokenAsync(Grant("contended"));
        var racers = Enumerable.Range(0, 4)
            .Select(index => new EfMobileAuthGrantRepository(NewContext())
                .TryRotateRefreshTokenAsync("contended", Grant($"successor-{index}"), Now))
            .ToArray();

        var results = await Task.WhenAll(racers);

        Assert.Single(results, won => won);
        var reader = NewContext();
        var stored = await reader.MobileAuthRefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash.StartsWith("successor-"))
            .ToListAsync();
        var winner = Assert.Single(stored);
        Assert.Equal(
            winner.TokenHash,
            (await new EfMobileAuthGrantRepository(reader).FindRefreshTokenByHashAsync("contended"))!.ReplacedByTokenHash);
    }

    private static MobileAuthRefreshTokenEntity Grant(string hash) =>
        new()
        {
            Id = Guid.NewGuid(),
            TokenHash = hash,
            MemberAccountId = MemberId,
            ClientId = "queenzone-mobile",
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(30),
        };

    private QueenZoneDbContext NewContext()
    {
        var context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(
                ConnectionString,
                sql => sql.EnableRetryOnFailure(
                    QueenZoneSqlServerOptions.MaxRetryCount,
                    QueenZoneSqlServerOptions.MaxRetryDelay,
                    errorNumbersToAdd: null))
            .Options);
        contexts.Add(context);
        return context;
    }
}
