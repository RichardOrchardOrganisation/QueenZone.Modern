using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Centralized mapping from repository DTOs to stable public view models.
/// Call once at the Web edge; page models and Razor views should consume view models only.
/// </summary>
public static class PublicContentMapper
{
    public static NewsArchiveItem ToNewsArchiveItem(
        NewsItem item,
        IReadOnlyDictionary<int, int>? replyCounts = null) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            item.PublishedAt,
            NewsRoutes.GetNewsDetailPath(item.Id, item.Title, item.Slug),
            item.SubmitterMemberId,
            item.SubmitterDisplayName,
            item.ImageBlobKey,
            item.ImageGalleryPicId,
            NewsArticleImage.ResolveImageUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            NewsArticleImage.ResolveThumbnailUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            item.ForumTopicId,
            ReplyCountFor(item.ForumTopicId, replyCounts));

    public static IReadOnlyList<NewsArchiveItem> ToNewsArchiveItems(IEnumerable<NewsItem> items) =>
        items.Select(item => ToNewsArchiveItem(item)).ToList();

    public static NewsDetailItem ToNewsDetailItem(
        NewsItem item,
        int? discussionReplyCount = null,
        IReadOnlyList<NewsDiscussionPreviewDto>? discussionPreview = null) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            item.Body,
            item.PublishedAt,
            item.SourceUrl,
            NewsRoutes.GetNewsDetailPath(item.Id, item.Title, item.Slug),
            item.SubmitterMemberId,
            item.SubmitterDisplayName,
            item.ImageBlobKey,
            item.ImageGalleryPicId,
            NewsArticleImage.ResolveImageUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            NewsArticleImage.ResolveThumbnailUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            item.ForumTopicId,
            item.ForumTopicId is null ? null : discussionReplyCount,
            item.ForumTopicId is null ? null : discussionPreview);

    private static int? ReplyCountFor(int? topicId, IReadOnlyDictionary<int, int>? replyCounts)
    {
        if (topicId is not int id)
        {
            return null;
        }

        return replyCounts is not null && replyCounts.TryGetValue(id, out var count) ? count : 0;
    }

    public static NewsDetailItem ToNewsDetailItem(AdminNewsArticle article) =>
        new(
            article.Id,
            article.Title,
            article.Excerpt,
            article.Body,
            article.PublishedAt,
            article.SourceUrl,
            NewsRoutes.GetNewsDetailPath(
                article.Id,
                article.Title,
                string.IsNullOrWhiteSpace(article.Slug) ? null : article.Slug),
            ImageBlobKey: article.ImageBlobKey,
            ImageGalleryPicId: article.ImageGalleryPicId,
            ImageUrl: NewsArticleImage.ResolveImageUrl(article.ImageBlobKey, article.ImageGalleryPicId),
            ThumbnailUrl: NewsArticleImage.ResolveThumbnailUrl(article.ImageBlobKey, article.ImageGalleryPicId),
            TopicId: article.ForumTopicId);

    public static ArticleArchiveItem ToArticleArchiveItem(ArticleItem item) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            item.PublishedAt,
            item.CategoryName,
            ArticlesRoutes.GetArticleDetailPath(item.Id, item.Title));

    public static IReadOnlyList<ArticleArchiveItem> ToArticleArchiveItems(IEnumerable<ArticleItem> items) =>
        items.Select(ToArticleArchiveItem).ToList();

    public static ArticleArchiveItem ToCommunityArticleArchiveItem(PublishedArticleSubmission item) =>
        new(
            0,
            item.Title,
            item.Excerpt ?? string.Empty,
            item.PublishedAt.UtcDateTime,
            item.Category,
            ArticlesRoutes.GetCommunityArticleDetailPath(item.Slug));

    public static IReadOnlyList<ArticleArchiveItem> DedupeArticleArchiveItemsByDetailPath(
        IReadOnlyList<ArticleArchiveItem> items)
    {
        if (items.Count <= 1)
        {
            return items;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<ArticleArchiveItem>(items.Count);
        foreach (var item in items)
        {
            if (seen.Add(item.DetailPath))
            {
                unique.Add(item);
            }
        }

        return unique;
    }

    public static ArticleDetailItem ToArticleDetailItem(ArticleItem item) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            item.Body,
            item.PublishedAt,
            item.Source,
            item.CategoryName,
            ArticlesRoutes.GetArticleDetailPath(item.Id, item.Title),
            NewsArticleImage.ResolveImageUrl(item.ImageBlobKey, null),
            item.AuthorName,
            item.Tags);

    public static ForumCategorySummary ToForumCategorySummary(ForumCategoryItem category)
    {
        var name = ForumTextCleaning.CleanForumText(category.Name);
        return new(
            category.Id,
            name,
            ForumTextCleaning.CleanForumTextOrNull(category.Description),
            category.PostCount,
            category.LastActivityAt,
            ForumTextCleaning.CleanForumTextOrNull(category.LatestThreadTitle),
            ForumRoutes.GetCategoryCanonicalPath(category.Id, name));
    }

    public static IReadOnlyList<ForumCategorySummary> ToForumCategorySummaries(
        IEnumerable<ForumCategoryItem> categories) =>
        categories.Select(ToForumCategorySummary).ToList();

    public static ForumThreadSummary ToForumThreadSummary(ForumTopicItem topic)
    {
        var title = ForumTextCleaning.CleanForumText(topic.Title);
        return new(
            topic.Id,
            title,
            topic.LastActivityAt,
            topic.AuthorUsername,
            topic.ReplyCount,
            topic.LastPostUsername,
            topic.IsSticky,
            ForumRoutes.GetTopicCanonicalPath(topic.Id, title));
    }

    public static IReadOnlyList<ForumThreadSummary> ToForumThreadSummaries(
        IEnumerable<ForumTopicItem> topics) =>
        topics.Select(ToForumThreadSummary).ToList();

    public static ForumRecentThreadSummary ToForumRecentThreadSummary(ForumRecentThreadItem item)
    {
        var title = ForumTextCleaning.CleanForumText(item.Title);
        var categoryName = ForumTextCleaning.CleanForumText(item.CategoryName);
        return new(
            item.TopicId,
            title,
            ForumRoutes.GetTopicCanonicalPath(item.TopicId, title),
            item.CategoryId,
            categoryName,
            ForumRoutes.GetCategoryCanonicalPath(item.CategoryId, categoryName),
            item.ReplyCount,
            item.LastActivityAt);
    }

    public static IReadOnlyList<ForumRecentThreadSummary> ToForumRecentThreadSummaries(
        IEnumerable<ForumRecentThreadItem> items) =>
        items.Select(ToForumRecentThreadSummary).ToList();

    public static ForumThreadHeader ToForumThreadHeader(ForumTopicHeader header)
    {
        var title = ForumTextCleaning.CleanForumText(header.Title);
        var forumName = ForumTextCleaning.CleanForumText(header.ForumName);
        return new(
            header.TopicId,
            title,
            header.ForumId,
            forumName,
            ForumRoutes.GetCategoryCanonicalPath(header.ForumId, forumName),
            ForumRoutes.GetTopicCanonicalPath(header.TopicId, title),
            header.HasPoll);
    }

    public static ForumPostViewModel ToForumPostViewModel(ForumPostItem post) =>
        new(
            post.Id,
            post.Body,
            post.PostedAt,
            post.AuthorUsername,
            post.Signature,
            post.AuthorMemberSince,
            ToForumAttachments(post.Attachments),
            post.AuthorMemberId,
            post.EditedAt,
            post.EditCount,
            AuthorLegacyUserId: post.AuthorLegacyUserId);

    public static ForumPostViewModel WithEditState(
        ForumPostViewModel post,
        Guid? currentMemberId,
        bool isAdmin,
        int editWindowMinutes,
        DateTimeOffset utcNow)
    {
        var postedAt = new DateTimeOffset(DateTime.SpecifyKind(post.PostedAt, DateTimeKind.Utc));
        var canEdit = ForumPostEditRules.CanEdit(
            post.AuthorMemberId,
            currentMemberId,
            isAdmin,
            postedAt,
            editWindowMinutes,
            utcNow);
        var showEdited = ForumPostEditRules.ShowEditedIndicator(post.EditCount, post.EditedAt, postedAt);
        var editedLabel = showEdited && post.EditedAt is not null
            ? ForumPostEditRules.FormatEditedLabel(post.EditCount, post.EditedAt.Value, utcNow)
            : null;
        var canMessage = PrivateMessageService.CanMessage(currentMemberId, post.AuthorMemberId);
        var isAuthor = currentMemberId is not null && post.AuthorMemberId == currentMemberId;

        return post with
        {
            CanEdit = canEdit,
            EditedLabel = editedLabel,
            CanMessage = canMessage,
            IsAuthor = isAuthor,
        };
    }

    public static IReadOnlyList<ForumPostViewModel> ToForumPostViewModels(
        IEnumerable<ForumPostItem> posts) =>
        posts.Select(ToForumPostViewModel).ToList();

    public static ForumIndexStats ToForumIndexStats(
        IReadOnlyList<ForumCategoryItem> categories,
        int threadCount) =>
        new(
            categories.Count,
            threadCount,
            categories.Sum(category => (long)category.PostCount));

    private static IReadOnlyList<ForumAttachmentViewModel> ToForumAttachments(
        IReadOnlyList<ForumPostAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return [];
        }

        return attachments
            .Select(attachment => new ForumAttachmentViewModel(
                attachment.FileName,
                attachment.Url,
                attachment.Extension,
                attachment.FormattedSize,
                attachment.IsImage,
                attachment.ThumbnailUrl))
            .ToList();
    }
}
