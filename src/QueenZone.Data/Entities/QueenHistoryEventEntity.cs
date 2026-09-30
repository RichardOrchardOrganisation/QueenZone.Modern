using QueenZone.Data;

namespace QueenZone.Data.Entities;

public sealed class QueenHistoryEventEntity
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTime EventDate { get; set; }

    public int EventMonthDay { get; private set; }

    public QueenHistoryDatePrecision DatePrecision { get; set; }

    public QueenHistoryEventCategory Category { get; set; }

    public int Importance { get; set; }

    public QueenHistoryEventSourceType SourceType { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
