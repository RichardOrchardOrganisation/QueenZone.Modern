using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs <see cref="ModernForumRepository"/> against production stored procedures
/// on a scratch SQL Server database (#1892 / #1672). Full-text sources are
/// substituted for LIKE; the mirror probe covers real FREETEXTTABLE.
/// </summary>
public sealed class ModernForumRepositorySqlServerTests : IAsyncLifetime
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day3 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day4 = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AuthorMember = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ModernForumSqlServerDatabase database = new();
    private QueenZoneDbContext dbContext = null!;
    private ModernForumRepository repository = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        dbContext = database.CreateContext();
        repository = new ModernForumRepository(dbContext);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public void Search_procedure_preserves_fulltext_sources_and_visibility_filters()
    {
        var sql = ModernForumSqlServerSchema.SearchProcedureSql();

        Assert.Equal(2, sql.Split(ModernForumSqlServerSchema.TitleFreeTextSource).Length - 1);
        Assert.Equal(2, sql.Split(ModernForumSqlServerSchema.BodyFreeTextSource).Length - 1);
        Assert.Contains("@TotalRecords INT OUTPUT", sql, StringComparison.Ordinal);
        Assert.Contains("t.IsHidden = 0", sql, StringComparison.Ordinal);
        Assert.Contains("p.IsHidden = 0", sql, StringComparison.Ordinal);
        Assert.Contains("c.IsSynthetic = 0", sql, StringComparison.Ordinal);
        Assert.Contains("t.IsLegacyTopicStarter = 1", sql, StringComparison.Ordinal);
        Assert.Contains("t.StartedByUserValidated = 1", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY r.TotalRank DESC, t.LastActivityAt DESC", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Categories_filter_synthetic_order_and_map_optional_fields()
    {
        await database.SeedCategoryAsync(1, 30, "  Zulu  ", sortOrder: 20, description: "  Live shows  ", lastActivityAt: Day2, legacyPostCount: 4);
        await database.SeedCategoryAsync(2, 10, "Alpha", sortOrder: 20, description: "   ", lastActivityAt: Day1, legacyPostCount: 1);
        await database.SeedCategoryAsync(3, 20, "Empty", sortOrder: 5, description: null, legacyPostCount: 0);
        await database.SeedCategoryAsync(4, 99, "Synthetic", sortOrder: 1, isSynthetic: true);
        await database.SeedThreadAsync(100, 1000, 30, 1, "Older visible", "Brian", lastActivityAt: Day1);
        await database.SeedThreadAsync(101, 1001, 30, 1, "  Latest visible  ", "Roger", lastActivityAt: Day3);
        await database.SeedThreadAsync(102, 1002, 30, 1, "Hidden newer", "John", lastActivityAt: Day4, isHidden: true);

        var categories = await repository.GetCategoriesAsync();
        Assert.Equal([20, 10, 30], categories.Select(item => item.Id));
        Assert.Equal("Empty", categories[0].Name);
        Assert.Null(categories[0].LatestThreadTitle);
        Assert.Equal("Alpha", categories[1].Name);
        Assert.Null(categories[1].Description);
        Assert.Equal("Zulu", categories[2].Name);
        Assert.Equal("Live shows", categories[2].Description);
        Assert.Equal(4, categories[2].PostCount);
        Assert.Equal(Day2, categories[2].LastActivityAt);
        Assert.DoesNotContain(categories, item => item.Id == 99);

        var lookup = await repository.GetCategoryByIdAsync(30);
        Assert.NotNull(lookup);
        Assert.Equal("Latest visible", lookup.LatestThreadTitle);
        Assert.Equal("Zulu", lookup.Name);

        var empty = await repository.GetCategoryByIdAsync(20);
        Assert.NotNull(empty);
        Assert.Null(empty.LatestThreadTitle);

        Assert.Null(await repository.GetCategoryByIdAsync(99));
        Assert.Null(await repository.GetCategoryByIdAsync(404));
    }

    [Fact]
    public async Task Category_topics_filter_order_page_and_report_totals()
    {
        await SeedCategoryTopicFixtureAsync();
        await database.SeedCategoryStatsAsync(1, 10, totalThreads: 8, validatedDisplayThreads: 4);

        var page1 = await repository.GetCategoryTopicsPageAsync(10, 1, 2);
        Assert.Equal(4, page1.TotalCount);
        Assert.Equal([1000, 1001], page1.Topics.Select(topic => topic.Id));
        Assert.True(page1.Topics[0].IsSticky);
        Assert.False(page1.Topics[1].IsSticky);
        Assert.Equal("Pinned", page1.Topics[0].Title);
        Assert.Null(page1.Topics[0].LastPostUsername);

        var page2 = await repository.GetCategoryTopicsPageAsync(10, 2, 2);
        Assert.Equal(4, page2.TotalCount);
        Assert.Equal([1002, 1003], page2.Topics.Select(topic => topic.Id));
        Assert.Equal(Day3, page2.Topics[0].LastActivityAt);
        Assert.Equal(DateTime.MinValue, page2.Topics[1].LastActivityAt);

        var beyond = await repository.GetCategoryTopicsPageAsync(10, 5, 2);
        Assert.Equal(4, beyond.TotalCount);
        Assert.Empty(beyond.Topics);

        await database.ExecuteAsync("DELETE FROM dbo.ModernForumCategoryReadStats WHERE CategoryId = 1;");
        var fallback = await repository.GetCategoryTopicsPageAsync(10, 1, 10);
        Assert.Equal(4, fallback.TotalCount);
        Assert.Equal([1000, 1001, 1002, 1003], fallback.Topics.Select(topic => topic.Id));
    }

    [Fact]
    public async Task Topic_posts_map_outputs_filter_hidden_and_merge_attachments()
    {
        await database.SeedCategoryAsync(1, 10, "  General  ", sortOrder: 1);
        await database.SeedThreadAsync(200, 2000, 10, 1, "  Topic title  ", "Freddie", replyCount: 3, lastActivityAt: Day2);
        await database.SeedThreadAsync(201, 2001, 10, 1, "No poll", "Brian", lastActivityAt: Day1);
        await database.SeedPostAsync(
            300, 3000, 2000, 200, 10, "  Freddie  ", "Hello 🎸 café",
            postedAt: Day1, authorLegacyUserId: 7, authorMemberId: AuthorMember,
            authorPostCount: 12, authorJoinedAt: Day1, signatureHtml: "  Deacy  ",
            editedAt: Day2, editCount: 3, attachment: "solo.jpg", fileSize: "2048");
        await database.SeedPostAsync(
            301, 3001, 2000, 200, 10, "Hidden", "secret", postedAt: Day2, isHidden: true);
        await database.SeedPostAsync(
            302, 3002, 2000, 200, 10, "John", "second body",
            postedAt: null, authorPostCount: null, signatureHtml: "   ");
        await database.SeedPostAsync(303, 3003, 2000, 200, 10, "Roger", "third", postedAt: Day3);
        await database.SeedPollAsync(200, 2000);
        await database.SeedModernAttachmentAsync(300, 3000, "modern.png", new DateTimeOffset(Day3, TimeSpan.Zero));
        await database.SeedThreadStatsAsync(200, 2000, 3);

        var page1 = await repository.GetTopicPostsPageAsync(2000, 1, 2);
        Assert.NotNull(page1);
        Assert.Equal(2000, page1.Header.TopicId);
        Assert.Equal("Topic title", page1.Header.Title);
        Assert.Equal(10, page1.Header.ForumId);
        Assert.Equal("General", page1.Header.ForumName);
        Assert.True(page1.Header.HasPoll);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([3000, 3002], page1.Posts.Select(post => post.Id));

        var first = page1.Posts[0];
        Assert.Equal("Hello 🎸 café", first.Body);
        Assert.Equal("Freddie", first.AuthorUsername);
        Assert.Equal("Deacy", first.Signature);
        Assert.Equal(12, first.AuthorPostCount);
        Assert.Equal(Day1, first.AuthorMemberSince);
        Assert.Equal(AuthorMember, first.AuthorMemberId);
        Assert.Equal(7, first.AuthorLegacyUserId);
        Assert.Equal(3, first.EditCount);
        Assert.Equal(new DateTimeOffset(Day2, TimeSpan.Zero), first.EditedAt);
        Assert.Equal(2, first.Attachments!.Count);
        Assert.Equal("solo.jpg", first.Attachments[0].FileName);
        Assert.Equal("/forum/attachment/legacy/3000", first.Attachments[0].Url);
        Assert.Equal("modern.png", first.Attachments[1].FileName);

        var second = page1.Posts[1];
        Assert.Equal(DateTime.MinValue, second.PostedAt);
        Assert.Equal("John", second.AuthorUsername);
        Assert.Null(second.Signature);
        Assert.Equal(0, second.AuthorPostCount);
        Assert.Null(second.AuthorMemberSince);

        var page2 = await repository.GetTopicPostsPageAsync(2000, 2, 2);
        Assert.NotNull(page2);
        Assert.Equal([3003], page2.Posts.Select(post => post.Id));
        Assert.DoesNotContain(page2.Posts, post => post.Id == 3001);

        var noPoll = await repository.GetTopicPostsPageAsync(2001, 1, 10);
        Assert.NotNull(noPoll);
        Assert.False(noPoll.Header.HasPoll);
        Assert.Equal(0, noPoll.TotalCount);
        Assert.Empty(noPoll.Posts);
    }

    [Fact]
    public async Task Find_legacy_post_resolves_topics_and_visible_reply_positions()
    {
        await database.SeedCategoryAsync(1, 10, "General", sortOrder: 1);
        await database.SeedThreadAsync(200, 2000, 10, 1, "  Topic title  ", "Freddie", lastActivityAt: Day2);
        await database.SeedThreadAsync(201, 3010, 10, 1, "Collision topic", "Brian", lastActivityAt: Day1);
        await database.SeedThreadAsync(202, 2002, 10, 1, "Hidden topic", "John", isHidden: true);
        await database.SeedPostAsync(300, 3000, 2000, 200, 10, "Freddie", "starter", postedAt: Day1);
        await database.SeedPostAsync(301, 3001, 2000, 200, 10, "Hidden", "secret", postedAt: Day2, isHidden: true);
        await database.SeedPostAsync(302, 3002, 2000, 200, 10, "John", "second", postedAt: Day2);
        await database.SeedPostAsync(303, 3003, 2000, 200, 10, "Roger", "third", postedAt: Day3);
        await database.SeedPostAsync(304, 3010, 2000, 200, 10, "Roger", "same ID as a topic", postedAt: Day4);
        await database.SeedPostAsync(305, 3005, 2002, 202, 10, "John", "in hidden topic", postedAt: Day1);

        Assert.Equal(new ForumLegacyPostLocation(2000, "Topic title", 2000, 0), await repository.FindLegacyPostAsync(2000));
        Assert.Equal(new ForumLegacyPostLocation(2000, "Topic title", 3000, 0), await repository.FindLegacyPostAsync(3000));
        Assert.Equal(new ForumLegacyPostLocation(2000, "Topic title", 3002, 1), await repository.FindLegacyPostAsync(3002));
        Assert.Equal(new ForumLegacyPostLocation(2000, "Topic title", 3003, 2), await repository.FindLegacyPostAsync(3003));

        // Topic IDs win when a post shares the ID.
        Assert.Equal(new ForumLegacyPostLocation(3010, "Collision topic", 3010, 0), await repository.FindLegacyPostAsync(3010));

        Assert.Null(await repository.FindLegacyPostAsync(3001));
        Assert.Null(await repository.FindLegacyPostAsync(2002));
        Assert.Null(await repository.FindLegacyPostAsync(3005));
        Assert.Null(await repository.FindLegacyPostAsync(404));
    }

    [Fact]
    public async Task Topic_posts_return_null_for_missing_hidden_or_blank_title()
    {
        await database.SeedCategoryAsync(1, 10, "General", sortOrder: 1);
        await database.SeedCategoryAsync(2, 0, "Zero", sortOrder: 2);
        await database.SeedThreadAsync(200, 2000, 10, 1, "Visible", "Freddie", lastActivityAt: Day1);
        await database.SeedThreadAsync(201, 2001, 10, 1, "Hidden", "Freddie", isHidden: true);
        await database.SeedThreadAsync(202, 2002, 10, 1, "   ", "Freddie");
        await database.SeedThreadAsync(203, 2003, 0, 2, "Zero forum", "Freddie");
        await database.SeedPostAsync(300, 3000, 2000, 200, 10, "Freddie", "body", postedAt: Day1);
        await database.SeedThreadStatsAsync(200, 2000, 1);

        Assert.Null(await repository.GetTopicPostsPageAsync(404, 1, 10));
        Assert.Null(await repository.GetTopicPostsPageAsync(2001, 1, 10));
        Assert.Null(await repository.GetTopicPostsPageAsync(2002, 1, 10));
        Assert.Null(await repository.GetTopicPostsPageAsync(2003, 1, 10));

        var emptyPage = await repository.GetTopicPostsPageAsync(2000, 9, 10);
        Assert.NotNull(emptyPage);
        Assert.Equal("Visible", emptyPage.Header.Title);
        Assert.Equal(1, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Posts);
    }

    [Fact]
    public async Task Archive_counts_use_cache_or_fallback_and_sum_category_posts()
    {
        Assert.Equal(0, await repository.GetTotalThreadCountAsync());
        var empty = await repository.GetArchiveStatsAsync();
        Assert.Equal((0, 0, 0L), (empty.ForumCount, empty.ThreadCount, empty.PostCount));

        await database.SeedCategoryAsync(1, 10, "A", sortOrder: 1, legacyPostCount: 5);
        await database.SeedCategoryAsync(2, 20, "B", sortOrder: 2, legacyPostCount: 7);
        await database.SeedCategoryAsync(3, 99, "Synth", sortOrder: 3, isSynthetic: true);
        await database.SeedThreadAsync(100, 1000, 10, 1, "One", "Freddie", lastActivityAt: Day1);
        await database.SeedThreadAsync(101, 1001, 10, 1, "Hidden", "Freddie", isHidden: true);
        await database.SeedThreadAsync(102, 1002, 20, 2, "Two", "Brian", isLegacyTopicStarter: false);
        await database.SeedArchiveStatsAsync(totalThreads: 4, sitemapTopicCount: 2);

        Assert.Equal(4, await repository.GetTotalThreadCountAsync());
        var cached = await repository.GetArchiveStatsAsync();
        Assert.Equal(2, cached.ForumCount);
        Assert.Equal(4, cached.ThreadCount);
        Assert.Equal(12, cached.PostCount);

        await database.ExecuteAsync("DELETE FROM dbo.ModernForumArchiveReadStats;");
        Assert.Equal(2, await repository.GetTotalThreadCountAsync());
        var fallback = await repository.GetArchiveStatsAsync();
        Assert.Equal(2, fallback.ForumCount);
        Assert.Equal(2, fallback.ThreadCount);
        Assert.Equal(12, fallback.PostCount);
    }

    [Fact]
    public async Task Sitemap_filters_hidden_blank_titles_and_pages_by_legacy_id()
    {
        await database.SeedCategoryAsync(1, 10, "A", sortOrder: 1);
        await database.SeedThreadAsync(100, 1002, 10, 1, "  Middle  ", "A", lastActivityAt: null);
        await database.SeedThreadAsync(101, 1001, 10, 1, "First", "B", lastActivityAt: Day1);
        await database.SeedThreadAsync(102, 1003, 10, 1, "   ", "C", lastActivityAt: Day2);
        await database.SeedThreadAsync(103, 1004, 10, 1, "Hidden", "D", lastActivityAt: Day3, isHidden: true);
        await database.SeedThreadAsync(104, 1005, 10, 1, "Last", "E", lastActivityAt: Day4);
        await database.SeedArchiveStatsAsync(totalThreads: 9, sitemapTopicCount: 3);

        Assert.Equal(3, await repository.GetTopicSitemapCountAsync());
        var page = await repository.GetTopicSitemapPageAsync(0, 2);
        Assert.Equal([1001, 1002], page.Select(item => item.TopicId));
        Assert.Equal("First", page[0].Title);
        Assert.Equal("Middle", page[1].Title);
        Assert.Null(page[1].LastActivityAt);

        var next = await repository.GetTopicSitemapPageAsync(2, 2);
        Assert.Equal([1005], next.Select(item => item.TopicId));

        var clamped = await repository.GetTopicSitemapPageAsync(-10, 1);
        Assert.Equal(1001, Assert.Single(clamped).TopicId);

        Assert.Empty(await repository.GetTopicSitemapPageAsync(50, 10));

        await database.ExecuteAsync("DELETE FROM dbo.ModernForumArchiveReadStats;");
        Assert.Equal(3, await repository.GetTopicSitemapCountAsync());
    }

    [Fact]
    public async Task Search_combines_title_and_body_matches_filters_ranks_and_pages()
    {
        await database.SeedCategoryAsync(1, 10, "  Main  ", sortOrder: 1);
        await database.SeedCategoryAsync(2, 99, "Synth", sortOrder: 2, isSynthetic: true);
        await database.SeedThreadAsync(100, 1000, 10, 1, "  Title hit  ", "  Brian  ", lastActivityAt: Day4);
        await database.SeedPostAsync(300, 3000, 1000, 100, 10, "Brian", "plain body");
        await database.SeedThreadAsync(101, 1001, 10, 1, "Other", "Roger", lastActivityAt: Day3);
        await database.SeedPostAsync(301, 3001, 1001, 101, 10, "Roger", "body hit here");
        await database.SeedThreadAsync(102, 1002, 10, 1, "Title and body hit", "John", lastActivityAt: Day1);
        await database.SeedPostAsync(302, 3002, 1002, 102, 10, "John", "another hit in the body");
        await database.SeedPostAsync(303, 3003, 1002, 102, 10, "John", "second hit in same thread");
        await database.SeedThreadAsync(103, 1003, 10, 1, "Hidden hit", "Freddie", lastActivityAt: Day4, isHidden: true);
        await database.SeedPostAsync(304, 3004, 1003, 103, 10, "Freddie", "hit");
        await database.SeedThreadAsync(104, 1004, 10, 1, "Hidden post thread", "Freddie", lastActivityAt: Day4);
        await database.SeedPostAsync(305, 3005, 1004, 104, 10, "Freddie", "hit", isHidden: true);
        await database.SeedThreadAsync(105, 1005, 10, 1, "Unvalidated hit", "X", lastActivityAt: Day4, startedByUserValidated: false);
        await database.SeedPostAsync(306, 3006, 1005, 105, 10, "X", "hit");
        await database.SeedThreadAsync(106, 1006, 10, 1, "Null validation hit", "Y", lastActivityAt: Day4, startedByUserValidated: null);
        await database.SeedPostAsync(307, 3007, 1006, 106, 10, "Y", "hit");
        await database.SeedThreadAsync(107, 1007, 10, 1, "Reply hit", "Z", lastActivityAt: Day4, isLegacyTopicStarter: false);
        await database.SeedPostAsync(308, 3008, 1007, 107, 10, "Z", "hit");
        await database.SeedThreadAsync(108, 1008, 99, 2, "Synthetic hit", "S", lastActivityAt: Day4);
        await database.SeedPostAsync(309, 3009, 1008, 108, 99, "S", "hit");

        var first = await repository.SearchForumAsync("hit", 1, 2);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal([1002, 1000], first.Results.Select(item => item.TopicId));
        Assert.Equal("Title and body hit", first.Results[0].Title);
        Assert.Equal("Title hit", first.Results[1].Title);
        Assert.Equal("Main", first.Results[1].CategoryName);
        Assert.Equal("Brian", first.Results[1].StartedByDisplayName);
        Assert.Equal(10, first.Results[1].CategoryId);

        var second = await repository.SearchForumAsync("hit", 2, 2);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal([1001], second.Results.Select(item => item.TopicId));

        var beyond = await repository.SearchForumAsync("hit", 5, 2);
        Assert.Equal(3, beyond.TotalCount);
        Assert.Empty(beyond.Results);

        var pageZero = await repository.SearchForumAsync("hit", 0, 2);
        Assert.Equal([1002, 1000], pageZero.Results.Select(item => item.TopicId));

        var none = await repository.SearchForumAsync("zeppelin", 1, 10);
        Assert.Equal(0, none.TotalCount);
        Assert.Empty(none.Results);
    }

    private async Task SeedCategoryTopicFixtureAsync()
    {
        await database.SeedCategoryAsync(1, 10, "General", sortOrder: 1);
        await database.SeedCategoryAsync(2, 99, "Synth", sortOrder: 2, isSynthetic: true);
        await database.SeedThreadAsync(100, 1000, 10, 1, "Pinned", "Freddie", isSticky: true, lastActivityAt: Day1, replyCount: 2);
        await database.SeedThreadAsync(101, 1001, 10, 1, "Tie B", "Brian", lastActivityAt: Day3);
        await database.SeedThreadAsync(102, 1002, 10, 1, "Tie C", "Roger", lastActivityAt: Day3);
        await database.SeedThreadAsync(103, 1003, 10, 1, "  Null date  ", "John", lastActivityAt: null);
        await database.SeedThreadAsync(104, 1004, 10, 1, "Hidden", "X", isHidden: true, lastActivityAt: Day4);
        await database.SeedThreadAsync(105, 1005, 10, 1, "Unvalidated", "Y", startedByUserValidated: false, lastActivityAt: Day4);
        await database.SeedThreadAsync(106, 1006, 10, 1, "Null validation", "Z", startedByUserValidated: null, lastActivityAt: Day4);
        await database.SeedThreadAsync(107, 1007, 10, 1, "Reply only", "W", isLegacyTopicStarter: false, lastActivityAt: Day4);
        await database.SeedThreadAsync(108, 1008, 99, 2, "Synthetic topic", "S", lastActivityAt: Day4);
    }
}
