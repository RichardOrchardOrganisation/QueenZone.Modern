using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class NewsForumTopicPublishTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public NewsForumTopicPublishTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task FirstPublish_CreatesExactlyOneNewsTopic_AndStoresForumTopicId()
    {
        using var scope = factory.Services.CreateScope();
        var (write, admin, forumWrite, members) = Resolve(scope);
        var draft = await CreateDraftAsync(admin, "First publish discussion article");

        await write.PublishAsync(draft, "editor@test.local");

        var published = await admin.GetByIdAsync(draft.Id);
        Assert.NotNull(published);
        Assert.True(published!.IsPublished);
        Assert.NotNull(published.ForumTopicId);

        var categoryId = await forumWrite.EnsureCategoryAsync(
            NewsForumDiscussion.CategorySlug,
            NewsForumDiscussion.CategoryName);
        var category = (await scope.ServiceProvider.GetRequiredService<IForumRepository>()
            .GetCategoryByIdAsync(categoryId))!;
        Assert.Equal(NewsForumDiscussion.CategoryName, category.Name);
        Assert.False(NewsForumDiscussion.IsTheMusic(category.Name));

        var thread = forumWrite.GetCreatedThreads().Single(item => item.TopicId == published.ForumTopicId);
        Assert.Equal(categoryId, thread.CategoryId);
        Assert.Equal(draft.Title, thread.Subject);

        var opening = Assert.Single(forumWrite.GetPostsForTopic(published.ForumTopicId.Value));
        Assert.Contains("https://www.queenzone.org/news/", opening.Body, StringComparison.Ordinal);
        Assert.Contains(NewsRoutes.GetNewsDetailPath(draft.Id, draft.Title, draft.Slug), opening.Body, StringComparison.Ordinal);
        Assert.Contains("<p>https://www.queenzone.org", opening.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(draft.Body, opening.Body, StringComparison.Ordinal);

        var author = await members.FindByEmailAsync(NewsForumDiscussion.SystemMemberEmail);
        Assert.NotNull(author);
        Assert.Equal(NewsForumDiscussion.SystemMemberDisplayName, author!.DisplayName);
        Assert.Equal(author.Id, opening.MemberId);
        Assert.False(author.IsSuspended);
    }

    [Fact]
    public async Task SecondPublish_DoesNotCreateASecondTopic()
    {
        using var scope = factory.Services.CreateScope();
        var (write, admin, forumWrite, _) = Resolve(scope);
        var draft = await CreateDraftAsync(admin, "Republish no-op discussion article");
        await write.PublishAsync(draft, "editor@test.local");
        var first = await admin.GetByIdAsync(draft.Id);
        Assert.NotNull(first!.ForumTopicId);
        var topicCountAfterFirst = forumWrite.GetCreatedThreads().Count(thread => thread.TopicId == first.ForumTopicId);

        await write.PublishAsync(first, "editor@test.local");

        var second = await admin.GetByIdAsync(draft.Id);
        Assert.Equal(first.ForumTopicId, second!.ForumTopicId);
        Assert.Equal(
            topicCountAfterFirst,
            forumWrite.GetCreatedThreads().Count(thread => thread.TopicId == first.ForumTopicId));
    }

    [Fact]
    public async Task Republish_RetriesMissingForumTopic()
    {
        using var scope = factory.Services.CreateScope();
        var (write, admin, forumWrite, _) = Resolve(scope);
        var draft = await CreateDraftAsync(admin, "Repair missing discussion article");
        await admin.PublishAsync(draft.Id, "editor@test.local", draft.UpdatedAt);
        var published = await admin.GetByIdAsync(draft.Id);
        Assert.NotNull(published);
        Assert.Null(published!.ForumTopicId);

        await write.PublishAsync(published, "editor@test.local");

        var repaired = await admin.GetByIdAsync(draft.Id);
        Assert.NotNull(repaired!.ForumTopicId);
        Assert.Single(forumWrite.GetCreatedThreads(), thread => thread.TopicId == repaired.ForumTopicId);
    }

    [Fact]
    public async Task EnsureTopic_WhenForumTopicIdAlreadySet_IsNoOp()
    {
        using var scope = factory.Services.CreateScope();
        var (_, admin, forumWrite, _) = Resolve(scope);
        var service = scope.ServiceProvider.GetRequiredService<INewsForumTopicService>();
        var draft = await CreateDraftAsync(admin, "Already linked discussion article");
        await scope.ServiceProvider.GetRequiredService<AdminNewsWriteService>()
            .PublishAsync(draft, "editor@test.local");
        var published = await admin.GetByIdAsync(draft.Id);
        Assert.NotNull(published!.ForumTopicId);
        var topicCount = forumWrite.GetCreatedThreads().Count;

        await service.EnsureTopicOnFirstPublishAsync(published);

        var again = await admin.GetByIdAsync(draft.Id);
        Assert.Equal(published.ForumTopicId, again!.ForumTopicId);
        Assert.Equal(topicCount, forumWrite.GetCreatedThreads().Count);
    }

    [Fact]
    public async Task Unpublish_DoesNotClearForumTopicId()
    {
        using var scope = factory.Services.CreateScope();
        var (write, admin, _, _) = Resolve(scope);
        var draft = await CreateDraftAsync(admin, "Unpublish keeps discussion article");
        await write.PublishAsync(draft, "editor@test.local");
        var published = await admin.GetByIdAsync(draft.Id);
        Assert.NotNull(published!.ForumTopicId);

        await admin.UnpublishAsync(published.Id, "editor@test.local");

        var unpublished = await admin.GetByIdAsync(draft.Id);
        Assert.False(unpublished!.IsPublished);
        Assert.Equal(published.ForumTopicId, unpublished.ForumTopicId);
    }

    [Fact]
    public async Task FailedTopicCreate_DoesNotFailPublish_AndLogsWarning()
    {
        var logger = new ListLogger<AdminNewsWriteService>();
        var store = new SharedNewsStore();
        var admin = new InMemoryAdminNewsRepository(store);
        var write = new AdminNewsWriteService(
            admin,
            new ThrowingNewsDispatcher(),
            new ThrowingNewsForumTopicService(),
            logger);
        var id = await admin.CreateDraftAsync(
            new AdminNewsDraft("Fail-open topic article", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            "editor@test.local");
        var article = (await admin.GetByIdAsync(id))!;

        await write.PublishAsync(article, "editor@test.local");

        var published = await admin.GetByIdAsync(id);
        Assert.True(published!.IsPublished);
        Assert.Null(published.ForumTopicId);
        Assert.Contains(
            logger.Warnings,
            message => message.Contains("News forum topic create failed", StringComparison.Ordinal)
                && message.Contains(id.ToString(), StringComparison.Ordinal)
                && message.Contains(NewsForumDiscussion.CategoryName, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildOpeningPost_UsesWwwOriginAndExcerptCap()
    {
        var excerpt = new string('x', 500);
        var article = new AdminNewsArticle(
            42,
            "A long enough title",
            "a-long-enough-title",
            excerpt,
            "Full article body that must not appear",
            DateTime.UtcNow.Date,
            null,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "editor@test.local");

        var body = NewsForumTopicService.BuildOpeningPost(article);
        var linkParagraph = NewsForumTopicService.BuildPublicArticleLinkParagraph(article);
        var wrapped = UgcHtml.WrapPlainText(body);

        Assert.Equal(NewsForumDiscussion.OpeningExcerptMaxLength, body.IndexOf("\n\n", StringComparison.Ordinal));
        Assert.Equal("https://www.queenzone.org/news/42/a-long-enough-title", linkParagraph);
        Assert.EndsWith("\n\n" + linkParagraph, body, StringComparison.Ordinal);
        Assert.Contains($"<p>{linkParagraph}</p>", wrapped, StringComparison.Ordinal);
        Assert.DoesNotContain("Full article body", body, StringComparison.Ordinal);
        Assert.Equal(linkParagraph, NewsForumTopicService.BuildOpeningPost(article with { Excerpt = "   " }));
        Assert.Equal("News article 7", NewsForumTopicService.ClampTitle("Hi", 7));
        Assert.Equal(ForumPostWriteService.SubjectMaxLength, NewsForumTopicService.ClampTitle(new string('t', 240), 8).Length);
    }

    private static async Task<AdminNewsArticle> CreateDraftAsync(IAdminNewsRepository admin, string title)
    {
        var id = await admin.CreateDraftAsync(
            new AdminNewsDraft(title, null, "Short excerpt for the opening post.", "Full article body", DateTime.UtcNow.Date, null),
            "editor@test.local");
        var draft = await admin.GetByIdAsync(id);
        Assert.NotNull(draft);
        Assert.False(draft!.IsPublished);
        return draft;
    }

    private static (
        AdminNewsWriteService Write,
        IAdminNewsRepository Admin,
        InMemoryForumWriteRepository ForumWrite,
        IMemberAccountRepository Members) Resolve(IServiceScope scope)
    {
        return (
            scope.ServiceProvider.GetRequiredService<AdminNewsWriteService>(),
            scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>(),
            scope.ServiceProvider.GetRequiredService<InMemoryForumWriteRepository>(),
            scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>());
    }

    private sealed class ThrowingNewsForumTopicService : INewsForumTopicService
    {
        public Task EnsureTopicOnFirstPublishAsync(
            AdminNewsArticle article,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("topic create exploded");
    }

    private sealed class ThrowingNewsDispatcher : INotificationDispatcher
    {
        public Task NotifyForumReplyAsync(
            int topicId,
            int postId,
            Guid authorMemberId,
            string topicTitle,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task NotifyPrivateMessageAsync(
            Guid conversationId,
            Guid recipientMemberId,
            Guid senderMemberId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task NotifyNewsPublishedAsync(
            int articleId,
            string title,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("push exploded");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
