namespace QueenZone.Data.Entities;

public interface ILeaseEntity
{
    string LeaseName { get; set; }

    string HolderId { get; set; }

    DateTime AcquiredAtUtc { get; set; }

    DateTime ExpiresAtUtc { get; set; }
}
