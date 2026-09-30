using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class NewsDiscussionComposerTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public NewsDiscussionComposerTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task List_BatchesReplyCounts_WithoutBodies_AndOmitsDiscussionWhenTopicMissing()
    {
        var lookup = new RecordingDiscussionLookup();
        lookup.ReplyCounts[11] = 4;
        lookup.ReplyCounts[12] = 0;
        var composer = CreateComposer(lookup);
        var items = new List<NewsItem>
        {
            Item(1, topicId: 11),
            Item(2, topicId: 12),
            Item(3, topicId: null),
        };

        var list = await composer.ToListItemsAsync(items);

        Assert.Equal([11, 12], Assert.Single(lookup.ReplyCountCalls));
        Assert.Equal(11, list[0].TopicId);
        Assert.Equal(4, list[0].ReplyCount);
        Assert.Equal(12, list[1].TopicId);
        Assert.Equal(0, list[1].ReplyCount);
        Assert.Null(list[2].TopicId);
        Assert.Null(list[2].ReplyCount);
        Assert.Empty(lookup.DiscussionCalls);
    }

    [Fact]
    public async Task Detail_ReturnsLastTwoReplies_NotOpeningPost()
    {
        var lookup = new RecordingDiscussionLookup
        {
            Discussion = NewsForumDiscussionLookupResult.Found(
                3,
                [
                    new NewsDiscussionPreview("Alice", new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), "first reply"),
                    new NewsDiscussionPreview("Bob", new DateTime(2026, 8, 1, 11, 0, 0, DateTimeKind.Utc), "second reply"),
                ]),
        };
        var composer = CreateComposer(lookup);

        var detail = await composer.ToDetailAsync(Item(9, topicId: 77));

        Assert.Equal(77, detail.TopicId);
        Assert.Equal(3, detail.DiscussionReplyCount);
        Assert.Equal(2, detail.DiscussionPreview!.Count);
        Assert.Equal("Alice", detail.DiscussionPreview[0].AuthorDisplayName);
        Assert.Equal("Bob", detail.DiscussionPreview[1].AuthorDisplayName);
        Assert.DoesNotContain(
            detail.DiscussionPreview,
            preview => preview.Excerpt.Contains("opening", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(77, Assert.Single(lookup.DiscussionCalls));
        Assert.Equal(NewsForumDiscussion.PreviewReplyCount, lookup.LastPreviewCount);

        var website = await composer.ToDetailItemAsync(Item(9, topicId: 77));
        Assert.Equal(77, website.TopicId);
        Assert.Equal(3, website.DiscussionReplyCount);
        Assert.Equal(2, website.DiscussionPreview!.Count);
    }

    [Fact]
    public async Task Detail_ReturnsSingleReply_WhenOnlyOneReplyExists()
    {
        var lookup = new RecordingDiscussionLookup
        {
            Discussion = NewsForumDiscussionLookupResult.Found(
                1,
                [
                    new NewsDiscussionPreview("Only", new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), "sole reply"),
                ]),
        };
        var composer = CreateComposer(lookup);

        var detail = await composer.ToDetailAsync(Item(8, topicId: 55));
        var website = await composer.ToDetailItemAsync(Item(8, topicId: 55));

        Assert.Equal(55, detail.TopicId);
        Assert.Equal(1, detail.DiscussionReplyCount);
        var preview = Assert.Single(detail.DiscussionPreview!);
        Assert.Equal("Only", preview.AuthorDisplayName);
        Assert.Equal("sole reply", preview.Excerpt);
        Assert.Equal(1, website.DiscussionReplyCount);
        Assert.Single(website.DiscussionPreview!);
        Assert.Equal(NewsForumDiscussion.PreviewReplyCount, lookup.LastPreviewCount);
    }

    [Fact]
    public async Task Detail_WithoutTopicId_HasNoDiscussionBlock()
    {
        var lookup = new RecordingDiscussionLookup();
        var composer = CreateComposer(lookup);

        var detail = await composer.ToDetailAsync(Item(5, topicId: null));
        var website = await composer.ToDetailItemAsync(Item(5, topicId: null));

        Assert.Null(detail.TopicId);
        Assert.Null(detail.DiscussionReplyCount);
        Assert.Null(detail.DiscussionPreview);
        Assert.Null(website.TopicId);
        Assert.Null(website.DiscussionReplyCount);
        Assert.Null(website.DiscussionPreview);
        Assert.Empty(lookup.DiscussionCalls);
        Assert.Empty(lookup.ReplyCountCalls);
    }

    [Fact]
    public async Task Detail_WhenVisibleThreadHasZeroReplies_ExposesZeroReplyCount()
    {
        var lookup = new RecordingDiscussionLookup
        {
            Discussion = NewsForumDiscussionLookupResult.Found(0, []),
        };
        var logger = new ListLogger<NewsDiscussionComposer>();
        var composer = CreateComposer(lookup, logger);

        var detail = await composer.ToDetailAsync(Item(6, topicId: 88));
        var website = await composer.ToDetailItemAsync(Item(6, topicId: 88));

        Assert.Equal(88, detail.TopicId);
        Assert.Equal(0, detail.DiscussionReplyCount);
        Assert.Empty(detail.DiscussionPreview!);
        Assert.Equal(88, website.TopicId);
        Assert.Equal(0, website.DiscussionReplyCount);
        Assert.Empty(website.DiscussionPreview!);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task Detail_WhenThreadIsMissing_OmitsDiscussionAndLogsWarning()
    {
        var lookup = new RecordingDiscussionLookup
        {
            Discussion = NewsForumDiscussionLookupResult.Missing,
        };
        var logger = new ListLogger<NewsDiscussionComposer>();
        var composer = CreateComposer(lookup, logger);

        var detail = await composer.ToDetailAsync(Item(7037, topicId: 1175833020));
        var website = await composer.ToDetailItemAsync(Item(7037, topicId: 1175833020));

        Assert.Null(detail.TopicId);
        Assert.Null(detail.DiscussionReplyCount);
        Assert.Null(detail.DiscussionPreview);
        Assert.Null(website.TopicId);
        Assert.Null(website.DiscussionReplyCount);
        Assert.Null(website.DiscussionPreview);
        Assert.Equal(2, logger.Warnings.Count);
        Assert.All(
            logger.Warnings,
            warning =>
            {
                Assert.Contains("7037", warning, StringComparison.Ordinal);
                Assert.Contains("1175833020", warning, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Detail_WhenLookupThrows_OmitsDiscussionAndLogsWarning()
    {
        var lookup = new RecordingDiscussionLookup
        {
            DiscussionException = new TimeoutException("command timeout"),
        };
        var logger = new ListLogger<NewsDiscussionComposer>();
        var composer = CreateComposer(lookup, logger);

        var detail = await composer.ToDetailAsync(Item(7037, topicId: 1175833020));
        var website = await composer.ToDetailItemAsync(Item(7037, topicId: 1175833020));

        Assert.Null(detail.TopicId);
        Assert.Null(detail.DiscussionReplyCount);
        Assert.Null(detail.DiscussionPreview);
        Assert.Null(website.TopicId);
        Assert.Null(website.DiscussionReplyCount);
        Assert.Null(website.DiscussionPreview);
        Assert.Equal(2, logger.Warnings.Count);
        Assert.All(
            logger.Warnings,
            warning =>
            {
                Assert.Contains("7037", warning, StringComparison.Ordinal);
                Assert.Contains("1175833020", warning, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task List_WhenReplyCountsThrow_FailOpenAndLogsWarning()
    {
        var lookup = new RecordingDiscussionLookup
        {
            ReplyCountsException = new InvalidOperationException("reply counts failed"),
        };
        lookup.ReplyCounts[11] = 4;
        var logger = new ListLogger<NewsDiscussionComposer>();
        var composer = CreateComposer(lookup, logger);
        var items = new List<NewsItem>
        {
            Item(1, topicId: 11),
            Item(2, topicId: 12),
        };

        var list = await composer.ToListItemsAsync(items);
        var archive = await composer.ToArchiveItemsAsync(items);

        Assert.Equal(11, list[0].TopicId);
        Assert.Equal(0, list[0].ReplyCount);
        Assert.Equal(12, list[1].TopicId);
        Assert.Equal(0, list[1].ReplyCount);
        Assert.Equal(0, archive[0].ReplyCount);
        Assert.Equal(2, logger.Warnings.Count);
        Assert.All(
            logger.Warnings,
            warning => Assert.Contains("11,12", warning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApiDetail_AfterFirstPublish_ExposesTopicAndLastTwoReplies()
    {
        using var scope = factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
        var write = scope.ServiceProvider.GetRequiredService<AdminNewsWriteService>();
        var forumWrite = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var draft = await admin.CreateDraftAsync(
            new AdminNewsDraft("API discussion article", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            "editor@test.local");
        var article = (await admin.GetByIdAsync(draft))!;
        await write.PublishAsync(article, "editor@test.local");
        var published = (await admin.GetByIdAsync(draft))!;
        Assert.NotNull(published.ForumTopicId);

        await forumWrite.CreateReplyAsync(
            Guid.NewGuid(), "First", published.ForumTopicId!.Value, "First reply body", attachments: null);
        await forumWrite.CreateReplyAsync(
            Guid.NewGuid(), "Second", published.ForumTopicId.Value, "Second reply body", attachments: null);
        await forumWrite.CreateReplyAsync(
            Guid.NewGuid(), "Third", published.ForumTopicId.Value, "Third reply body", attachments: null);

        using var client = factory.CreateAnonymousClient();
        using var detailResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/news/{draft}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<NewsDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(published.ForumTopicId, detail!.TopicId);
        Assert.Equal(3, detail.DiscussionReplyCount);
        Assert.Equal(2, detail.DiscussionPreview!.Count);
        Assert.Equal("Second", detail.DiscussionPreview[0].AuthorDisplayName);
        Assert.Equal("Third", detail.DiscussionPreview[1].AuthorDisplayName);

        using var listResponse = await client.GetAsync($"{ContentApiEndpoints.RootPath}/news?page=1&pageSize=20");
        var list = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<NewsListItemDto>>();
        var card = Assert.Single(list!.Items, item => item.Id == draft);
        Assert.Equal(published.ForumTopicId, card.TopicId);
        Assert.Equal(3, card.ReplyCount);
    }

    private static NewsDiscussionComposer CreateComposer(
        INewsForumDiscussionLookup lookup,
        ILogger<NewsDiscussionComposer>? logger = null) =>
        new(lookup, logger ?? NullLogger<NewsDiscussionComposer>.Instance);

    private static NewsItem Item(int id, int? topicId) =>
        new(
            id,
            $"Title {id}",
            "Excerpt",
            "Body",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true,
            ForumTopicId: topicId);

    private sealed class RecordingDiscussionLookup : INewsForumDiscussionLookup
    {
        public Dictionary<int, int> ReplyCounts { get; } = [];

        public List<IReadOnlyList<int>> ReplyCountCalls { get; } = [];

        public List<int> DiscussionCalls { get; } = [];

        public int LastPreviewCount { get; private set; }

        public NewsForumDiscussionLookupResult Discussion { get; set; } =
            NewsForumDiscussionLookupResult.Found(0, []);

        public Exception? DiscussionException { get; set; }

        public Exception? ReplyCountsException { get; set; }

        public Task<IReadOnlyDictionary<int, int>> GetReplyCountsAsync(
            IReadOnlyList<int> topicIds,
            CancellationToken cancellationToken = default)
        {
            if (ReplyCountsException is not null)
            {
                throw ReplyCountsException;
            }

            ReplyCountCalls.Add(topicIds.ToList());
            return Task.FromResult<IReadOnlyDictionary<int, int>>(
                topicIds.ToDictionary(id => id, id => ReplyCounts.GetValueOrDefault(id)));
        }

        public Task<NewsForumDiscussionLookupResult> GetDiscussionAsync(
            int topicId,
            int previewCount,
            CancellationToken cancellationToken = default)
        {
            if (DiscussionException is not null)
            {
                throw DiscussionException;
            }

            DiscussionCalls.Add(topicId);
            LastPreviewCount = previewCount;
            return Task.FromResult(Discussion);
        }
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
