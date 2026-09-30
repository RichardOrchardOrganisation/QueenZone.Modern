using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class PublicQueryCacheOptionsValidator : IValidateOptions<PublicQueryCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicQueryCacheOptions options)
    {
        if (options.NewsCacheDuration <= TimeSpan.Zero
            || options.ArticleCountCacheDuration <= TimeSpan.Zero
            || options.ForumStatsCacheDuration <= TimeSpan.Zero
            || options.OnThisDayCacheDuration <= TimeSpan.Zero
            || options.PhotoCacheDuration <= TimeSpan.Zero
            || options.FanPerformanceCacheDuration <= TimeSpan.Zero
            || options.LiveActivityCacheDuration <= TimeSpan.Zero
            || options.CatalogCacheDuration <= TimeSpan.Zero
            || options.FreddieSampleCacheDuration <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{PublicQueryCacheOptions.SectionName} durations must be greater than zero.");
        }

        return ValidateOptionsResult.Success;
    }
}
