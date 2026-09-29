namespace QueenZone.Data.Entities;

public sealed class NewsAgentRunRequestEntity : IRunRequestEntity
{
    public long Id { get; set; }

    public NewsAgentRunRequestStatus Status { get; set; }

    public NewsAgentRunRequestKind Kind { get; set; }

    public string RequestedBy { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }

    public string? ArticleUrl { get; set; }

    public bool GenerateDraft { get; set; }

    public string? RunnerId { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Summary { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ActiveKey { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
