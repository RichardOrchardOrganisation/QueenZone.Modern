using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class MutationRateLimitingOptionsValidator
    : IValidateOptions<MutationRateLimitingOptions>
{
    public const int MaxPermitLimit = 10_000;

    public const int MaxWindowMinutes = 1_440;

    public ValidateOptionsResult Validate(string? name, MutationRateLimitingOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{MutationRateLimitingOptions.SectionName}:AnonymousPermitLimit",
            options.AnonymousPermitLimit,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{MutationRateLimitingOptions.SectionName}:AnonymousWindowMinutes",
            options.AnonymousWindowMinutes,
            MaxWindowMinutes);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{MutationRateLimitingOptions.SectionName}:AuthenticatedMemberPermitLimit",
            options.AuthenticatedMemberPermitLimit,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{MutationRateLimitingOptions.SectionName}:AuthenticatedMemberWindowMinutes",
            options.AuthenticatedMemberWindowMinutes,
            MaxWindowMinutes);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{MutationRateLimitingOptions.SectionName}:AuthenticatedIpPermitLimit",
            options.AuthenticatedIpPermitLimit,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{MutationRateLimitingOptions.SectionName}:AuthenticatedIpWindowMinutes",
            options.AuthenticatedIpWindowMinutes,
            MaxWindowMinutes);
        return OptionsValidation.Result(failures);
    }
}
