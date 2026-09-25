using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>Resolves a leaderboard page and its optional viewer in one member lookup.</summary>
public static class QuizLeaderboardNameReader
{
    public static Task<IReadOnlyDictionary<Guid, string>> LoadAsync(
        IMemberAccountRepository members,
        IEnumerable<Guid> topMemberIds,
        Guid? viewerMemberId,
        CancellationToken cancellationToken)
    {
        var ids = topMemberIds
            .Concat(viewerMemberId is Guid viewer ? [viewer] : [])
            .Distinct()
            .ToArray();
        return members.ListDisplayNamesAsync(ids, cancellationToken);
    }

    public static string DisplayName(IReadOnlyDictionary<Guid, string> names, Guid memberId) =>
        names.GetValueOrDefault(memberId) ?? "Member";
}
