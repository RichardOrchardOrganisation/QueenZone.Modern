using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class PushNotificationOptionsValidator : IValidateOptions<PushNotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, PushNotificationOptions options)
    {
        var failures = new List<string>();
        if (options.Apns?.Environment is not ("sandbox" or "production"))
        {
            failures.Add(
                $"{PushNotificationOptions.SectionName}:Apns:Environment " +
                "must be 'sandbox' or 'production'.");
        }

        return OptionsValidation.Result(failures);
    }
}
