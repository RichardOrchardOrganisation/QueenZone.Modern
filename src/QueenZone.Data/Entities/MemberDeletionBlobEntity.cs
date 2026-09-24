namespace QueenZone.Data.Entities;

/// <summary>Durable cleanup work after personal rows are purged.</summary>
public sealed class MemberDeletionBlobEntity
{
    public Guid Id { get; set; }

    public Guid MemberAccountId { get; set; }

    public string Container { get; set; } = string.Empty;

    public string BlobPath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
