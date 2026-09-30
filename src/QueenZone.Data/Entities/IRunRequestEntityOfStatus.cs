namespace QueenZone.Data.Entities;

public interface IRunRequestEntity<TStatus> : IRunRequestEntity
    where TStatus : struct, Enum
{
    TStatus Status { get; set; }

    DateTime? StartedAtUtc { get; set; }

    DateTime? CompletedAtUtc { get; set; }
}
