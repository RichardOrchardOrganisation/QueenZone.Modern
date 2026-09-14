using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class MobileAuthGrantRepositoryTests
{
    [Fact]
    public async Task RedeemAuthorizationCode_IsSingleUse()
    {
        var repository = new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore());
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var code = CreateCode("hash-1", now.AddMinutes(5), now);
        await repository.StoreAuthorizationCodeAsync(code);

        var first = await repository.RedeemAuthorizationCodeAsync("hash-1", now);
        var second = await repository.RedeemAuthorizationCodeAsync("hash-1", now);

        Assert.NotNull(first);
        Assert.Equal(code.MemberAccountId, first.MemberAccountId);
        Assert.Equal(now, first.RedeemedAt);
        Assert.Null(second);
    }

    [Fact]
    public async Task RedeemAuthorizationCode_RejectsExpired()
    {
        var repository = new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore());
        var created = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        await repository.StoreAuthorizationCodeAsync(CreateCode("hash-expired", created.AddMinutes(5), created));

        var redeemed = await repository.RedeemAuthorizationCodeAsync("hash-expired", created.AddMinutes(6));

        Assert.Null(redeemed);
    }

    [Fact]
    public async Task RedeemAuthorizationCode_RejectsUnknownHash()
    {
        var repository = new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore());

        var redeemed = await repository.RedeemAuthorizationCodeAsync(
            "missing",
            new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        Assert.Null(redeemed);
    }

    [Fact]
    public async Task StoreRefreshToken_PersistsHashOnlyRecord()
    {
        var store = new SharedMobileAuthGrantStore();
        var repository = new InMemoryMobileAuthGrantRepository(store);
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var token = new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "refresh-hash",
            MemberAccountId = Guid.NewGuid(),
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        };

        await repository.StoreRefreshTokenAsync(token);

        lock (store.Gate)
        {
            Assert.Single(store.RefreshTokens);
            Assert.Equal("refresh-hash", store.RefreshTokens[0].TokenHash);
            Assert.NotSame(token, store.RefreshTokens[0]);
        }
    }

    [Fact]
    public async Task TryRevokeRefreshToken_IsSingleUse()
    {
        var repository = new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore());
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var memberId = Guid.NewGuid();
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "to-revoke",
            MemberAccountId = memberId,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        Assert.True(await repository.TryRevokeRefreshTokenAsync("to-revoke", now));
        Assert.False(await repository.TryRevokeRefreshTokenAsync("to-revoke", now));
        var stored = await repository.FindRefreshTokenByHashAsync("to-revoke");
        Assert.Equal(now, stored!.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllRefreshTokensForMember_LeavesOtherMembersAlone()
    {
        var repository = new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore());
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "alice-1",
            MemberAccountId = alice,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "bob-1",
            MemberAccountId = bob,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        var revoked = await repository.RevokeAllRefreshTokensForMemberAsync(alice, now);

        Assert.Equal(1, revoked);
        Assert.NotNull((await repository.FindRefreshTokenByHashAsync("alice-1"))!.RevokedAt);
        Assert.Null((await repository.FindRefreshTokenByHashAsync("bob-1"))!.RevokedAt);
    }

    [Fact]
    public async Task LinkRefreshTokenRotation_RecordsSuccessorOnce()
    {
        var repository = new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore());
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "old-hash",
            MemberAccountId = Guid.NewGuid(),
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        Assert.True(await repository.LinkRefreshTokenRotationAsync("old-hash", "new-hash"));
        Assert.Equal("new-hash", (await repository.FindRefreshTokenByHashAsync("old-hash"))!.ReplacedByTokenHash);

        // Already linked, and an unknown hash: both no-ops.
        Assert.False(await repository.LinkRefreshTokenRotationAsync("old-hash", "another-hash"));
        Assert.False(await repository.LinkRefreshTokenRotationAsync("missing-hash", "new-hash"));
    }

    private static MobileAuthAuthorizationCodeEntity CreateCode(string hash, DateTime expiresAt, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            CodeHash = hash,
            MemberAccountId = Guid.NewGuid(),
            ClientId = MobileAuthOptions.DefaultClientId,
            RedirectUri = MobileAuthPkceTestData.RedirectUri,
            CodeChallenge = "challenge",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        };
}
