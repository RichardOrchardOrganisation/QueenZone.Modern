using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class PasswordSignInLockoutOptionsValidator : IValidateOptions<PasswordSignInLockoutOptions>
{
    public const int MaxFailuresLimit = 10_000;

    public const int MaxWindowMinutes = 1_440;

    public ValidateOptionsResult Validate(string? name, PasswordSignInLockoutOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PasswordSignInLockoutOptions.SectionName}:MaxFailures",
            options.MaxFailures,
            MaxFailuresLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PasswordSignInLockoutOptions.SectionName}:WindowMinutes",
            options.WindowMinutes,
            MaxWindowMinutes);
        return OptionsValidation.Result(failures);
    }
}
