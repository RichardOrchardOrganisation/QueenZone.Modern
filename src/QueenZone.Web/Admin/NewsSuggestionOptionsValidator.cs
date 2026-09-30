using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class NewsSuggestionOptionsValidator : IValidateOptions<NewsSuggestionOptions>
{
    public const int MaxSubmissionsPerMemberPerDay = 10_000;

    public ValidateOptionsResult Validate(string? name, NewsSuggestionOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{NewsSuggestionOptions.SectionName}:MaxSubmissionsPerMemberPerDay",
            options.MaxSubmissionsPerMemberPerDay,
            MaxSubmissionsPerMemberPerDay);
        return OptionsValidation.Result(failures);
    }
}
