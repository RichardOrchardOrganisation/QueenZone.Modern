using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfIdempotencyStore(QueenZoneDbContext dbContext, TimeProvider? timeProvider = null)
    : IIdempotencyStore
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

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

        var existing = await FindAsync(memberId, operationKind, operationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ExpiresAt <= clock.GetUtcNow())
            {
                dbContext.IdempotencyReceipts.Remove(existing);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                return Decide<T>(ToReceipt(existing), payloadHash);
            }
        }

        try
        {
            return await QueenZoneDbTransactions.ExecuteAsync(
                dbContext,
                System.Data.IsolationLevel.ReadCommitted,
                async innerCt =>
                {
                    var again = await FindAsync(memberId, operationKind, operationId, innerCt);
                    if (again is not null && again.ExpiresAt > clock.GetUtcNow())
                    {
                        return Decide<T>(ToReceipt(again), payloadHash);
                    }

                    var (result, success) = await action(innerCt);
                    if (success is null)
                    {
                        return IdempotencyExecuteResult<T>.Ran(result);
                    }

                    dbContext.IdempotencyReceipts.Add(ToEntity(
                        memberId,
                        operationKind,
                        operationId,
                        success));
                    await dbContext.SaveChangesAsync(innerCt);
                    return IdempotencyExecuteResult<T>.Ran(result);
                },
                cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            var winner = await FindAsync(memberId, operationKind, operationId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Decide<T>(ToReceipt(winner), payloadHash);
        }
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var expired = (await dbContext.IdempotencyReceipts.ToListAsync(cancellationToken))
            .Where(row => row.ExpiresAt <= now)
            .ToList();
        if (expired.Count == 0)
        {
            return 0;
        }

        dbContext.IdempotencyReceipts.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private Task<IdempotencyReceiptEntity?> FindAsync(
        Guid memberId,
        string operationKind,
        Guid operationId,
        CancellationToken cancellationToken) =>
        dbContext.IdempotencyReceipts
            .SingleOrDefaultAsync(
                row => row.MemberId == memberId
                    && row.OperationKind == operationKind
                    && row.OperationId == operationId,
                cancellationToken);

    private IdempotencyReceiptEntity ToEntity(
        Guid memberId,
        string operationKind,
        Guid operationId,
        IdempotencyReceipt receipt)
    {
        var now = clock.GetUtcNow();
        return new IdempotencyReceiptEntity
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            OperationKind = operationKind,
            OperationId = operationId,
            PayloadHash = receipt.PayloadHash,
            StatusCode = receipt.StatusCode,
            Location = Truncate(receipt.Location, IdempotencyLimits.LocationMaxLength),
            ResponseBodyJson = receipt.ResponseBodyJson,
            CreatedAt = now,
            ExpiresAt = now + IdempotencyLimits.ReceiptLifetime,
        };
    }

    private static IdempotencyReceipt ToReceipt(IdempotencyReceiptEntity entity) =>
        new(entity.StatusCode, entity.Location, entity.ResponseBodyJson, entity.PayloadHash);

    private static IdempotencyExecuteResult<T> Decide<T>(IdempotencyReceipt existing, string payloadHash) =>
        string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)
            ? IdempotencyExecuteResult<T>.Replay(existing)
            : IdempotencyExecuteResult<T>.Conflict();

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
