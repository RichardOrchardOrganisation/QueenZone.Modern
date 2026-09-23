using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class HelpRequestRateLimiterTests
{
    [Fact]
    public void IsAllowed_DeniesMissingIp()
    {
        var limiter = CreateLimiter();

        Assert.False(limiter.IsAllowed(null, null));
        Assert.False(limiter.IsAllowed(Guid.NewGuid(), " "));
    }

    [Fact]
    public void IsAllowed_AllowsUpToConfiguredLimitThenDenies()
    {
        var limiter = CreateLimiter(maxPerHour: 2);

        Assert.True(limiter.IsAllowed(null, "203.0.113.10"));
        Assert.True(limiter.IsAllowed(null, "203.0.113.10"));
        Assert.False(limiter.IsAllowed(null, "203.0.113.10"));
        Assert.True(limiter.IsAllowed(null, "203.0.113.11"));
    }

    [Fact]
    public void IsAllowed_AppliesMemberAndIpLimitsTogether()
    {
        var limiter = CreateLimiter(maxPerHour: 3, maxPerMemberPerMinute: 1);
        var firstMember = Guid.NewGuid();
        var secondMember = Guid.NewGuid();

        Assert.True(limiter.IsAllowed(firstMember, "203.0.113.20"));
        Assert.False(limiter.IsAllowed(firstMember, "203.0.113.21"));
        Assert.True(limiter.IsAllowed(secondMember, "203.0.113.20"));
        Assert.True(limiter.IsAllowed(null, "203.0.113.20"));
        Assert.False(limiter.IsAllowed(Guid.NewGuid(), "203.0.113.20"));
    }

    private static HelpRequestRateLimiter CreateLimiter(int maxPerHour = 3, int maxPerMemberPerMinute = 20)
    {
        return new HelpRequestRateLimiter(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new HelpRequestOptions
            {
                MaxAnonymousPerIpPerHour = maxPerHour,
                MaxPerMemberPerMinute = maxPerMemberPerMinute,
            }));
    }
}
