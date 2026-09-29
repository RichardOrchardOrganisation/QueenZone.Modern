namespace QueenZone.Data.Entities;

public sealed class NewsAgentRunLeaseEntity : ILeaseEntity
{
    public string LeaseName { get; set; } = string.Empty;

    public string HolderId { get; set; } = string.Empty;

    public DateTime AcquiredAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}
