using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed class NewsForumTopicService(
    NewsForumCategoryResolver categories,
    ForumPostWriteService forumPostWrite,
    IMemberAccountRepository memberAccounts,
    IAdminNewsRepository adminNews,
    IOptions<NewsForumOptions> newsForumOptions,
    TimeProvider timeProvider,
    ILogger<NewsForumTopicService> logger) : INewsForumTopicService
{
    private readonly NewsForumOptions options = newsForumOptions.Value;

    public async Task EnsureTopicOnFirstPublishAsync(
        AdminNewsArticle article,
        CancellationToken cancellationToken = default)
    {
        if (article.ForumTopicId is not null)
        {
            return;
        }

        var latest = await adminNews.GetByIdAsync(article.Id, cancellationToken);
        if (latest?.ForumTopicId is not null)
        {
            return;
        }

        var categoryId = await categories.ResolveAsync(cancellationToken);

        var author = await EnsureSystemMemberAsync(cancellationToken);
        var title = ClampTitle(article.Title, article.Id);
        var body = BuildOpeningPost(article);
        var outcome = await forumPostWrite.CreateTopicAsync(new NewForumTopicRequest(author.Id, options.SystemMemberDisplayName, categoryId, title, body, Attachments: null, Poll: null) { TrustedSystemAuthor = true }, cancellationToken);
        if (!outcome.Succeeded)
        {
            logger.LogWarning(
                "News forum topic create failed after news publish {NewsId} for category {Category}: {Status}",
                article.Id,
                NewsForumDiscussion.CategoryName,
                outcome.Status);
            return;
        }

        await adminNews.TrySetForumTopicIdAsync(article.Id, outcome.TopicId, cancellationToken);
    }

    internal static string BuildOpeningPost(AdminNewsArticle article)
    {
        var excerpt = NewsForumDiscussion.TruncatePlain(
            article.Excerpt,
            NewsForumDiscussion.OpeningExcerptMaxLength);
        var linkParagraph = BuildPublicArticleLinkParagraph(article);
        return string.IsNullOrWhiteSpace(excerpt)
            ? linkParagraph
            : excerpt + "\n\n" + linkParagraph;
    }

    internal static string BuildPublicArticleLinkParagraph(AdminNewsArticle article)
    {
        var path = NewsRoutes.GetNewsDetailPath(
            article.Id,
            article.Title,
            string.IsNullOrWhiteSpace(article.Slug) ? null : article.Slug);
        return NewsForumDiscussion.PublicArticleOrigin + path;
    }

    internal static string ClampTitle(string title, int articleId)
    {
        var trimmed = title.Trim();
        if (trimmed.Length > ForumPostWriteService.SubjectMaxLength)
        {
            trimmed = trimmed[..ForumPostWriteService.SubjectMaxLength].Trim();
        }

        return trimmed.Length < ForumPostWriteService.SubjectMinLength
            ? $"News article {articleId}"
            : trimmed;
    }

    private async Task<MemberAccount> EnsureSystemMemberAsync(CancellationToken cancellationToken)
    {
        var existing = await memberAccounts.FindByEmailAsync(
            options.SystemMemberEmail,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        return await memberAccounts.CreateAsync(
            new MemberAccount
            {
                Id = Guid.NewGuid(),
                Email = options.SystemMemberEmail,
                DisplayName = options.SystemMemberDisplayName,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            },
            cancellationToken);
    }
}
