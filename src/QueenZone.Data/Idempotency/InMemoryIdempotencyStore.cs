using System.Collections.Concurrent;

namespace QueenZone.Data;

public sealed class InMemoryIdempotencyStore(TimeProvider? timeProvider = null) : IIdempotencyStore
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, StoredReceipt> receipts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    public async Task<IdempotencyExecuteResult<T>> ExecuteAsync<T>(
        Guid memberId,
        string operationKind,
        Guid operationId,
        string payloadHash,
        Func<CancellationToken, Task<(T Result, IdempotencyReceipt? Success)>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);
        ArgumentNullException.ThrowIfNull(action);

        await CleanupExpiredAsync(cancellationToken);
        var key = Key(memberId, operationKind, operationId);
        var gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (receipts.TryGetValue(key, out var existing) && existing.ExpiresAt > clock.GetUtcNow())
            {
                return Decide<T>(existing.Receipt, payloadHash);
            }

            if (existing is not null)
            {
                receipts.TryRemove(key, out _);
            }

            var (result, success) = await action(cancellationToken);
            if (success is null)
            {
                return IdempotencyExecuteResult<T>.Ran(result);
            }

            var now = clock.GetUtcNow();
            receipts[key] = new StoredReceipt(success, now + IdempotencyLimits.ReceiptLifetime);
            return IdempotencyExecuteResult<T>.Ran(result);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CleanupExpired());
    }

    internal void SeedExpired(
        Guid memberId,
        string operationKind,
        Guid operationId,
        IdempotencyReceipt receipt,
        DateTimeOffset expiresAt)
    {
        receipts[Key(memberId, operationKind, operationId)] = new StoredReceipt(receipt, expiresAt);
    }

    internal bool TryGet(
        Guid memberId,
        string operationKind,
        Guid operationId,
        out IdempotencyReceipt? receipt)
    {
        if (receipts.TryGetValue(Key(memberId, operationKind, operationId), out var stored)
            && stored.ExpiresAt > clock.GetUtcNow())
        {
            receipt = stored.Receipt;
            return true;
        }

        receipt = null;
        return false;
    }

    private int CleanupExpired()
    {
        var now = clock.GetUtcNow();
        var removed = 0;
        foreach (var pair in receipts)
        {
            if (pair.Value.ExpiresAt <= now && receipts.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private static IdempotencyExecuteResult<T> Decide<T>(IdempotencyReceipt existing, string payloadHash) =>
        string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)
            ? IdempotencyExecuteResult<T>.Replay(existing)
            : IdempotencyExecuteResult<T>.Conflict();

    private static string Key(Guid memberId, string operationKind, Guid operationId) =>
        $"{memberId:D}|{operationKind}|{operationId:D}";

    private sealed record StoredReceipt(IdempotencyReceipt Receipt, DateTimeOffset ExpiresAt);
}
