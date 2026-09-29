namespace QueenZone.Data.Entities;

public interface IRunRequestEntity
{
    long Id { get; set; }

    string RequestedBy { get; set; }

    DateTime RequestedAtUtc { get; set; }

    string? RunnerId { get; set; }

    string? Summary { get; set; }

    string? ErrorMessage { get; set; }

    string? ActiveKey { get; set; }

    DateTime UpdatedAtUtc { get; set; }
}
