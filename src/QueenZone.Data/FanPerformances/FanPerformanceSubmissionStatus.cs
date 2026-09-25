namespace QueenZone.Data;

public static class FanPerformanceSubmissionStatus
{
    public const string Pending = "Pending";
    public const string UnderReview = "UnderReview";
    public const string NeedsInfo = "NeedsInfo";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";

    public static readonly IReadOnlyList<string> All =
    [
        Pending,
        UnderReview,
        NeedsInfo,
        Approved,
        Rejected,
        Withdrawn,
    ];

    internal static readonly SubmissionStatusSet Statuses = new("fan-performance submission", All);

    public static bool IsKnown(string? status) => Statuses.IsKnown(status);

    public static string Normalize(string status) => Statuses.Normalize(status);
}
