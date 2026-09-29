namespace QueenZone.Data;

/// <summary>Transitions the in-memory run-request stores apply to the request record of a queue.</summary>
public interface IRunRequestRecord<TSelf>
    where TSelf : IRunRequestRecord<TSelf>
{
    long Id { get; }

    DateTime RequestedAtUtc { get; }

    DateTime? StartedAtUtc { get; }

    bool IsPending { get; }

    bool IsRunning { get; }

    bool IsActive => IsPending || IsRunning;

    TSelf AsPending();

    TSelf AsRunning(string runnerId, DateTime startedAtUtc);

    TSelf AsCompleted(DateTime completedAtUtc, string summary);

    TSelf AsFailed(DateTime completedAtUtc, string errorMessage);
}
