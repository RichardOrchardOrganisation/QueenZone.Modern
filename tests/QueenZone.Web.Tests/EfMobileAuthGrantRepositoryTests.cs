using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfMobileAuthGrantRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfMobileAuthGrantRepository repository;

    public EfMobileAuthGrantRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        repository = new EfMobileAuthGrantRepository(dbContext);
    }

    [Fact]
    public async Task RedeemAuthorizationCode_IsSingleUse()
    {
        var member = await SeedMemberAsync();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        await repository.StoreAuthorizationCodeAsync(new MobileAuthAuthorizationCodeEntity
        {
            Id = Guid.NewGuid(),
            CodeHash = "ef-hash-1",
            MemberAccountId = member.Id,
            ClientId = MobileAuthOptions.DefaultClientId,
            RedirectUri = MobileAuthPkceTestData.RedirectUri,
            CodeChallenge = "challenge",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5),
        });

        var first = await repository.RedeemAuthorizationCodeAsync("ef-hash-1", now);
        var second = await repository.RedeemAuthorizationCodeAsync("ef-hash-1", now);

        Assert.NotNull(first);
        Assert.Equal(member.Id, first.MemberAccountId);
        Assert.Equal(now, first.RedeemedAt);
        Assert.Null(second);
    }

    [Fact]
    public async Task StoreRefreshToken_PersistsHashedToken()
    {
        var member = await SeedMemberAsync();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "ef-refresh-hash",
            MemberAccountId = member.Id,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        var stored = await dbContext.MobileAuthRefreshTokens.AsNoTracking()
            .SingleAsync(token => token.TokenHash == "ef-refresh-hash");
        Assert.Equal(member.Id, stored.MemberAccountId);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task TryRevokeRefreshToken_ThenFind_ShowsRevoked()
    {
        var member = await SeedMemberAsync();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "ef-revoke",
            MemberAccountId = member.Id,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        Assert.True(await repository.TryRevokeRefreshTokenAsync("ef-revoke", now));
        Assert.False(await repository.TryRevokeRefreshTokenAsync("ef-revoke", now));
        var stored = await repository.FindRefreshTokenByHashAsync("ef-revoke");
        Assert.Equal(now, stored!.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllRefreshTokensForMember_RevokesOnlyThatMember()
    {
        var member = await SeedMemberAsync();
        var other = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "other-ef@example.com",
            NormalizedEmail = "OTHER-EF@EXAMPLE.COM",
            DisplayName = "Other EF",
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.MemberAccounts.Add(other);
        await dbContext.SaveChangesAsync();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "ef-alice",
            MemberAccountId = member.Id,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "ef-bob",
            MemberAccountId = other.Id,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        var revoked = await repository.RevokeAllRefreshTokensForMemberAsync(member.Id, now);

        Assert.Equal(1, revoked);
        Assert.NotNull((await repository.FindRefreshTokenByHashAsync("ef-alice"))!.RevokedAt);
        Assert.Null((await repository.FindRefreshTokenByHashAsync("ef-bob"))!.RevokedAt);
    }

    [Fact]
    public async Task LinkRefreshTokenRotation_RecordsSuccessorOnce()
    {
        var member = await SeedMemberAsync();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        await repository.StoreRefreshTokenAsync(new MobileAuthRefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = "ef-old-hash",
            MemberAccountId = member.Id,
            ClientId = MobileAuthOptions.DefaultClientId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        });

        Assert.True(await repository.LinkRefreshTokenRotationAsync("ef-old-hash", "ef-new-hash"));
        Assert.Equal(
            "ef-new-hash",
            (await repository.FindRefreshTokenByHashAsync("ef-old-hash"))!.ReplacedByTokenHash);
        Assert.False(await repository.LinkRefreshTokenRotationAsync("ef-old-hash", "ef-another-hash"));
        Assert.False(await repository.LinkRefreshTokenRotationAsync("ef-missing-hash", "ef-new-hash"));
    }

    private async Task<MemberAccount> SeedMemberAsync()
    {
        var account = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "mobile-ef@example.com",
            NormalizedEmail = "MOBILE-EF@EXAMPLE.COM",
            DisplayName = "Mobile EF",
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.MemberAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account;
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
