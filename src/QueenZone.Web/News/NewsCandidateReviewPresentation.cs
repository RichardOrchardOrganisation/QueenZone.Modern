using QueenZone.Data;

namespace QueenZone.Web;

public sealed record NewsCandidateReviewActions(bool CanGenerateDraft, string GenerationLabel, string GenerationBusyLabel,
    bool ConfirmGeneration, bool CanPromote, bool CanReject, bool CanIgnoreDuplicate);

public static class NewsCandidateReviewPresentation
{
    public static NewsCandidateReviewActions Actions(NewsCandidateStatus status, bool hasDraft) => new(
        status is NewsCandidateStatus.Discovered or NewsCandidateStatus.NeedsReview or NewsCandidateStatus.Rejected
            || (hasDraft && status == NewsCandidateStatus.Drafted),
        hasDraft ? "Regenerate draft with AI" : "Generate draft with AI",
        hasDraft ? "Regenerating draft" : "Generating draft",
        hasDraft,
        hasDraft,
        status != NewsCandidateStatus.Rejected,
        status != NewsCandidateStatus.IgnoredDuplicate);

    public static string PublishedDate(DateTime? date) => date?.ToString("dd MMM yyyy") ?? "Unknown";

    public static string Score(decimal? score) => score?.ToString("0.00") ?? "—";

    public static string GuidanceRevision(int? revision) => revision?.ToString() ?? "compiled default";

    public static string GuidanceHash(string? hash) => hash ?? "—";
}
