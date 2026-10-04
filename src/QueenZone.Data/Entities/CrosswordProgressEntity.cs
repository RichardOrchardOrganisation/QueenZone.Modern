namespace QueenZone.Data.Entities;

public sealed class CrosswordProgressEntity
{
    public Guid Id { get; set; }
    public Guid CrosswordId { get; set; }
    public Guid MemberId { get; set; }
    public string GridFingerprint { get; set; } = string.Empty;
    public string Letters { get; set; } = string.Empty;
    public string RevealedCellsJson { get; set; } = "[]";
    public int ElapsedSeconds { get; set; }
    public bool AutoCheckUsed { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Immutable first completion; resetting an edited puzzle's in-progress saves must preserve these rows.</summary>
public sealed class CrosswordCompletionEntity
{
    public Guid Id { get; set; }
    public Guid CrosswordId { get; set; }
    public Guid MemberId { get; set; }
    public int ElapsedSeconds { get; set; }
    public bool Clean { get; set; }
    public bool RankingEligible { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
