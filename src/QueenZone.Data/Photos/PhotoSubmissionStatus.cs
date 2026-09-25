namespace QueenZone.Data;

public static class PhotoSubmissionStatus
{
    public const string Pending = "Pending";
    public const string UnderReview = "UnderReview";
    public const string NeedsInfo = "NeedsInfo";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static readonly IReadOnlyList<string> All =
    [
        Pending,
        UnderReview,
        NeedsInfo,
        Approved,
        Rejected,
    ];

    internal static readonly SubmissionStatusSet Statuses = new("photo submission", All);

    public static bool IsKnown(string? status) => Statuses.IsKnown(status);

    public static string Normalize(string status) => Statuses.Normalize(status);
}
