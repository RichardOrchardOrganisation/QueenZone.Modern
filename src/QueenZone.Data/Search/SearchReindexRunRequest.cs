namespace QueenZone.Data;

public enum SearchReindexRunRequestStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed record SearchReindexRunRequest(
    long Id,
    SearchReindexRunRequestStatus Status,
    string RequestedBy,
    DateTime RequestedAtUtc,
    string? RunnerId,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Summary,
    string? ErrorMessage) : IRunRequestRecord<SearchReindexRunRequest>
{
    bool IRunRequestRecord<SearchReindexRunRequest>.IsPending => Status == SearchReindexRunRequestStatus.Pending;

    bool IRunRequestRecord<SearchReindexRunRequest>.IsRunning => Status == SearchReindexRunRequestStatus.Running;

    SearchReindexRunRequest IRunRequestRecord<SearchReindexRunRequest>.AsPending() =>
        this with { Status = SearchReindexRunRequestStatus.Pending, RunnerId = null, StartedAtUtc = null };

    SearchReindexRunRequest IRunRequestRecord<SearchReindexRunRequest>.AsRunning(string runnerId, DateTime startedAtUtc) =>
        this with { Status = SearchReindexRunRequestStatus.Running, RunnerId = runnerId, StartedAtUtc = startedAtUtc };

    SearchReindexRunRequest IRunRequestRecord<SearchReindexRunRequest>.AsCompleted(DateTime completedAtUtc, string summary) =>
        this with
        {
            Status = SearchReindexRunRequestStatus.Completed,
            CompletedAtUtc = completedAtUtc,
            Summary = summary,
            ErrorMessage = null
        };

    SearchReindexRunRequest IRunRequestRecord<SearchReindexRunRequest>.AsFailed(DateTime completedAtUtc, string errorMessage) =>
        this with
        {
            Status = SearchReindexRunRequestStatus.Failed,
            CompletedAtUtc = completedAtUtc,
            ErrorMessage = errorMessage
        };
}

public sealed record SearchReindexRunRequestCreate(string RequestedBy);

public sealed record SearchReindexRunRequestQueueResult(
    SearchReindexRunRequest Request,
    bool WasCreated);
