namespace QueenZone.Data;

public interface IForumWriteRepository
{
    Task<ForumThreadCreateResult> CreateThreadAsync(NewForumThread thread, CancellationToken cancellationToken = default);

    Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default);

    Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default);

    Task<ForumPostUpdateResult> UpdatePostAsync(
        int postId,
        Guid editorMemberId,
        string sanitisedBody,
        bool isAdmin,
        int editWindowMinutes,
        DateTimeOffset? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default);

    Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default);

    /// <summary>Counts visible member posts, excluding imported legacy posts with null AuthorMemberId.</summary>
    Task<int> CountPostsByMemberSinceAsync(Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Counts visible member posts, excluding imported legacy posts with null AuthorMemberId.</summary>
    Task<int> CountApprovedPostsByMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<ForumAuthorContentSummary> GetAuthorForumContentSummaryAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ForumAuthorContentSummary(memberId, displayName, 0, 0, false));

    Task<ForumAuthorContentSummary?> FindNoAccountForumAuthorAsync(
        string displayName, CancellationToken cancellationToken = default) => Task.FromResult<ForumAuthorContentSummary?>(null);

    Task HideAuthorForumContentAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default);

    Task UnhideAuthorForumContentAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a category by URL slug first, then by case-insensitive name, or
    /// creates one named <paramref name="name"/>. Never returns The Music.
    /// </summary>
    Task<int> EnsureCategoryAsync(string slug, string name, CancellationToken cancellationToken = default);
}
