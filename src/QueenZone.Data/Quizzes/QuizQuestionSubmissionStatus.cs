namespace QueenZone.Data;

public static class QuizQuestionSubmissionStatus
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

    public static bool IsKnown(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && All.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsPendingReview(string status) =>
        string.Equals(Normalize(status), Pending, StringComparison.Ordinal);

    public static string Normalize(string status)
    {
        var match = All.FirstOrDefault(s =>
            string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
        return match
            ?? throw new ArgumentException($"Unknown quiz question submission status '{status}'.", nameof(status));
    }
}
