using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class DeviceTokenRepositoryTests
{
    [Fact]
    public async Task ListByMemberIdsAsync_ReturnsEachDevice_AndClonesRows()
    {
        var store = new SharedDeviceTokenStore();
        var repository = new InMemoryDeviceTokenRepository(store);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();

        await repository.UpsertAsync(DeviceTokenTestData.Token(alice, DevicePushPlatform.Apns, "alice-phone"));
        await repository.UpsertAsync(DeviceTokenTestData.Token(alice, DevicePushPlatform.Fcm, "alice-android"));
        await repository.UpsertAsync(DeviceTokenTestData.Token(bob, DevicePushPlatform.Apns, "bob-phone"));
        await repository.UpsertAsync(DeviceTokenTestData.Token(carol, DevicePushPlatform.Fcm, "carol-android"));

        var listed = await repository.ListByMemberIdsAsync([alice, bob, alice]);

        Assert.Equal(3, listed.Count);
        Assert.Equal(2, listed.Count(row => row.MemberAccountId == alice));
        Assert.Contains(listed, row => row.MemberAccountId == bob && row.Token == "bob-phone");
        Assert.DoesNotContain(listed, row => row.MemberAccountId == carol);

        listed[0].Token = "mutated";
        lock (store.Gate)
        {
            Assert.DoesNotContain(store.Tokens, row => row.Token == "mutated");
        }
    }

    [Fact]
    public async Task DeleteIfTokenMatchesAsync_PreservesRefreshedRegistration()
    {
        var repository = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());
        var memberId = Guid.NewGuid();
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var stale = await repository.UpsertAsync(
            DeviceTokenTestData.Token(memberId, DevicePushPlatform.Apns, "old", deviceId));
        var staleUpdatedAt = stale.UpdatedAt;
        var refreshedToken = DeviceTokenTestData.Token(memberId, DevicePushPlatform.Apns, "new", deviceId);
        refreshedToken.UpdatedAt = staleUpdatedAt.AddMinutes(1);
        var fresh = await repository.UpsertAsync(refreshedToken);

        Assert.False(await repository.DeleteIfTokenMatchesAsync(
            stale.Id, staleUpdatedAt, memberId, DevicePushPlatform.Apns, "old"));
        Assert.True(await repository.DeleteIfTokenMatchesAsync(
            fresh.Id, fresh.UpdatedAt, memberId, DevicePushPlatform.Apns, "new"));
        Assert.Empty(await repository.ListByMemberIdsAsync([memberId]));
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_SameMember_UpdatesTokenPlatformAndUpdatedAt()
    {
        var store = new SharedDeviceTokenStore();
        var repository = new InMemoryDeviceTokenRepository(store);
        var alice = Guid.NewGuid();
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        var first = DeviceTokenTestData.Token(alice, DevicePushPlatform.Apns, "token-old", deviceId);
        first.UpdatedAt = DateTime.UtcNow.AddMinutes(-5);
        await repository.UpsertAsync(first);

        var second = DeviceTokenTestData.Token(alice, DevicePushPlatform.Fcm, "token-new", deviceId);
        second.UpdatedAt = DateTime.UtcNow;
        var updated = await repository.UpsertAsync(second);

        Assert.Equal(alice, updated.MemberAccountId);
        Assert.Equal(DevicePushPlatform.Fcm, updated.Platform);
        Assert.Equal("token-new", updated.Token);
        Assert.Equal(second.UpdatedAt, updated.UpdatedAt);
        lock (store.Gate)
        {
            var stored = Assert.Single(store.Tokens);
            Assert.Equal(first.Id, stored.Id);
        }
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_DifferentMember_ReassignsOwnership()
    {
        var store = new SharedDeviceTokenStore();
        var repository = new InMemoryDeviceTokenRepository(store);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        const string deviceId = "e3c869b0-f770-4ee4-be4a-46c63ccba90f";
        await repository.UpsertAsync(DeviceTokenTestData.Token(alice, DevicePushPlatform.Apns, "alice-tok", deviceId));

        var updated = await repository.UpsertAsync(
            DeviceTokenTestData.Token(bob, DevicePushPlatform.Fcm, "bob-tok", deviceId));

        Assert.Equal(bob, updated.MemberAccountId);
        Assert.Equal("bob-tok", updated.Token);
        lock (store.Gate)
        {
            var stored = Assert.Single(store.Tokens);
            Assert.Equal(bob, stored.MemberAccountId);
        }
    }

    [Fact]
    public async Task UpsertAsync_SameDeviceId_DifferentCasing_UpdatesInPlace()
    {
        var store = new SharedDeviceTokenStore();
        var repository = new InMemoryDeviceTokenRepository(store);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await repository.UpsertAsync(DeviceTokenTestData.Token(
            alice, DevicePushPlatform.Apns, "alice-tok", "E3C869B0-F770-4EE4-BE4A-46C63CCBA90F"));

        var updated = await repository.UpsertAsync(DeviceTokenTestData.Token(
            bob, DevicePushPlatform.Fcm, "bob-tok", "e3c869b0-f770-4ee4-be4a-46c63ccba90f"));

        Assert.Equal(bob, updated.MemberAccountId);
        Assert.Equal("bob-tok", updated.Token);
        lock (store.Gate)
        {
            Assert.Single(store.Tokens);
        }
    }

    [Fact]
    public async Task ListByMemberIdsAsync_EmptyInput_ReturnsEmpty()
    {
        var repository = new InMemoryDeviceTokenRepository(new SharedDeviceTokenStore());

        var listed = await repository.ListByMemberIdsAsync([]);

        Assert.Empty(listed);
    }
}
