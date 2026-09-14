using System.Diagnostics.CodeAnalysis;

namespace QueenZone.Data.Entities;

[ExcludeFromCodeCoverage]
public sealed class ForumPostReportEntity
{
    public Guid Id { get; set; }
    public int PostId { get; set; }
    public int TopicId { get; set; }
    public Guid ReporterMemberId { get; set; }
    public Guid? ReportedMemberId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = PrivateMessageReportStatus.Open;
    public string PostBodySnapshot { get; set; } = string.Empty;
    public string AuthorDisplayNameSnapshot { get; set; } = string.Empty;
    public DateTimeOffset PostCreatedAtSnapshot { get; set; }
    public string ThreadTitleSnapshot { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
    public MemberAccount? Reporter { get; set; }
    public MemberAccount? Reported { get; set; }
}
