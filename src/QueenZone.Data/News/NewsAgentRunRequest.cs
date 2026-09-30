namespace QueenZone.Data;

public enum NewsAgentRunRequestStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public enum NewsAgentRunRequestKind
{
    ScheduledGathering = 0,
    UrlIngestion = 1
}

public sealed record NewsAgentRunRequest(
    long Id,
    NewsAgentRunRequestStatus Status,
    NewsAgentRunRequestKind Kind,
    string RequestedBy,
    DateTime RequestedAtUtc,
    string? ArticleUrl,
    bool GenerateDraft,
    string? RunnerId,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Summary,
    string? ErrorMessage) : IRunRequestRecord<NewsAgentRunRequest>
{
    bool IRunRequestRecord<NewsAgentRunRequest>.IsPending => Status == NewsAgentRunRequestStatus.Pending;

    bool IRunRequestRecord<NewsAgentRunRequest>.IsRunning => Status == NewsAgentRunRequestStatus.Running;

    NewsAgentRunRequest IRunRequestRecord<NewsAgentRunRequest>.AsPending() =>
        this with { Status = NewsAgentRunRequestStatus.Pending, RunnerId = null, StartedAtUtc = null };

    NewsAgentRunRequest IRunRequestRecord<NewsAgentRunRequest>.AsRunning(string runnerId, DateTime startedAtUtc) =>
        this with { Status = NewsAgentRunRequestStatus.Running, RunnerId = runnerId, StartedAtUtc = startedAtUtc };

    NewsAgentRunRequest IRunRequestRecord<NewsAgentRunRequest>.AsCompleted(DateTime completedAtUtc, string summary) =>
        this with
        {
            Status = NewsAgentRunRequestStatus.Completed,
            CompletedAtUtc = completedAtUtc,
            Summary = summary,
            ErrorMessage = null
        };

    NewsAgentRunRequest IRunRequestRecord<NewsAgentRunRequest>.AsFailed(DateTime completedAtUtc, string errorMessage) =>
        this with
        {
            Status = NewsAgentRunRequestStatus.Failed,
            CompletedAtUtc = completedAtUtc,
            ErrorMessage = errorMessage
        };
}

public sealed record NewsAgentRunRequestCreate(
    string RequestedBy,
    NewsAgentRunRequestKind Kind = NewsAgentRunRequestKind.ScheduledGathering,
    string? ArticleUrl = null,
    bool GenerateDraft = false);

public sealed record NewsAgentRunRequestQueueResult(
    NewsAgentRunRequest Request,
    bool WasCreated);

public sealed record NewsAgentRunnerHeartbeat(
    string RunnerId,
    DateTime LastSeenAtUtc,
    DateTime? LastClaimedAtUtc);
