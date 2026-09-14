namespace QueenZone.Web;

/// <summary>
/// List-card shape for <c>/api/v1/forum/categories</c>. Same public fields as the
/// website board cards on <c>/forum</c>.
/// </summary>
public sealed record ForumCategoryListItemDto(
    int Id,
    string Name,
    string? Description,
    int PostCount,
    DateTime? LastActivityAt,
    string? LatestThreadTitle,
    string DetailPath);

/// <summary>
/// Thread row for <c>/api/v1/forum/categories/{id}/topics</c>. Same public fields
/// as the website topic list on a category page.
/// </summary>
public sealed record ForumTopicListItemDto(
    int Id,
    string Title,
    DateTime LastActivityAt,
    string AuthorUsername,
    int ReplyCount,
    string? LastPostUsername,
    bool IsSticky,
    string DetailPath);

/// <summary>
/// Thread header for <c>/api/v1/forum/topics/{id}</c>. Same public fields as the
/// website topic page (title, board, post count), plus <see cref="IsLocked"/>
/// so clients can hide compose instead of inviting a 403.
/// </summary>
public sealed record ForumTopicDetailDto(
    int Id,
    string Title,
    int ForumId,
    string ForumName,
    string CategoryPath,
    string DetailPath,
    int PostCount,
    bool? HasPoll,
    bool IsLocked);

/// <summary>
/// Chronological post on <c>/api/v1/forum/topics/{id}/posts</c>. Body is the same
/// sanitized HTML the website renders via <see cref="UgcHtml.FormatForDisplay"/>.
/// </summary>
public sealed record ForumPostDto(
    int Id,
    string Body,
    DateTime PostedAt,
    string AuthorUsername,
    string? Signature,
    DateTime? AuthorMemberSince,
    Guid? AuthorMemberId,
    DateTimeOffset? EditedAt,
    int EditCount,
    IReadOnlyList<ForumAttachmentDto> Attachments);

/// <summary>
/// Attachment card for a forum post. <see cref="Url"/> is the existing
/// cookie-gated download path (<c>/forum/attachment/...</c>) and stays that
/// shape for the website. <see cref="DownloadUrl"/> is the additive Bearer
/// alias under <c>/api/v1/forum/attachments/...</c> for mobile.
/// </summary>
public sealed record ForumAttachmentDto(
    string FileName,
    string Url,
    string Extension,
    string FormattedSize,
    bool IsImage,
    string? ThumbnailUrl,
    string DownloadUrl);

/// <summary>
/// Index totals for <c>/api/v1/forum/stats</c>. Same numeric sources as the
/// website forum index: <see cref="PublicContentMapper.ToForumIndexStats"/>
/// with <see cref="PublicQueryCacheService.GetForumThreadCountAsync"/>.
/// <see cref="BoardCount"/> is the public board count; <see cref="ThreadCount"/>
/// is the archive thread total (not a sum of category DTOs).
/// </summary>
public sealed record ForumIndexStatsDto(
    int BoardCount,
    int ThreadCount,
    long PostCount);

/// <summary>
/// Cross-board recent-activity row for <c>/api/v1/forum/recent-threads</c>. Same source
/// (<see cref="PublicQueryCacheService.GetForumRecentThreadsAsync"/>) as the website
/// forum index's activity feed, most-recent first.
/// </summary>
public sealed record ForumRecentThreadDto(
    int TopicId,
    string Title,
    int CategoryId,
    string CategoryName,
    int ReplyCount,
    DateTime LastActivityAt,
    string DetailPath);

/// <summary>
/// Create-topic or reply body. Plain text is wrapped as HTML before the same
/// <see cref="UgcHtml"/> sanitizer the website uses. Polls are not accepted on
/// these write routes (#734).
/// </summary>
public sealed record ForumWriteRequestDto
{
    public string? Title { get; init; }

    public string? Subject { get; init; }

    public string? Body { get; init; }

    public string? ResolvedTitle =>
        string.IsNullOrWhiteSpace(Title) ? Subject : Title;
}

/// <summary>Result of <c>POST /api/v1/forum/categories/{id}/topics</c>.</summary>
public sealed record ForumTopicCreatedDto(
    int Id,
    int StarterPostId,
    string Title,
    string DetailPath);

/// <summary>
/// Result of <c>POST /api/v1/forum/topics/{id}/posts</c>.
/// <see cref="DetailPath"/> is the website topic URL plus <c>#post-{id}</c>;
/// the 201 Location header uses the same value.
/// </summary>
public sealed record ForumPostCreatedDto(
    int Id,
    int TopicId,
    string DetailPath);

public sealed record ForumPostUpdateRequestDto
{
    public string? Body { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record ForumPostReportRequestDto
{
    public string? Category { get; init; }
    public string? Details { get; init; }
}

public sealed record ForumPostReportResponseDto(Guid ReportId, string Status, bool AlreadyReported);

public sealed record ForumPostModerationStateRequestDto
{
    public IReadOnlyList<int> PostIds { get; init; } = [];
    public IReadOnlyList<Guid> AuthorMemberIds { get; init; } = [];
}

public sealed record ForumPostModerationStateDto(
    IReadOnlyList<int> ReportedPostIds,
    IReadOnlyList<Guid> BlockedMemberIds);

/// <summary>
/// Poll card for <c>/api/v1/forum/topics/{id}/poll</c>. Same fields the website
/// renders in <c>_ForumPoll.cshtml</c> (question, close state, vote vs results).
/// </summary>
public sealed record ForumPollDto(
    Guid PollId,
    int TopicId,
    string Question,
    bool IsMultiChoice,
    int? MaxChoices,
    DateTimeOffset? ClosesAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    int TotalVotes,
    int DistinctVoters,
    bool ViewerHasVoted,
    bool IsClosed,
    bool CanViewerVote,
    bool CanViewerClose,
    IReadOnlyList<ForumPollOptionDto> Options);

public sealed record ForumPollOptionDto(
    Guid OptionId,
    string OptionText,
    int DisplayOrder,
    int VoteCount,
    double Percentage,
    bool SelectedByViewer);

/// <summary>
/// Vote body for <c>POST /api/v1/forum/topics/{id}/poll/vote</c>. Accepts the
/// same single-choice <see cref="OptionId"/> or multi-choice <see cref="OptionIds"/>
/// the website form posts.
/// </summary>
public sealed record ForumPollVoteRequestDto
{
    public IReadOnlyList<Guid>? OptionIds { get; init; }

    public Guid? OptionId { get; init; }
}
