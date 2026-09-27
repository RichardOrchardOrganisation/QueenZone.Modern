using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class AnalyticsOptionsValidator(IHostEnvironment environment) : IValidateOptions<AnalyticsOptions>
{
    public const int MaxTrafficCacheMinutes = 60 * 24 * 7;

    private static readonly Regex MeasurementIdPattern = new(
        "^G-[A-Z0-9]{6,14}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexDefaults.MatchTimeout);

    public ValidateOptionsResult Validate(string? name, AnalyticsOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{AnalyticsOptions.SectionName}:TrafficCacheMinutes",
            options.TrafficCacheMinutes,
            MaxTrafficCacheMinutes);

        var measurementId = options.MeasurementId?.Trim();
        if (string.IsNullOrWhiteSpace(measurementId))
        {
            if (QueenZoneEnvironments.IsProductionLike(environment))
            {
                failures.Add(
                    $"{AnalyticsOptions.SectionName}:MeasurementId is required in {environment.EnvironmentName}.");
            }
        }
        else if (!MeasurementIdPattern.IsMatch(measurementId))
        {
            failures.Add(
                $"{AnalyticsOptions.SectionName}:MeasurementId must look like a GA4 id (G- followed by 6-14 alphanumeric characters).");
        }

        var hasPropertyId = !string.IsNullOrWhiteSpace(options.GoogleAnalyticsPropertyId);
        var hasServiceAccount = !string.IsNullOrWhiteSpace(options.GoogleAnalyticsServiceAccountJson);
        if (hasPropertyId != hasServiceAccount)
        {
            failures.Add(
                $"{AnalyticsOptions.SectionName}:GoogleAnalyticsPropertyId and GoogleAnalyticsServiceAccountJson must both be set, or both left empty.");
        }

        return OptionsValidation.Result(failures);
    }
}
