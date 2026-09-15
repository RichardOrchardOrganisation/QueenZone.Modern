namespace QueenZone.Data;

/// <summary>
/// Reads all posts by a single unlinked legacy forum author (keyed by their stable legacy user
/// id, not their display name, since the same id can carry different display-name spellings
/// across posts). Once an author links their legacy account, their archive posts are served on
/// their member profile by <see cref="IMemberPublicActivityRepository.GetPageAsync"/> instead.
/// </summary>
public interface IForumArchiveAuthorRepository
{
    Task<ForumArchiveAuthorSummary?> GetSummaryAsync(
        int legacyUserId,
        CancellationToken cancellationToken = default);

    Task<MemberPublicActivityPage> GetPostsPageAsync(
        int legacyUserId,
        int page,
        int pageSize,
        int totalCount,
        CancellationToken cancellationToken = default);
}
