using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MemberFollowServiceTests
{
    [Fact]
    public async Task Follow_IsIdempotent_AndUnfollowRemovesIt()
    {
        var (service, _, follows, alice, bob) = CreateSystem();

        Assert.True((await service.FollowAsync(alice.Id, bob.Id)).Succeeded);
        Assert.True(await service.IsFollowingAsync(alice.Id, bob.Id));
        Assert.False(await service.IsFollowingAsync(bob.Id, alice.Id));

        Assert.True((await service.FollowAsync(alice.Id, bob.Id)).Succeeded);
        Assert.True(await follows.IsFollowingAsync(alice.Id, bob.Id));

        Assert.True(await service.UnfollowAsync(alice.Id, bob.Id));
        Assert.False(await service.IsFollowingAsync(alice.Id, bob.Id));
        Assert.False(await service.UnfollowAsync(alice.Id, bob.Id));
    }

    [Fact]
    public async Task Follow_RejectsSelfAndMissingOrDeletedMember()
    {
        var (service, members, _, alice, _) = CreateSystem();

        var self = await service.FollowAsync(alice.Id, alice.Id);
        Assert.False(self.Succeeded);
        Assert.Contains("yourself", self.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var missing = await service.FollowAsync(alice.Id, Guid.NewGuid());
        Assert.False(missing.Succeeded);
        Assert.Equal(MemberRelationshipOutcome.MemberNotFound, missing.Outcome);
        Assert.Contains("not found", missing.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var deleted = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "gone@example.com",
            DisplayName = "Gone",
            CreatedAt = DateTime.UtcNow,
        });
        await members.RequestDeletionAsync(deleted.Id, DateTime.UtcNow);
        var followDeleted = await service.FollowAsync(alice.Id, deleted.Id);
        Assert.False(followDeleted.Succeeded);
        Assert.Equal(MemberRelationshipOutcome.MemberNotFound, followDeleted.Outcome);
        Assert.Contains("not found", followDeleted.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListFollowedIds_ReturnsFollowedMembers()
    {
        var (service, _, _, alice, bob) = CreateSystem();
        Assert.Empty(await service.ListFollowedIdsAsync(alice.Id));

        Assert.True((await service.FollowAsync(alice.Id, bob.Id)).Succeeded);
        Assert.Equal([bob.Id], await service.ListFollowedIdsAsync(alice.Id));
        Assert.Empty(await service.ListFollowedIdsAsync(bob.Id));
    }

    private static (
        MemberFollowService Service,
        IMemberAccountRepository Members,
        IMemberFollowRepository Follows,
        MemberAccount Alice,
        MemberAccount Bob) CreateSystem()
    {
        var members = new InMemoryMemberAccountRepository();
        var alice = members.CreateAsync(new MemberAccount
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Email = "alice-follow@example.com",
            DisplayName = "Alice",
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();
        var bob = members.CreateAsync(new MemberAccount
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Email = "bob-follow@example.com",
            DisplayName = "Bob",
            CreatedAt = DateTime.UtcNow,
        }).GetAwaiter().GetResult();
        var follows = new InMemoryMemberFollowRepository();
        var service = new MemberFollowService(follows, members, TimeProvider.System);
        return (service, members, follows, alice, bob);
    }
}
