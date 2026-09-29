using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class MobileAuthOptionsValidator : IValidateOptions<MobileAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, MobileAuthOptions options)
    {
        var failures = new List<string>();
        var prefix = MobileAuthOptions.SectionName;

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{prefix}:ClientId is required.");
        }

        OptionsValidation.RequireNonBlankEntries(
            failures,
            $"{prefix}:RedirectUris",
            options.RedirectUris,
            requireAtLeastOne: true);

        foreach (var uri in options.RedirectUris ?? [])
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                continue;
            }

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || parsed.Scheme is "javascript" or "data" or "file")
            {
                failures.Add($"{prefix}:RedirectUris entry '{uri}' must be an absolute URI with a non-script scheme.");
            }
        }

        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{prefix}:AccessTokenLifetimeMinutes",
            options.AccessTokenLifetimeMinutes,
            maximum: 120);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{prefix}:AuthorizationCodeLifetimeMinutes",
            options.AuthorizationCodeLifetimeMinutes,
            maximum: 15);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{prefix}:RefreshTokenLifetimeDays",
            options.RefreshTokenLifetimeDays,
            maximum: 90);
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{prefix}:RefreshTokenUnusedSuccessorRecoveryDailyLimit",
            options.RefreshTokenUnusedSuccessorRecoveryDailyLimit,
            maximum: 1000);

        // Do not fail production startup when SigningKey is blank. Mobile PKCE is unused until
        // a client exists; requiring MobileAuth__SigningKey at ValidateOnStart took
        // www.queenzone.org down after #774 because the App Service setting had not been added.
        // Token issuance still fails closed via MobileAuthTokenIssuer when the key is missing.
        if (OptionsValidation.LooksConfigured(options.SigningKey) && options.SigningKey.Trim().Length < 32)
        {
            failures.Add($"{prefix}:SigningKey must be at least 32 characters.");
        }

        return OptionsValidation.Result(failures);
    }
}
