using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class HelpRequestRateLimiter(
    IMemoryCache cache,
    TimeProvider timeProvider,
    IOptions<HelpRequestOptions> options)
{
    private readonly Lock gate = new();

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

            cache.Set(ipKey, ipCount + 1, TimeSpan.FromHours(1));
            if (memberKey is not null)
            {
                cache.Set(memberKey, memberCount + 1, TimeSpan.FromMinutes(1));
            }

            return true;
        }
    }
}
