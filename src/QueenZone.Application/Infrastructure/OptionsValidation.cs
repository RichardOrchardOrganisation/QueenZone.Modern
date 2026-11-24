using Microsoft.Extensions.Options;

namespace QueenZone.Application;

internal static class OptionsValidation
{
    public static void RequirePositiveAtMost(
        ICollection<string> failures,
        string name,
        int value,
        int maximum)
    {
        if (value <= 0 || value > maximum)
        {
            failures.Add($"{name} must be greater than 0 and at most {maximum}.");
        }
    }

    public static void RequirePositiveAtMost(
        ICollection<string> failures,
        string name,
        long value,
        long maximum)
    {
        if (value <= 0 || value > maximum)
        {
            failures.Add($"{name} must be greater than 0 and at most {maximum}.");
        }
    }

    public static void RequireNonBlankEntries(
        ICollection<string> failures,
        string name,
        IEnumerable<string>? values,
        bool requireAtLeastOne)
    {
        var list = values?.ToList() ?? [];
        if (requireAtLeastOne && list.Count == 0)
        {
            failures.Add($"{name} must contain at least one entry.");
        }

        if (list.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{name} must not contain blank entries.");
        }
    }

    public static bool LooksConfigured(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return !trimmed.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "TODO", StringComparison.OrdinalIgnoreCase);
    }

    public static ValidateOptionsResult Result(IReadOnlyCollection<string> failures) =>
        failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
}
