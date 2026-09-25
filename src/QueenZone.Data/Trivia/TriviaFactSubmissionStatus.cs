namespace QueenZone.Data;

public static class TriviaFactSubmissionStatus
{
    public const string Pending = "Pending";

    public const string Approved = "Approved";

    public const string Rejected = "Rejected";

    public static readonly IReadOnlyList<string> All =
    [
        Pending,
        Approved,
        Rejected,
    ];

    internal static readonly SubmissionStatusSet Statuses = new("trivia fact submission", All);

    public static bool IsKnown(string? status) => Statuses.IsKnown(status);

    public static bool IsPendingReview(string status) =>
        string.Equals(Normalize(status), Pending, StringComparison.Ordinal);

    public static string Normalize(string status) => Statuses.Normalize(status);
}
