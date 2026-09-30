using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Attaches batched discussion fields to news list/detail shapes. Does not render UI.
/// </summary>
public sealed class NewsDiscussionComposer(
    INewsForumDiscussionLookup discussionLookup,
    ILogger<NewsDiscussionComposer> logger)
{
    public async Task<IReadOnlyList<NewsListItemDto>> ToListItemsAsync(
        IReadOnlyList<NewsItem> items,
        CancellationToken cancellationToken = default)
    {
        var counts = await GetReplyCountsAsync(items, cancellationToken);
        return items.Select(item => ContentApiMapper.ToNewsListItem(item, counts)).ToList();
    }

    public async Task<NewsDetailDto> ToDetailAsync(
        NewsItem item,
        CancellationToken cancellationToken = default)
    {
        var discussion = await GetDetailDiscussionAsync(item, cancellationToken);
        return ContentApiMapper.ToNewsDetail(item, discussion.ReplyCount, discussion.Preview);
    }

    public async Task<IReadOnlyList<NewsArchiveItem>> ToArchiveItemsAsync(
        IReadOnlyList<NewsItem> items,
        CancellationToken cancellationToken = default)
    {
        var counts = await GetReplyCountsAsync(items, cancellationToken);
        return items.Select(item => PublicContentMapper.ToNewsArchiveItem(item, counts)).ToList();
    }

    public async Task<NewsDetailItem> ToDetailItemAsync(
        NewsItem item,
        CancellationToken cancellationToken = default)
    {
        var discussion = await GetDetailDiscussionAsync(item, cancellationToken);
        return PublicContentMapper.ToNewsDetailItem(item, discussion.ReplyCount, discussion.Preview);
    }

    private async Task<IReadOnlyDictionary<int, int>> GetReplyCountsAsync(
        IReadOnlyList<NewsItem> items,
        CancellationToken cancellationToken)
    {
        var topicIds = items
            .Select(item => item.ForumTopicId)
            .OfType<int>()
            .Distinct()
            .ToList();
        if (topicIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        try
        {
            return await discussionLookup.GetReplyCountsAsync(topicIds, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "News list discussion reply-count lookup failed for topics {TopicIds}; omitting reply counts.",
                string.Join(",", topicIds));
            return new Dictionary<int, int>();
        }
    }

    private async Task<(int? ReplyCount, IReadOnlyList<NewsDiscussionPreviewDto>? Preview)> GetDetailDiscussionAsync(
        NewsItem item,
        CancellationToken cancellationToken)
    {
        if (item.ForumTopicId is not int topicId)
        {
            return (null, null);
        }

        try
        {
            var discussion = await discussionLookup.GetDiscussionAsync(
                topicId,
                NewsForumDiscussion.PreviewReplyCount,
                cancellationToken);
            if (!discussion.ThreadFound)
            {
                logger.LogWarning(
                    "News {NewsId} discussion topic {TopicId} has no visible forum thread; omitting discussion.",
                    item.Id,
                    topicId);
                return (null, null);
            }

            var preview = discussion.Preview
                .Select(previewItem => new NewsDiscussionPreviewDto(
                    previewItem.AuthorDisplayName,
                    previewItem.PostedAt,
                    previewItem.Excerpt))
                .ToList();
            return (discussion.ReplyCount, preview);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "News {NewsId} discussion lookup failed for topic {TopicId}; omitting discussion.",
                item.Id,
                topicId);
            return (null, null);
        }
    }
}
