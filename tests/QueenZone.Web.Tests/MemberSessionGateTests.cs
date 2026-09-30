using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class MemberSessionGateTests
{
    [Fact]
    public void Reject_IsFalse_ForAnActiveAccount()
    {
        var account = new MemberAccount { Id = Guid.NewGuid() };

        Assert.False(MemberSessionGate.Reject(account, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reject_IsTrue_WhenTheAccountIsMissingOrSuspended(bool suspended)
    {
        var account = suspended
            ? new MemberAccount { Id = Guid.NewGuid(), IsSuspended = true }
            : null;

        Assert.True(MemberSessionGate.Reject(account, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reject_DropsCredentialsIssuedBeforeDeletion_AndKeepsLaterOnes()
    {
        var requestedAt = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var account = new MemberAccount
        {
            Id = Guid.NewGuid(),
            DeletionRequestedAt = requestedAt,
        };

        Assert.True(MemberSessionGate.Reject(account, null));
        Assert.True(MemberSessionGate.Reject(account, new DateTimeOffset(requestedAt.AddSeconds(-30))));
        Assert.True(MemberSessionGate.Reject(account, new DateTimeOffset(requestedAt)));
        Assert.False(MemberSessionGate.Reject(account, new DateTimeOffset(requestedAt.AddMilliseconds(1))));
    }
}
