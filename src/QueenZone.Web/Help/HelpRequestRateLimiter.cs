using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace QueenZone.Web;

public sealed class HelpRequestRateLimiter(
    IMemoryCache cache,
    TimeProvider timeProvider,
    IOptions<HelpRequestOptions> options)
{
    private readonly Lock gate = new();
    private CancellationTokenSource resetSource = new();

    public bool IsAllowed(Guid? memberId, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var ipLimit = Math.Max(1, options.Value.MaxAnonymousPerIpPerHour);
        var ipKey = $"help-request-ip:{clientIp.Trim()}:{now.ToUnixTimeSeconds() / 3600}";
        var memberKey = memberId is Guid id
            ? $"help-request-member:{id:N}:{now.ToUnixTimeSeconds() / 60}"
            : null;
        var memberLimit = Math.Max(1, options.Value.MaxPerMemberPerMinute);

        lock (gate)
        {
            var ipCount = cache.Get<int>(ipKey);
            if (ipCount >= ipLimit)
            {
                return false;
            }

            var memberCount = memberKey is null ? 0 : cache.Get<int>(memberKey);
            if (memberKey is not null && memberCount >= memberLimit)
            {
                return false;
            }

            cache.Set(ipKey, ipCount + 1, CreateEntryOptions(TimeSpan.FromHours(1)));
            if (memberKey is not null)
            {
                cache.Set(memberKey, memberCount + 1, CreateEntryOptions(TimeSpan.FromMinutes(1)));
            }

            return true;
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            var previous = resetSource;
            resetSource = new CancellationTokenSource();
            previous.Cancel();
            previous.Dispose();
        }
    }

    private MemoryCacheEntryOptions CreateEntryOptions(TimeSpan expiration)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration,
        };
        entryOptions.AddExpirationToken(new CancellationChangeToken(Volatile.Read(ref resetSource).Token));
        return entryOptions;
    }
}
