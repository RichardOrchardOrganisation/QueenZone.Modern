namespace QueenZone.Data;

public interface IMemberPublicActivityRepository
{
    /// <summary>
    /// Newest-first public activity for a single member's profile. When
    /// <paramref name="linkedLegacyUserId"/> is set, that legacy account's archive forum posts are
    /// merged in and attributed to the member, so linking an account does not hide old posts.
    /// </summary>
    Task<MemberPublicActivityPage> GetPageAsync(
        Guid memberId,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Newest-first public activity for every author in <paramref name="memberIds"/>.
    /// Same four sources and item shape as <see cref="GetPageAsync"/>, without linked legacy posts.
    /// An empty id set returns no rows and does not query the sources.
    /// </summary>
    Task<MemberPublicActivityPage> GetFeedPageAsync(
        IReadOnlyCollection<Guid> memberIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
