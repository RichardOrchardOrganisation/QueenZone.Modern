using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfIdempotencyStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfIdempotencyStore store;

    public EfIdempotencyStoreTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        store = new EfIdempotencyStore(dbContext, TimeProvider.System);
    }

    [Fact]
    public async Task Execute_CommitsWriteAndReceiptTogether()
    {
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var email = $"{memberId:N}@idem.test";

        var ran = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            async ct =>
            {
                AddMember(memberId, email);
                await dbContext.SaveChangesAsync(ct);
                return ("ok", Success("hash-a"));
            });

        Assert.Equal(IdempotencyExecuteKind.Ran, ran.Kind);
        Assert.Equal(1, await dbContext.MemberAccounts.CountAsync(row => row.Id == memberId));
        Assert.Equal(1, await dbContext.IdempotencyReceipts.CountAsync());
    }

    [Fact]
    public async Task Execute_RollsBackWrite_WhenActionThrowsBeforeReceipt()
    {
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var email = $"{memberId:N}@boom.test";

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync<string>(
            memberId,
            IdempotencyOperationKinds.MessageReply,
            operationId,
            "hash-a",
            async ct =>
            {
                AddMember(memberId, email);
                await dbContext.SaveChangesAsync(ct);
                throw new InvalidOperationException("boom");
            }));

        Assert.Equal(0, await dbContext.MemberAccounts.CountAsync(row => row.Id == memberId));
        Assert.Equal(0, await dbContext.IdempotencyReceipts.CountAsync());
    }

    [Fact]
    public async Task Execute_ReplaysExistingReceipt_WithoutSecondWrite()
    {
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = 0;

        await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateTopic,
            operationId,
            "hash-a",
            async ct =>
            {
                writes++;
                AddMember(memberId, $"{memberId:N}@once.test");
                await dbContext.SaveChangesAsync(ct);
                return ("first", Success("hash-a"));
            });

        var replay = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateTopic,
            operationId,
            "hash-a",
            ct =>
            {
                writes++;
                return Task.FromResult<(string, IdempotencyReceipt?)>(("second", Success("hash-a")));
            });

        Assert.Equal(IdempotencyExecuteKind.Replay, replay.Kind);
        Assert.Equal("first", replay.Receipt!.ResponseBodyJson);
        Assert.Equal(1, writes);
        Assert.Equal(1, await dbContext.MemberAccounts.CountAsync());
    }

    [Fact]
    public async Task Execute_UniqueIndex_SecondInsertReplaysWinner()
    {
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        dbContext.IdempotencyReceipts.Add(new IdempotencyReceiptEntity
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            OperationKind = IdempotencyOperationKinds.MessageCompose,
            OperationId = operationId,
            PayloadHash = "hash-a",
            StatusCode = 201,
            Location = "/messages/winner",
            ResponseBodyJson = "winner",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var outcome = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageCompose,
            operationId,
            "hash-a",
            ct => Task.FromResult<(string, IdempotencyReceipt?)>(("loser", Success("hash-a"))));

        Assert.Equal(IdempotencyExecuteKind.Replay, outcome.Kind);
        Assert.Equal("winner", outcome.Receipt!.ResponseBodyJson);
        Assert.Equal(1, await dbContext.IdempotencyReceipts.CountAsync());
    }

    [Fact]
    public async Task Execute_PayloadMismatch_IsConflict()
    {
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageReply,
            operationId,
            "hash-a",
            _ => Task.FromResult<(string, IdempotencyReceipt?)>(("ok", Success("hash-a"))));

        var conflict = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageReply,
            operationId,
            "hash-b",
            _ => Task.FromResult<(string, IdempotencyReceipt?)>(("other", Success("hash-b"))));

        Assert.Equal(IdempotencyExecuteKind.Conflict, conflict.Kind);
    }

    [Fact]
    public async Task Cleanup_RemovesExpiredRows()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-30T00:00:00Z"));
        var timed = new EfIdempotencyStore(dbContext, clock);
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        await timed.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            _ => Task.FromResult<(string, IdempotencyReceipt?)>(("ok", Success("hash-a"))));

        clock.Advance(IdempotencyLimits.ReceiptLifetime + TimeSpan.FromMinutes(1));
        Assert.Equal(1, await timed.CleanupExpiredAsync());
        Assert.Equal(0, await dbContext.IdempotencyReceipts.CountAsync());

        var writes = 0;
        var again = await timed.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            _ =>
            {
                writes++;
                return Task.FromResult<(string, IdempotencyReceipt?)>(("fresh", Success("hash-a")));
            });

        Assert.Equal(IdempotencyExecuteKind.Ran, again.Kind);
        Assert.Equal(1, writes);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private void AddMember(Guid memberId, string email)
    {
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Idem Fan",
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static IdempotencyReceipt Success(string hash) =>
        new(201, "/created", "first", hash);

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}
