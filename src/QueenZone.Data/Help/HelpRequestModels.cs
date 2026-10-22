namespace QueenZone.Data;

public sealed record HelpRequest(
    Guid Id,
    string Topic,
    string Subject,
    string Message,
    string Name,
    string Email,
    string NormalizedEmail,
    Guid? MemberId,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewerEmail,
    string? ReviewNotes);

/// <summary>An email an admin sent in response to a help request, kept as an audit log.</summary>
public sealed record HelpRequestReply(
    Guid Id,
    Guid HelpRequestId,
    string ToEmail,
    string Subject,
    string Body,
    string SentByEmail,
    DateTimeOffset SentAt)
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 8000;
}

public sealed record HelpRequestListItem(
    Guid Id,
    string Topic,
    string Subject,
    string Name,
    string Email,
    Guid? MemberId,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record HelpRequestListPage(
    IReadOnlyList<HelpRequestListItem> Items,
    int TotalCount,
    string? StatusFilter);
