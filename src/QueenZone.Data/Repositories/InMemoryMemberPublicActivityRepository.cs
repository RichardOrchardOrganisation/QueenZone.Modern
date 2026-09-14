namespace QueenZone.Data;

public sealed class InMemoryMemberPublicActivityRepository(
    InMemoryForumWriteRepository forumWriteRepository,
    IArticleSubmissionRepository articleSubmissionRepository,
    IPhotoSubmissionRepository photoSubmissionRepository,
    INewsSuggestionRepository newsSuggestionRepository,
    IForumArchiveAuthorRepository archiveAuthorRepository) : IMemberPublicActivityRepository
{
    public Task<MemberPublicActivityPage> GetPageAsync(
        Guid memberId,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetPageCoreAsync([memberId], linkedLegacyUserId, page, pageSize, cancellationToken);

    public Task<MemberPublicActivityPage> GetFeedPageAsync(
        IReadOnlyCollection<Guid> memberIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetPageCoreAsync(memberIds, linkedLegacyUserId: null, page, pageSize, cancellationToken);

    private async Task<MemberPublicActivityPage> GetPageCoreAsync(
        IReadOnlyCollection<Guid> memberIds,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var authorIds = memberIds.ToHashSet();
        if (authorIds.Count == 0)
        {
            return new MemberPublicActivityPage([], 0, page, pageSize);
        }

        var forumItems = forumWriteRepository.GetCreatedThreads()
            .SelectMany(thread => forumWriteRepository.GetPostsForTopic(thread.TopicId)
                .Where(post => authorIds.Contains(post.MemberId))
                .Select(post => new MemberPublicActivityItem(
                    MemberPublicActivityType.ForumPost,
                    thread.Subject,
                    post.Body,
                    post.CreatedAt,
                    post.PostId,
                    thread.TopicId,
                    NewsSlug.Slugify(thread.Subject),
                    AuthorId: post.MemberId,
                    AuthorDisplayName: post.DisplayName)));

        var articleItems = (await articleSubmissionRepository.GetPublishedAsync(cancellationToken))
            .Where(article => article.AuthorMemberId is Guid authorId && authorIds.Contains(authorId))
            .Select(article => new MemberPublicActivityItem(
                MemberPublicActivityType.Article,
                article.Title,
                article.Excerpt,
                article.PublishedAt,
                Slug: article.Slug,
                AuthorId: article.AuthorMemberId,
                AuthorDisplayName: article.AuthorDisplayName));

        var photoItems = new List<MemberPublicActivityItem>();
        var newsItems = new List<MemberPublicActivityItem>();
        foreach (var authorId in authorIds)
        {
            var photoPage = await photoSubmissionRepository.GetBySubmitterAsync(
                authorId,
                pageSize: int.MaxValue,
                cancellationToken: cancellationToken);
            photoItems.AddRange(photoPage.Items
                .Where(photo => photo.Status == PhotoSubmissionStatus.Approved)
                .Select(photo => new MemberPublicActivityItem(
                    MemberPublicActivityType.Photo,
                    photo.Title,
                    photo.Description,
                    photo.ReviewedAt ?? photo.SubmittedAt,
                    Category: photo.ApprovedCategory,
                    AuthorId: photo.SubmitterMemberId,
                    AuthorDisplayName: photo.SubmitterDisplayName)));

            var newsPage = await newsSuggestionRepository.GetBySubmitterAsync(
                authorId,
                pageSize: int.MaxValue,
                cancellationToken: cancellationToken);
            newsItems.AddRange(newsPage.Items
                .Where(news => news.Status == NewsSuggestionStatus.Promoted && news.PromotedNewsId is not null)
                .Select(news => new MemberPublicActivityItem(
                    MemberPublicActivityType.News,
                    string.IsNullOrWhiteSpace(news.Title) ? "News contribution" : news.Title,
                    news.Notes,
                    news.ReviewedAt ?? news.SubmittedAt,
                    ContentId: news.PromotedNewsId,
                    Slug: NewsSlug.Slugify(news.Title ?? "news"),
                    AuthorId: news.SubmitterMemberId,
                    AuthorDisplayName: news.SubmitterDisplayName)));
        }

        var legacyItems = linkedLegacyUserId is int legacyUserId
            ? await LoadLinkedLegacyItemsAsync(legacyUserId, memberIds.First(), cancellationToken)
            : [];

        var all = forumItems
            .Concat(articleItems)
            .Concat(photoItems)
            .Concat(newsItems)
            .Concat(legacyItems)
            .OrderByDescending(item => item.PublishedAt)
            .ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new MemberPublicActivityPage(items, all.Count, page, pageSize);
    }

    private async Task<List<MemberPublicActivityItem>> LoadLinkedLegacyItemsAsync(
        int legacyUserId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var summary = await archiveAuthorRepository.GetSummaryAsync(legacyUserId, cancellationToken);
        var items = new List<MemberPublicActivityItem>();
        if (summary is null)
        {
            return items;
        }

        const int batchSize = 100;
        for (var page = 1; items.Count < summary.PostCount; page++)
        {
            var batch = await archiveAuthorRepository.GetPostsPageAsync(
                legacyUserId,
                page,
                batchSize,
                summary.PostCount,
                cancellationToken);
            if (batch.Items.Count == 0)
            {
                break;
            }

            items.AddRange(batch.Items.Select(item => item with { AuthorId = memberId }));
        }

        return items;
    }
}
