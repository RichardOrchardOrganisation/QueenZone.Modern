using QueenZone.Data;

namespace QueenZone.Web;

public sealed class FanPerformanceReportService(
    IFanPerformanceReportRepository reportRepository,
    IFanPerformanceRepository fanPerformanceRepository,
    AdminFanPerformanceWriteService adminFanPerformanceWriteService)
{
    public async Task<FanPerformanceReportCreateResult> CreateAsync(
        Guid reporterMemberId,
        int stageId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (reporterMemberId == Guid.Empty)
        {
            return FanPerformanceReportCreateResult.Failure(FanPerformanceReportOutcome.Invalid, "Sign in is required to report a fan performance.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FanPerformanceReportCreateResult.Failure(FanPerformanceReportOutcome.Invalid, "A reason is required.");
        }

        var trimmed = reason.Trim();
        if (trimmed.Length > FanPerformanceReportLimits.MaxReasonLength)
        {
            return FanPerformanceReportCreateResult.Failure(
                FanPerformanceReportOutcome.Invalid,
                $"Reason must be {FanPerformanceReportLimits.MaxReasonLength} characters or fewer.");
        }

        var performance = await fanPerformanceRepository.GetByIdAsync(stageId, cancellationToken);
        if (performance is null)
        {
            return FanPerformanceReportCreateResult.Failure(FanPerformanceReportOutcome.NotFound, "Fan performance was not found.");
        }

        return await reportRepository.CreateAsync(
            new NewFanPerformanceReport(
                stageId,
                reporterMemberId,
                trimmed,
                TruncateSnapshot(performance.Title),
                TruncateSnapshot(performance.PerformedBy)),
            cancellationToken);
    }

    public async Task<FanPerformanceReport?> HideAndResolveAsync(
        Guid reportId,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var report = await reportRepository.GetByIdAsync(reportId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        await adminFanPerformanceWriteService.HideAsync(report.StageId, editorEmail, cancellationToken: cancellationToken);
        return await reportRepository.UpdateStatusAsync(
            reportId,
            FanPerformanceReportStatus.Resolved,
            editorEmail,
            cancellationToken);
    }

    public Task<FanPerformanceReport?> DismissAsync(
        Guid reportId,
        string editorEmail,
        CancellationToken cancellationToken = default) =>
        reportRepository.UpdateStatusAsync(
            reportId,
            FanPerformanceReportStatus.Dismissed,
            editorEmail,
            cancellationToken);

    private static string? TruncateSnapshot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= FanPerformanceReportLimits.MaxSnapshotLength
            ? trimmed
            : trimmed[..FanPerformanceReportLimits.MaxSnapshotLength];
    }
}
