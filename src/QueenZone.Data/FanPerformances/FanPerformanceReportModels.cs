namespace QueenZone.Data;

public sealed record FanPerformanceReport(
    Guid Id,
    int StageId,
    Guid ReporterMemberId,
    string ReporterDisplayName,
    string Reason,
    DateTimeOffset CreatedAt,
    string Status,
    string? TitleSnapshot,
    string? PerformedBySnapshot,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt);

public sealed record FanPerformanceReportListItem(
    Guid Id,
    int StageId,
    Guid ReporterMemberId,
    string ReporterDisplayName,
    string Reason,
    DateTimeOffset CreatedAt,
    string Status,
    string? TitleSnapshot,
    string? PerformedBySnapshot);

public sealed record FanPerformanceReportListPage(
    IReadOnlyList<FanPerformanceReportListItem> Items,
    int TotalCount,
    string StatusFilter);

public sealed record NewFanPerformanceReport(
    int StageId,
    Guid ReporterMemberId,
    string Reason,
    string? TitleSnapshot,
    string? PerformedBySnapshot);

/// <summary>Typed outcome of reporting a fan performance so callers never branch on message text (#2436).</summary>
public enum FanPerformanceReportOutcome
{
    Created,
    AlreadyReported,
    NotFound,
    Invalid,
}

public sealed record FanPerformanceReportCreateResult(
    FanPerformanceReportOutcome Outcome,
    Guid? ReportId,
    string? Error)
{
    public bool Succeeded => Outcome is FanPerformanceReportOutcome.Created or FanPerformanceReportOutcome.AlreadyReported;

    public bool AlreadyReported => Outcome == FanPerformanceReportOutcome.AlreadyReported;

    public static FanPerformanceReportCreateResult Created(Guid reportId) =>
        new(FanPerformanceReportOutcome.Created, reportId, null);

    public static FanPerformanceReportCreateResult Duplicate(Guid reportId) =>
        new(FanPerformanceReportOutcome.AlreadyReported, reportId, null);

    public static FanPerformanceReportCreateResult Failure(FanPerformanceReportOutcome outcome, string error) =>
        new(outcome, null, error);
}
