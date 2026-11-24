using Microsoft.Extensions.Options;

namespace QueenZone.Application;

public sealed class GalleryOrphanSweepOptionsValidator : IValidateOptions<GalleryOrphanSweepOptions>
{
    public const int GracePeriodMinutesCeiling = 7 * 24 * 60;

    public ValidateOptionsResult Validate(string? name, GalleryOrphanSweepOptions options)
    {
        var failures = new List<string>();
        OptionsValidation.RequirePositiveAtMost(
            failures,
            $"{GalleryOrphanSweepOptions.SectionName}:GracePeriodMinutes",
            options.GracePeriodMinutes,
            GracePeriodMinutesCeiling);
        return OptionsValidation.Result(failures);
    }
}
