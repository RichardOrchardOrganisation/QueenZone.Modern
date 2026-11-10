using QueenZone.Data;

namespace QueenZone.Web;

public sealed class MemberFollowService(
    IMemberFollowRepository memberFollowRepository,
    IMemberAccountRepository memberAccountRepository,
    TimeProvider timeProvider)
{
    public const string UnableToFollow = "Unable to follow this member.";

    public Task<bool> IsFollowingAsync(
        Guid followerMemberId,
        Guid followedMemberId,
        CancellationToken cancellationToken = default) =>
        memberFollowRepository.IsFollowingAsync(followerMemberId, followedMemberId, cancellationToken);

    public async Task<MemberFollowResult> FollowAsync(
        Guid followerMemberId,
        Guid followedMemberId,
        CancellationToken cancellationToken = default)
    {
        if (followerMemberId == followedMemberId)
        {
            return MemberFollowResult.Failure(MemberRelationshipOutcome.Invalid, "You cannot follow yourself.");
        }

        var target = await memberAccountRepository.FindByIdAsync(followedMemberId, cancellationToken);
        if (target is null || target.DeletionRequestedAt is not null)
        {
            return MemberFollowResult.Failure(MemberRelationshipOutcome.MemberNotFound, "Member was not found.");
        }

        await memberFollowRepository.FollowAsync(
            followerMemberId,
            followedMemberId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return MemberFollowResult.Success();
    }

    public Task<bool> UnfollowAsync(
        Guid followerMemberId,
        Guid followedMemberId,
        CancellationToken cancellationToken = default) =>
        memberFollowRepository.UnfollowAsync(followerMemberId, followedMemberId, cancellationToken);

    public Task<IReadOnlyList<Guid>> ListFollowedIdsAsync(
        Guid followerMemberId,
        CancellationToken cancellationToken = default) =>
        memberFollowRepository.ListFollowedIdsAsync(followerMemberId, cancellationToken);
}
