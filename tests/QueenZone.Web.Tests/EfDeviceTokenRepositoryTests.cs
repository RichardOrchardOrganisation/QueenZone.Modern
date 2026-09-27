using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfDeviceTokenRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfDeviceTokenRepository repository;

    public EfDeviceTokenRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();
        repository = new EfDeviceTokenRepository(dbContext);
    }

    [Fact]
    public async Task ListByMemberIdsAsync_ReturnsOwnedTokens()
    {
        var alice = await SeedAccountAsync("alice-token@example.com");
        var bob = await SeedAccountAsync("bob-token@example.com");
        await repository.UpsertAsync(DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Apns, "alice-a"));
        await repository.UpsertAsync(DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Fcm, "alice-f"));
        await repository.UpsertAsync(DeviceTokenTestData.Token(bob.Id, DevicePushPlatform.Apns, "bob-a"));

        var listed = await repository.ListByMemberIdsAsync([alice.Id]);

        Assert.Equal(2, listed.Count);
        Assert.All(listed, row => Assert.Equal(alice.Id, row.MemberAccountId));
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_SameMember_UpdatesTokenPlatformAndUpdatedAt()
    {
        var alice = await SeedAccountAsync("alice-reregister@example.com");
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var first = DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Apns, "token-old", deviceId);
        first.UpdatedAt = DateTime.UtcNow.AddMinutes(-5);
        first.CreatedAt = first.UpdatedAt;
        var inserted = await repository.UpsertAsync(first);

        var second = DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Fcm, "token-new", deviceId);
        second.UpdatedAt = DateTime.UtcNow;
        var updated = await repository.UpsertAsync(second);

        Assert.Equal(inserted.Id, updated.Id);
        Assert.Equal(inserted.CreatedAt, updated.CreatedAt);
        Assert.Equal(alice.Id, updated.MemberAccountId);
        Assert.Equal(DevicePushPlatform.Fcm, updated.Platform);
        Assert.Equal("token-new", updated.Token);
        Assert.Equal(second.UpdatedAt, updated.UpdatedAt);
        Assert.Equal(1, await CountByDeviceIdAsync(deviceId));
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_DifferentMember_ReassignsOwnership()
    {
        var alice = await SeedAccountAsync("alice-owner@example.com");
        var bob = await SeedAccountAsync("bob-owner@example.com");
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var first = await repository.UpsertAsync(
            DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Apns, "alice-tok", deviceId));

        var updated = await repository.UpsertAsync(
            DeviceTokenTestData.Token(bob.Id, DevicePushPlatform.Apns, "bob-tok", deviceId));

        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(bob.Id, updated.MemberAccountId);
        Assert.Equal("bob-tok", updated.Token);
        Assert.Equal(1, await CountByDeviceIdAsync(deviceId));
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_SeparateContext_DoesNotViolateUniqueIndex()
    {
        var alice = await SeedAccountAsync("alice-ctx@example.com");
        var bob = await SeedAccountAsync("bob-ctx@example.com");
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        await repository.UpsertAsync(
            DeviceTokenTestData.Token(alice.Id, DevicePushPlatform.Apns, "alice-tok", deviceId));

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        await using (var otherContext = new QueenZoneDbContext(options))
        {
            var otherRepository = new EfDeviceTokenRepository(otherContext);
            var updated = await otherRepository.UpsertAsync(
                DeviceTokenTestData.Token(bob.Id, DevicePushPlatform.Fcm, "bob-tok", deviceId));
            Assert.Equal(bob.Id, updated.MemberAccountId);
            Assert.Equal("bob-tok", updated.Token);
        }

        dbContext.ChangeTracker.Clear();
        Assert.Equal(1, await CountByDeviceIdAsync(deviceId));
        var stored = await dbContext.DeviceTokens.SingleAsync(row => row.DeviceId == deviceId);
        Assert.Equal(bob.Id, stored.MemberAccountId);
        Assert.Equal(DevicePushPlatform.Fcm, stored.Platform);
        Assert.Equal("bob-tok", stored.Token);
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_ConcurrentRegisters_KeepSingleRow()
    {
        var alice = await SeedAccountAsync("alice-race@example.com");
        var bob = await SeedAccountAsync("bob-race@example.com");
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        var members = new[] { alice.Id, bob.Id };

        var tasks = Enumerable.Range(0, 12).Select(async index =>
        {
            await using var otherContext = new QueenZoneDbContext(options);
            var otherRepository = new EfDeviceTokenRepository(otherContext);
            await otherRepository.UpsertAsync(DeviceTokenTestData.Token(
                members[index % 2],
                DevicePushPlatform.Apns,
                $"tok-{index}",
                deviceId));
        });

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));

        dbContext.ChangeTracker.Clear();
        Assert.Null(exception);
        Assert.Equal(1, await CountByDeviceIdAsync(deviceId));
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_DifferentCasing_UpdatesInsteadOfInsert()
    {
        var alice = await SeedAccountAsync("alice-case@example.com");
        var bob = await SeedAccountAsync("bob-case@example.com");
        const string storedDeviceId = "E3C869B0-F770-4EE4-BE4A-46C63CCBA90F";
        const string incomingDeviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var first = await repository.UpsertAsync(DeviceTokenTestData.Token(
            alice.Id,
            DevicePushPlatform.Apns,
            "alice-tok",
            storedDeviceId));

        var updated = await repository.UpsertAsync(DeviceTokenTestData.Token(
            bob.Id,
            DevicePushPlatform.Fcm,
            "bob-tok",
            incomingDeviceId));

        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(bob.Id, updated.MemberAccountId);
        Assert.Equal("bob-tok", updated.Token);
        Assert.Equal(DevicePushPlatform.Fcm, updated.Platform);
        Assert.Equal(1, await dbContext.DeviceTokens.CountAsync());
    }

    [Fact]
    public async Task UpsertAsync_UniqueConflictOnInsert_UpdatesExistingRow()
    {
        var alice = await SeedAccountAsync("alice-conflict@example.com");
        var bob = await SeedAccountAsync("bob-conflict@example.com");
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var interceptor = new InsertCompetingDeviceTokenInterceptor
        {
            Connection = connection,
            MemberAccountId = alice.Id,
            DeviceId = deviceId,
        };
        await using var racingContext = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options);
        var racingRepository = new EfDeviceTokenRepository(racingContext);

        var updated = await racingRepository.UpsertAsync(
            DeviceTokenTestData.Token(bob.Id, DevicePushPlatform.Fcm, "bob-tok", deviceId));

        Assert.Equal(bob.Id, updated.MemberAccountId);
        Assert.Equal("bob-tok", updated.Token);
        Assert.Equal(DevicePushPlatform.Fcm, updated.Platform);
        Assert.Equal(1, interceptor.CompetingInserts);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(1, await CountByDeviceIdAsync(deviceId));
    }

    private sealed class InsertCompetingDeviceTokenInterceptor : SaveChangesInterceptor
    {
        public required SqliteConnection Connection { get; init; }

        public required Guid MemberAccountId { get; init; }

        public required string DeviceId { get; init; }

        public int CompetingInserts { get; private set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            InsertCompetingRow(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            InsertCompetingRow(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private void InsertCompetingRow(DbContext? context)
        {
            if (context is null
                || CompetingInserts > 0
                || !context.ChangeTracker.Entries<DeviceTokenEntity>().Any(entry => entry.State == EntityState.Added))
            {
                return;
            }

            var now = DateTime.UtcNow.ToString("o");
            using var command = Connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO DeviceTokens (Id, DeviceId, MemberAccountId, Platform, Token, CreatedAt, UpdatedAt)
                VALUES ($id, $deviceId, $memberId, 'Apns', 'competitor', $now, $now)
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid());
            command.Parameters.AddWithValue("$deviceId", DeviceId);
            command.Parameters.AddWithValue("$memberId", MemberAccountId);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
            CompetingInserts++;
        }
    }

    [Fact]
    public async Task ListByMemberIdsAsync_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(await repository.ListByMemberIdsAsync([]));
    }

    private async Task<MemberAccount> SeedAccountAsync(string email)
    {
        var account = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = email,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.MemberAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account;
    }

    private Task<int> CountByDeviceIdAsync(string deviceId) =>
        dbContext.DeviceTokens.CountAsync(row => row.DeviceId == deviceId);

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
