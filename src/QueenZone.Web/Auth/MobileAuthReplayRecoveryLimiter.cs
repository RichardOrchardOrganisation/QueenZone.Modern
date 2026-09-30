using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

/// <summary>
/// Process-local per-member cap on unused-successor refresh recoveries
/// (<see cref="MobileAuthOptions.RefreshTokenUnusedSuccessorRecoveryDailyLimit"/>).
/// A thief holding one token of a chain can swap it back and forth with the real
/// client through that recovery; the cap turns a sustained swap into revoke-all.
/// </summary>
public sealed class MobileAuthReplayRecoveryLimiter(
    IMemoryCache cache,
    TimeProvider timeProvider,
    IOptions<MobileAuthOptions> options)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>Counts one recovery. Returns false once the member is over the limit.</summary>
    public bool TryConsume(Guid memberId)
    {
        var limit = Math.Max(1, options.Value.RefreshTokenUnusedSuccessorRecoveryDailyLimit);
        var key = $"mobile-auth-replay-recovery:{memberId:N}";
        var now = timeProvider.GetUtcNow();
        var window = cache.Get<RecoveryWindow>(key);
        if (window is null || now - window.StartedAt >= Window)
        {
            window = new RecoveryWindow(now, 0);
        }

        if (window.Count >= limit)
        {
            return false;
        }

        // Relative: the cache expires on its own clock, not timeProvider's.
        cache.Set(key, window with { Count = window.Count + 1 }, window.StartedAt + Window - now);
        return true;
    }

    private sealed record RecoveryWindow(DateTimeOffset StartedAt, int Count);
}
