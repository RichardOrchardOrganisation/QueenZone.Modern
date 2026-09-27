namespace QueenZone.Data;

/// <summary>
/// A closed, case-insensitive set of status names for one kind of member submission. The
/// per-type <c>*SubmissionStatus</c> classes keep their constants and delegate here.
/// </summary>
/// <param name="description">Lower-case noun used in messages, e.g. "photo submission".</param>
/// <param name="all">Every status, in display order.</param>
internal sealed class SubmissionStatusSet(string description, IReadOnlyList<string> all)
{
    public string Description { get; } = description;

    public IReadOnlyList<string> All { get; } = all;

    public bool IsKnown(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && All.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

    public string Normalize(string status)
    {
        var match = All.FirstOrDefault(s =>
            string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
        return match
            ?? throw new ArgumentException($"Unknown {Description} status '{status}'.", nameof(status));
    }
}
