using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Shared optimistic-concurrency helpers for EF <see cref="SaveChangesAsync"/> and
/// SQL/ExecuteUpdate compare-and-swap writes. Lives next to
/// <see cref="QueenZoneDbTransactions"/> so write repositories share one pattern.
/// </summary>
internal static class QueenZoneConcurrency
{
    public static async Task SaveChangesAsync(
        QueenZoneDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            foreach (var entry in exception.Entries)
            {
                await entry.ReloadAsync(cancellationToken);
            }

            throw new OptimisticConcurrencyException();
        }
    }

    public static void EnsureUpdated(int affectedRows, bool exists, string notFoundMessage)
    {
        if (affectedRows > 0)
        {
            return;
        }

        if (exists)
        {
            throw new OptimisticConcurrencyException();
        }

        throw new InvalidOperationException(notFoundMessage);
    }

    public static byte[] NewClientRowVersion() => Guid.NewGuid().ToByteArray();

    /// <summary>
    /// Compare-and-swap guard: a <see langword="null"/> <paramref name="expected"/> skips the check
    /// (the caller did not ask for one); otherwise a mismatch throws <see cref="OptimisticConcurrencyException"/>.
    /// </summary>
    public static void EnsureRowVersion(byte[]? actual, byte[]? expected)
    {
        if (expected is not null && !RowVersionEquals(actual, expected))
        {
            throw new OptimisticConcurrencyException();
        }
    }

    /// <summary>
    /// Strict guard: a missing <paramref name="expected"/> or a mismatch throws
    /// <typeparamref name="TException"/>.
    /// </summary>
    public static void EnsureRequiredRowVersion<TException>(byte[]? actual, byte[]? expected)
        where TException : Exception, new()
    {
        if (!RowVersionEquals(actual, expected))
        {
            throw new TException();
        }
    }

    public static bool RowVersionEquals(byte[]? left, byte[]? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }
}
