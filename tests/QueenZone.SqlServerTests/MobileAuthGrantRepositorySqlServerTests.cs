using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Tests;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs <see cref="EfMobileAuthGrantRepository.TryRotateRefreshTokenAsync"/> on SQL Server
/// under the production retrying execution strategy (#1929): revoke, store and link commit
/// together, a failed store rolls the revoke back, and concurrent rotations of one grant
/// produce exactly one winner. Injected transient commit faults exercise rollback/retry
/// and lost-acknowledgement recovery (#1953).
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
    public async Task TryRotate_TransientFailureBeforeCommitRollsBackAndRetriesOneSuccessor()
    {
        await new EfMobileAuthGrantRepository(NewContext()).StoreRefreshTokenAsync(Grant("old"));
        var replacement = Grant("new");
        var fault = new FailOnceCommitInterceptor("old", replacement, afterCommit: false);
        var repository = new EfMobileAuthGrantRepository(NewContext(fault));

        var rotated = await repository.TryRotateRefreshTokenAsync("old", replacement, Now);

        Assert.Equal(1, fault.InjectedFaultCount);
        Assert.Equal(2, fault.CommitAttemptIds.Count);
        Assert.NotEqual(fault.CommitAttemptIds[0], fault.CommitAttemptIds[1]);
        Assert.Equal(1, fault.CommitCount);
        Assert.Equal(1, fault.RollbackCount);
        await AssertSingleRotationAsync(NewContext(), "old", replacement);
        Assert.True(rotated);
    }

    [Fact]
    public async Task TryRotate_LostCommitAcknowledgementRetriesWithoutDuplicatingAndReturnsSuccess()
    {
        await new EfMobileAuthGrantRepository(NewContext()).StoreRefreshTokenAsync(Grant("old"));
        var replacement = Grant("new");
        var fault = new FailOnceCommitInterceptor("old", replacement, afterCommit: true);
        var repository = new EfMobileAuthGrantRepository(NewContext(fault));

        var rotated = await repository.TryRotateRefreshTokenAsync("old", replacement, Now);

        Assert.Equal(1, fault.InjectedFaultCount);
        Assert.Equal(2, fault.CommitAttemptIds.Count);
        Assert.NotEqual(fault.CommitAttemptIds[0], fault.CommitAttemptIds[1]);
        Assert.Equal(2, fault.CommitCount);
        Assert.Equal(0, fault.RollbackCount);
        await AssertSingleRotationAsync(NewContext(), "old", replacement);
        // The original transaction committed. Returning false would contradict the
        // repository contract that false stores nothing, despite the persisted successor.
        Assert.True(rotated);
    }

    [Fact]
    public async Task TryRotate_SeparateReplayWithSameReplacementStillFails()
    {
        var repository = new EfMobileAuthGrantRepository(NewContext());
        await repository.StoreRefreshTokenAsync(Grant("old"));
        var replacement = Grant("new");

        Assert.True(await repository.TryRotateRefreshTokenAsync("old", replacement, Now));
        Assert.False(await repository.TryRotateRefreshTokenAsync("old", replacement, Now));
        await AssertSingleRotationAsync(NewContext(), "old", replacement);
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

    private static async Task AssertSingleRotationAsync(
        QueenZoneDbContext context,
        string oldTokenHash,
        MobileAuthRefreshTokenEntity replacement,
        CancellationToken cancellationToken = default)
    {
        var stored = await context.MobileAuthRefreshTokens.AsNoTracking().ToListAsync(cancellationToken);
        var old = Assert.Single(stored, token => token.TokenHash == oldTokenHash);
        var successor = Assert.Single(stored, token => token.TokenHash != oldTokenHash);
        Assert.Equal(Now, old.RevokedAt);
        Assert.Equal(replacement.TokenHash, old.ReplacedByTokenHash);
        Assert.Equal(replacement.Id, successor.Id);
        Assert.Equal(replacement.TokenHash, successor.TokenHash);
        Assert.Equal(replacement.MemberAccountId, successor.MemberAccountId);
        Assert.Equal(replacement.ClientId, successor.ClientId);
        Assert.Equal(replacement.CreatedAt, successor.CreatedAt);
        Assert.Equal(replacement.ExpiresAt, successor.ExpiresAt);
        Assert.Null(successor.RevokedAt);
        Assert.Null(successor.ReplacedByTokenHash);
    }

    private sealed class FailOnceCommitInterceptor(
        string oldTokenHash,
        MobileAuthRefreshTokenEntity replacement,
        bool afterCommit) : DbTransactionInterceptor
    {
        public List<Guid> CommitAttemptIds { get; } = [];

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int InjectedFaultCount { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            CommitAttemptIds.Add(eventData.TransactionId);
            // Query the real rows inside this transaction, after both revoke and insert.
            // The second attempt must still contain exactly the same single successor.
            await AssertSingleRotationAsync(
                Assert.IsType<QueenZoneDbContext>(eventData.Context),
                oldTokenHash,
                replacement,
                cancellationToken);
            if (!afterCommit && InjectedFaultCount == 0)
            {
                ThrowTransientFault();
            }

            return result;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            // EF invokes this only after the underlying SQL transaction has committed.
            // Throwing here models a lost acknowledgement, not a rolled-back commit.
            if (afterCommit && InjectedFaultCount == 0)
            {
                ThrowTransientFault();
            }

            return Task.CompletedTask;
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        private void ThrowTransientFault()
        {
            InjectedFaultCount++;
            // 40613 is transient in the production SQL Server execution strategy.
            throw SqlExceptionFactory.Create(40613, "Injected transient rotation commit failure.");
        }
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

    private QueenZoneDbContext NewContext(params IInterceptor[] interceptors)
    {
        var context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(
                ConnectionString,
                sql => sql.EnableRetryOnFailure(
                    QueenZoneSqlServerOptions.MaxRetryCount,
                    QueenZoneSqlServerOptions.MaxRetryDelay,
                    errorNumbersToAdd: null))
            .AddInterceptors(interceptors)
            .Options);
        contexts.Add(context);
        return context;
    }
}
