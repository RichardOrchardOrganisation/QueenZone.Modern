using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class FanPerformanceSubmissionOptionsValidator : IValidateOptions<FanPerformanceSubmissionOptions>
{
    public const int MaxStaleAfterDays = 365;

    public ValidateOptionsResult Validate(string? name, FanPerformanceSubmissionOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{FanPerformanceSubmissionOptions.SectionName}:StaleAfterDays",
            options.StaleAfterDays,
            MaxStaleAfterDays);
        return OptionsValidation.Result(failures);
    }
}
