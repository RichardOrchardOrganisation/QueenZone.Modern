using QueenZone.Data;

namespace QueenZone.Web;

public sealed class ForumPostReportService(
    IForumPostReportRepository repository,
    TimeProvider timeProvider)
{
    public Task<ForumReportablePost?> GetVisiblePostAsync(int postId, CancellationToken cancellationToken = default) =>
        repository.GetVisiblePostAsync(postId, cancellationToken);

    public async Task<ForumPostReportResult> ReportAsync(
        Guid reporterMemberId,
        int postId,
        string? category,
        string? details,
        CancellationToken cancellationToken = default)
    {
        if (!ForumPostReportCategories.IsKnown(category))
        {
            return new ForumPostReportResult(false, null, ForumPostReportText.CategoryRequired);
        }

        var normalizedDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (normalizedDetails is { Length: > ForumPostReportLimits.MaxDetailsLength })
        {
            return new ForumPostReportResult(false, null, ForumPostReportText.DetailsTooLong);
        }

        return await repository.CreateAsync(reporterMemberId, postId, category!, normalizedDetails,
            timeProvider.GetUtcNow(), cancellationToken);
    }
}
