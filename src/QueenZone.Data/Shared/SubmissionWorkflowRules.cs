namespace QueenZone.Data;

/// <summary>
/// Transition rules over a <see cref="SubmissionStatusSet"/>. Each submission type declares only
/// its allowed-transition table (business rules); validation and error text live here.
/// </summary>
internal sealed class SubmissionWorkflowRules(
    SubmissionStatusSet statuses,
    IReadOnlyDictionary<string, string[]> allowedTransitions)
{
    public bool CanTransition(string current, string next)
    {
        if (!statuses.IsKnown(current) || !statuses.IsKnown(next))
        {
            return false;
        }

        var normalizedCurrent = statuses.Normalize(current);
        var normalizedNext = statuses.Normalize(next);
        return allowedTransitions.TryGetValue(normalizedCurrent, out var allowed)
            && allowed.Contains(normalizedNext, StringComparer.Ordinal);
    }

    public bool TryValidateStatusChange(string current, string next, out string? error)
    {
        if (!statuses.IsKnown(current))
        {
            error = $"Unknown current status '{current}'.";
            return false;
        }

        if (!statuses.IsKnown(next))
        {
            error = $"Unknown target status '{next}'.";
            return false;
        }

        var normalizedCurrent = statuses.Normalize(current);
        var normalizedNext = statuses.Normalize(next);

        if (string.Equals(normalizedCurrent, normalizedNext, StringComparison.Ordinal))
        {
            error = $"This submission is already {normalizedNext}.";
            return false;
        }

        if (CanTransition(normalizedCurrent, normalizedNext))
        {
            error = null;
            return true;
        }

        error = $"Cannot transition {statuses.Description} status from {normalizedCurrent} to {normalizedNext}.";
        return false;
    }
}
