using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class HelpRequestOptionsValidator : IValidateOptions<HelpRequestOptions>
{
    public const int MaxPermitLimit = 10_000;

    public const int MaxMinimumDwellSeconds = 3_600;

    public ValidateOptionsResult Validate(string? name, HelpRequestOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{HelpRequestOptions.SectionName}:MaxAnonymousPerIpPerHour",
            options.MaxAnonymousPerIpPerHour,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{HelpRequestOptions.SectionName}:MaxPerMemberPerMinute",
            options.MaxPerMemberPerMinute,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{HelpRequestOptions.SectionName}:MaxPerEmailPerDay",
            options.MaxPerEmailPerDay,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{HelpRequestOptions.SectionName}:MaxPerMemberPerDay",
            options.MaxPerMemberPerDay,
            MaxPermitLimit);

        // Zero dwell is a deliberate disable switch used by appsettings.Testing.json,
        // not a submission cap. RequirePositiveAtMost would reject it.
        if (options.MinimumDwellSeconds is < 0 or > MaxMinimumDwellSeconds)
        {
            failures.Add(
                $"{HelpRequestOptions.SectionName}:MinimumDwellSeconds must be between 0 and {MaxMinimumDwellSeconds}.");
        }

        return OptionsValidation.Result(failures);
    }
}
