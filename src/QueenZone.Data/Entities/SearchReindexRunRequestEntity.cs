namespace QueenZone.Data.Entities;

public sealed class SearchReindexRunRequestEntity : IRunRequestEntity
{
    public long Id { get; set; }

    public SearchReindexRunRequestStatus Status { get; set; }

    public string RequestedBy { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }

    public string? RunnerId { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Summary { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ActiveKey { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
