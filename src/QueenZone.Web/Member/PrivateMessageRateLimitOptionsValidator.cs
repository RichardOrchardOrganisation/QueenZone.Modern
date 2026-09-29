using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class PrivateMessageRateLimitOptionsValidator : IValidateOptions<PrivateMessageRateLimitOptions>
{
    public const int MaxPermitLimit = 10_000;

    public const int MaxWindowMinutes = 1_440;

    public const int MaxNewAccountAgeDays = 365;

    public ValidateOptionsResult Validate(string? name, PrivateMessageRateLimitOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:WindowMinutes",
            options.WindowMinutes,
            MaxWindowMinutes);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:MaxMessagesPerWindow",
            options.MaxMessagesPerWindow,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:MaxNewRecipientsPerWindow",
            options.MaxNewRecipientsPerWindow,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:MaxDuplicateMessagesPerWindow",
            options.MaxDuplicateMessagesPerWindow,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:NewAccountAgeDays",
            options.NewAccountAgeDays,
            MaxNewAccountAgeDays);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:NewAccountMaxMessagesPerWindow",
            options.NewAccountMaxMessagesPerWindow,
            MaxPermitLimit);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{PrivateMessageRateLimitOptions.SectionName}:NewAccountMaxNewRecipientsPerWindow",
            options.NewAccountMaxNewRecipientsPerWindow,
            MaxPermitLimit);
        return OptionsValidation.Result(failures);
    }
}
