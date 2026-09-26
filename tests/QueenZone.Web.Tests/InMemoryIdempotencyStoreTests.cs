using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class InMemoryIdempotencyStoreTests
{
    [Fact]
    public async Task Execute_PersistsSuccess_AndReplaysSamePayload()
    {
        var store = new InMemoryIdempotencyStore();
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = new WriteCounter();

        var first = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "body-1"),
            CancellationToken.None);

        var second = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "body-2"),
            CancellationToken.None);

        Assert.Equal(IdempotencyExecuteKind.Ran, first.Kind);
        Assert.Equal("body-1", first.Result);
        Assert.Equal(IdempotencyExecuteKind.Replay, second.Kind);
        Assert.Equal("body-1", second.Receipt!.ResponseBodyJson);
        Assert.Equal(1, writes.Count);
    }

    [Fact]
    public async Task Execute_SameKeyDifferentPayload_ConflictsWithoutWriting()
    {
        var store = new InMemoryIdempotencyStore();
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = new WriteCounter();

        await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageReply,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "ok"),
            CancellationToken.None);

        var conflict = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageReply,
            operationId,
            "hash-b",
            ct => WriteAsync(ct, writes, "other"),
            CancellationToken.None);

        Assert.Equal(IdempotencyExecuteKind.Conflict, conflict.Kind);
        Assert.Equal(1, writes.Count);
    }

    [Fact]
    public async Task Execute_FailedWrite_DoesNotPersistReceipt()
    {
        var store = new InMemoryIdempotencyStore();
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = new WriteCounter();

        var failed = await store.ExecuteAsync<string>(
            memberId,
            IdempotencyOperationKinds.ForumCreateTopic,
            operationId,
            "hash-a",
            _ =>
            {
                writes.Increment();
                return Task.FromResult<(string, IdempotencyReceipt?)>(("nope", null));
            });

        var retry = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateTopic,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "ok"),
            CancellationToken.None);

        Assert.Equal(IdempotencyExecuteKind.Ran, failed.Kind);
        Assert.Equal("nope", failed.Result);
        Assert.Equal(IdempotencyExecuteKind.Ran, retry.Kind);
        Assert.Equal("ok", retry.Result);
        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public async Task Execute_SerializesConcurrentDuplicates()
    {
        var store = new InMemoryIdempotencyStore();
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = new WriteCounter();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageCompose,
            operationId,
            "hash-a",
            async ct =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(ct);
                return await WriteAsync(ct, writes, "only-once");
            },
            CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.MessageCompose,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "duplicate"),
            CancellationToken.None);

        // The first write is still in flight, so the duplicate must wait rather than run.
        Assert.False(second.IsCompleted);
        releaseFirst.SetResult();

        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, writes.Count);
        Assert.Contains(results, result => result.Kind == IdempotencyExecuteKind.Ran);
        Assert.Contains(results, result => result.Kind == IdempotencyExecuteKind.Replay);
        Assert.All(results, result =>
        {
            var body = result.Kind == IdempotencyExecuteKind.Ran
                ? result.Result
                : result.Receipt!.ResponseBodyJson;
            Assert.Equal("only-once", body);
        });
    }

    [Fact]
    public async Task Cleanup_RemovesExpiredReceipts_UnknownKeyWritesAgain()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-30T00:00:00Z"));
        var store = new InMemoryIdempotencyStore(clock);
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = new WriteCounter();

        await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "first"),
            CancellationToken.None);

        clock.Advance(IdempotencyLimits.ReceiptLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await store.CleanupExpiredAsync());
        Assert.False(store.TryGet(memberId, IdempotencyOperationKinds.ForumCreateReply, operationId, out _));

        var again = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "second"),
            CancellationToken.None);

        Assert.Equal(IdempotencyExecuteKind.Ran, again.Kind);
        Assert.Equal("second", again.Result);
        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public async Task SeedExpired_IsTreatedAsUnknownOnNextExecute()
    {
        var store = new InMemoryIdempotencyStore();
        var memberId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var writes = new WriteCounter();
        store.SeedExpired(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            new IdempotencyReceipt(201, "/x", "{}", "hash-a"),
            DateTimeOffset.UtcNow.AddMinutes(-1));

        var ran = await store.ExecuteAsync(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            operationId,
            "hash-a",
            ct => WriteAsync(ct, writes, "fresh"),
            CancellationToken.None);

        Assert.Equal(IdempotencyExecuteKind.Ran, ran.Kind);
        Assert.Equal(1, writes.Count);
    }

    private static Task<(string Result, IdempotencyReceipt? Success)> WriteAsync(
        CancellationToken cancellationToken,
        WriteCounter writes,
        string body)
    {
        cancellationToken.ThrowIfCancellationRequested();
        writes.Increment();
        return Task.FromResult<(string, IdempotencyReceipt?)>((
            body,
            new IdempotencyReceipt(201, "/created", body, "hash-a")));
    }

    private sealed class WriteCounter
    {
        public int Count { get; private set; }

        public void Increment() => Count++;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}
