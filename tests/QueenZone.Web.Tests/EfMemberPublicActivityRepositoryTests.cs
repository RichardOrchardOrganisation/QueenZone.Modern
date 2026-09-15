using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfMemberPublicActivityRepositoryTests : IAsyncDisposable
{
    private const int LegacyUserId = 7001;

    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly Guid memberId = Guid.NewGuid();

    public EfMemberPublicActivityRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        dbContext.Database.EnsureCreated();
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumThread (
                Id INTEGER PRIMARY KEY,
                LegacyTopicId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                StartedByLegacyUserId INTEGER NULL,
                StartedByDisplayName TEXT NOT NULL,
                StartedAt TEXT NULL,
                LastActivityAt TEXT NULL,
                ReplyCount INTEGER NOT NULL,
                IsSticky INTEGER NOT NULL,
                IsLegacyTopicStarter INTEGER NOT NULL,
                LegacyDiscography INTEGER NOT NULL,
                StartedByUserValidated INTEGER NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                StarterAttachment TEXT NULL,
                StarterFileSize TEXT NULL,
                StarterAttachCount INTEGER NOT NULL,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumPost (
                Id INTEGER PRIMARY KEY,
                LegacyPostId INTEGER NOT NULL,
                LegacyThreadTopicId INTEGER NOT NULL,
                ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                AuthorLegacyUserId INTEGER NULL,
                AuthorMemberId TEXT NULL,
                AuthorDisplayName TEXT NOT NULL,
                AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL,
                BodyHtml TEXT NOT NULL,
                SignatureHtml TEXT NULL,
                PostedAt TEXT NULL,
                EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0,
                LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL,
                Attachment TEXT NULL,
                FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread(Id)
            );
            """);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsOnlyPublicMemberContributions()
    {
        var now = DateTimeOffset.Parse("2026-08-03T08:00:00Z");
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = "public-activity@example.com",
            DisplayName = "Public Activity",
            CreatedAt = now.UtcDateTime,
        });
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            Id = 1,
            LegacyTopicId = 101,
            LegacyForumId = 1,
            CategoryId = 1,
            Title = "Public topic",
        });
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            Id = 1,
            LegacyPostId = 201,
            LegacyThreadTopicId = 101,
            ThreadId = 1,
            LegacyForumId = 1,
            AuthorMemberId = memberId,
            AuthorDisplayName = "Public Activity",
            BodyHtml = "Public forum post",
            PostedAt = now.UtcDateTime,
        });
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            Id = 2,
            LegacyPostId = 202,
            LegacyThreadTopicId = 101,
            ThreadId = 1,
            LegacyForumId = 1,
            AuthorMemberId = memberId,
            AuthorDisplayName = "Public Activity",
            BodyHtml = "Hidden spam post",
            PostedAt = now.UtcDateTime,
            IsHidden = true,
        });
        dbContext.ArticleSubmissions.AddRange(
            Article("Published article", ArticleSubmissionStatus.Published, now.AddHours(-1)),
            Article("Private draft", ArticleSubmissionStatus.Draft, null));
        dbContext.PhotoSubmissions.AddRange(
            Photo("Approved photo", PhotoSubmissionStatus.Approved, now.AddHours(-2)),
            Photo("Pending photo", PhotoSubmissionStatus.Pending, null));
        dbContext.NewsSuggestions.AddRange(
            News("Promoted news", NewsSuggestionStatus.Promoted, 301, now.AddHours(-3)),
            News("Pending news", NewsSuggestionStatus.Pending, null, null));
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository()
            .GetPageAsync(memberId, linkedLegacyUserId: null, 1, 20);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(
            [MemberPublicActivityType.ForumPost, MemberPublicActivityType.Article, MemberPublicActivityType.Photo, MemberPublicActivityType.News],
            result.Items.Select(item => item.Type).ToArray());
        Assert.DoesNotContain(result.Items, item => item.Title.Contains("Private", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, item => item.Title.Contains("Pending", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, item => item.Summary != null && item.Summary.Contains("Hidden spam", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetFeedPageAsync_EmptyIds_DoesNotReturnActivity()
    {
        SeedMember(memberId, "Solo");
        SeedForumPost(memberId, "Solo", 1, 101, 201, "Solo topic", DateTimeOffset.Parse("2026-08-03T08:00:00Z").UtcDateTime);
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository()
            .GetFeedPageAsync([], 1, 20);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetFeedPageAsync_FiltersAuthorIdIn_AndMixesNewestFirst()
    {
        var aliceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var carolId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SeedMember(aliceId, "Alice");
        SeedMember(bobId, "Bob");
        SeedMember(carolId, "Carol");
        SeedForumPost(aliceId, "Alice", 1, 101, 201, "Alice forum", DateTimeOffset.Parse("2026-08-03T12:00:00Z").UtcDateTime);
        SeedForumPost(bobId, "Bob", 2, 102, 202, "Bob forum", DateTimeOffset.Parse("2026-08-03T10:00:00Z").UtcDateTime);
        SeedForumPost(carolId, "Carol", 3, 103, 203, "Carol forum", DateTimeOffset.Parse("2026-08-03T14:00:00Z").UtcDateTime);
        dbContext.ArticleSubmissions.Add(ArticleFor(bobId, "Bob article", ArticleSubmissionStatus.Published, DateTimeOffset.Parse("2026-08-03T11:00:00Z")));
        dbContext.PhotoSubmissions.Add(PhotoFor(aliceId, "Alice photo", PhotoSubmissionStatus.Approved, DateTimeOffset.Parse("2026-08-03T09:00:00Z")));
        dbContext.NewsSuggestions.Add(NewsFor(carolId, "Carol news", NewsSuggestionStatus.Promoted, 301, DateTimeOffset.Parse("2026-08-03T13:00:00Z")));
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository()
            .GetFeedPageAsync([aliceId, bobId], 1, 20);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(
            ["Alice forum", "Bob article", "Bob forum", "Alice photo"],
            result.Items.Select(item => item.Title).ToArray());
        Assert.DoesNotContain(result.Items, item => item.Title.Contains("Carol", StringComparison.Ordinal));
        Assert.Equal(aliceId, result.Items[0].AuthorId);
        Assert.Equal("Alice", result.Items[0].AuthorDisplayName);
        Assert.Equal(bobId, result.Items[1].AuthorId);
    }

    [Fact]
    public async Task GetFeedPageAsync_PaginatesAcrossAuthors()
    {
        var aliceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedMember(aliceId, "Alice");
        SeedMember(bobId, "Bob");
        for (var index = 0; index < 21; index++)
        {
            var authorId = index % 2 == 0 ? aliceId : bobId;
            var authorName = index % 2 == 0 ? "Alice" : "Bob";
            SeedForumPost(
                authorId,
                authorName,
                index + 1,
                200 + index,
                300 + index,
                $"Topic {index}",
                DateTimeOffset.Parse("2026-08-03T08:00:00Z").UtcDateTime.AddMinutes(-index));
        }

        await dbContext.SaveChangesAsync();
        var repository = CreateRepository();

        var first = await repository.GetFeedPageAsync([aliceId, bobId], 1, 20);
        var second = await repository.GetFeedPageAsync([aliceId, bobId], 2, 20);

        Assert.Equal(21, first.TotalCount);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal("Topic 0", first.Items[0].Title);
        Assert.DoesNotContain(first.Items, item => item.Title == "Topic 20");
        Assert.Equal(["Topic 20"], second.Items.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task GetPageAsync_LinkedLegacyUser_MergesArchivePostsAttributedToMember()
    {
        SeedMember(memberId, "New Name");
        SeedForumPost(memberId, "New Name", 1, 101, 201, "Modern topic", DateTime.Parse("2026-08-03T08:00:00Z").ToUniversalTime());
        SeedLegacyForumPost(LegacyUserId, 2, 102, 202, "Archive topic", "Old archive body", DateTime.Parse("2009-05-01T08:00:00Z").ToUniversalTime());
        SeedLegacyForumPost(LegacyUserId, 3, 103, 203, "Hidden archive", "Hidden body", DateTime.Parse("2009-06-01T08:00:00Z").ToUniversalTime(), isHidden: true);
        SeedLegacyForumPost(LegacyUserId + 1, 4, 104, 204, "Someone else", "Other body", DateTime.Parse("2010-01-01T08:00:00Z").ToUniversalTime());
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository().GetPageAsync(memberId, LegacyUserId, 1, 20);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["Modern topic", "Archive topic"], result.Items.Select(item => item.Title).ToArray());
        var archive = result.Items[1];
        Assert.Equal(MemberPublicActivityType.ForumPost, archive.Type);
        Assert.Equal("Old archive body", archive.Summary);
        Assert.Equal(202, archive.ContentId);
        Assert.Equal(102, archive.ParentId);
        Assert.Equal(memberId, archive.AuthorId);
        Assert.Equal("New Name", archive.AuthorDisplayName);
    }

    [Fact]
    public async Task GetPageAsync_LinkedLegacyUser_PaginatesAndLoadsBodiesForPageRows()
    {
        SeedMember(memberId, "Prolific");
        for (var index = 0; index < 25; index++)
        {
            SeedLegacyForumPost(
                LegacyUserId,
                index + 1,
                500 + index,
                600 + index,
                $"Archive {index}",
                $"Body {index}",
                DateTime.Parse("2009-05-01T08:00:00Z").ToUniversalTime().AddDays(-index));
        }

        await dbContext.SaveChangesAsync();
        var repository = CreateRepository();

        var second = await repository.GetPageAsync(memberId, LegacyUserId, 2, 20);
        var feed = await repository.GetFeedPageAsync([memberId], 1, 20);

        Assert.Equal(25, second.TotalCount);
        Assert.Equal(
            Enumerable.Range(20, 5).Select(index => $"Archive {index}").ToArray(),
            second.Items.Select(item => item.Title).ToArray());
        Assert.Equal(
            Enumerable.Range(20, 5).Select(index => $"Body {index}").ToArray(),
            second.Items.Select(item => item.Summary).ToArray());
        Assert.Equal(0, feed.TotalCount);
    }

    [Fact]
    public async Task GetPageAsync_LinkedLegacyUserWithoutPosts_ReturnsMemberActivityOnly()
    {
        SeedMember(memberId, "Quiet");
        SeedForumPost(memberId, "Quiet", 1, 101, 201, "Modern topic", DateTime.Parse("2026-08-03T08:00:00Z").ToUniversalTime());
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository().GetPageAsync(memberId, LegacyUserId, 1, 20);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(["Modern topic"], result.Items.Select(item => item.Title).ToArray());
    }

    private EfMemberPublicActivityRepository CreateRepository() =>
        new(
            dbContext,
            new EfForumArchiveAuthorRepository(
                dbContext,
                legacyUserId => $"""
                    SELECT
                        AuthorLegacyUserId AS LegacyUserId,
                        AuthorDisplayName AS DisplayName,
                        AuthorJoinedAt AS MemberSince,
                        (
                            SELECT COUNT(*)
                            FROM ModernForumPost counted
                            WHERE counted.AuthorLegacyUserId = {legacyUserId}
                              AND counted.IsHidden = 0
                        ) AS PostCount
                    FROM ModernForumPost
                    WHERE AuthorLegacyUserId = {legacyUserId}
                      AND IsHidden = 0
                    ORDER BY PostedAt DESC, Id DESC
                    LIMIT 1
                    """));

    private void SeedLegacyForumPost(
        int legacyUserId,
        int id,
        int topicId,
        int postId,
        string title,
        string body,
        DateTime postedAt,
        bool isHidden = false)
    {
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            Id = id,
            LegacyTopicId = topicId,
            LegacyForumId = 1,
            CategoryId = 1,
            Title = title,
        });
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            Id = id,
            LegacyPostId = postId,
            LegacyThreadTopicId = topicId,
            ThreadId = id,
            LegacyForumId = 1,
            AuthorLegacyUserId = legacyUserId,
            AuthorDisplayName = "old_username",
            BodyHtml = body,
            PostedAt = postedAt,
            IsHidden = isHidden,
        });
    }

    private void SeedMember(Guid id, string displayName)
    {
        var email = $"{displayName.ToLowerInvariant()}-{id:N}@example.com";
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private void SeedForumPost(
        Guid authorId,
        string authorName,
        int id,
        int topicId,
        int postId,
        string title,
        DateTime postedAt)
    {
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            Id = id,
            LegacyTopicId = topicId,
            LegacyForumId = 1,
            CategoryId = 1,
            Title = title,
        });
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            Id = id,
            LegacyPostId = postId,
            LegacyThreadTopicId = topicId,
            ThreadId = id,
            LegacyForumId = 1,
            AuthorMemberId = authorId,
            AuthorDisplayName = authorName,
            BodyHtml = title,
            PostedAt = postedAt,
        });
    }

    private static ArticleSubmissionEntity ArticleFor(
        Guid authorId,
        string title,
        string status,
        DateTimeOffset? publishedAt)
    {
        return new ArticleSubmissionEntity
        {
            Id = Guid.NewGuid(),
            AuthorMemberId = authorId,
            Title = title,
            Slug = NewsSlug.Slugify(title),
            Body = "Body",
            Status = status,
            PublishedAt = publishedAt,
        };
    }

    private static PhotoSubmissionEntity PhotoFor(
        Guid authorId,
        string title,
        string status,
        DateTimeOffset? reviewedAt)
    {
        return new PhotoSubmissionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = authorId,
            Title = title,
            BlobPath = $"original/{Guid.NewGuid():N}.jpg",
            WebOptimizedBlobPath = $"web/{Guid.NewGuid():N}.webp",
            ThumbnailBlobPath = $"thumb/{Guid.NewGuid():N}.webp",
            OriginalFileName = "photo.jpg",
            MimeType = "image/jpeg",
            Status = status,
            SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            ReviewedAt = reviewedAt,
        };
    }

    private static NewsSuggestionEntity NewsFor(
        Guid authorId,
        string title,
        string status,
        int? newsId,
        DateTimeOffset? reviewedAt)
    {
        return new NewsSuggestionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = authorId,
            Url = $"https://example.com/{Guid.NewGuid():N}",
            UrlHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Title = title,
            Status = status,
            SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            ReviewedAt = reviewedAt,
            PromotedNewsId = newsId,
        };
    }

    private ArticleSubmissionEntity Article(string title, string status, DateTimeOffset? publishedAt) => new()
    {
        Id = Guid.NewGuid(),
        AuthorMemberId = memberId,
        Title = title,
        Slug = NewsSlug.Slugify(title),
        Body = "Body",
        Status = status,
        PublishedAt = publishedAt,
    };

    private PhotoSubmissionEntity Photo(string title, string status, DateTimeOffset? reviewedAt) => new()
    {
        Id = Guid.NewGuid(),
        SubmitterMemberId = memberId,
        Title = title,
        BlobPath = $"original/{Guid.NewGuid():N}.jpg",
        WebOptimizedBlobPath = $"web/{Guid.NewGuid():N}.webp",
        ThumbnailBlobPath = $"thumb/{Guid.NewGuid():N}.webp",
        OriginalFileName = "photo.jpg",
        MimeType = "image/jpeg",
        Status = status,
        SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
        ReviewedAt = reviewedAt,
    };

    private NewsSuggestionEntity News(string title, string status, int? newsId, DateTimeOffset? reviewedAt) => new()
    {
        Id = Guid.NewGuid(),
        SubmitterMemberId = memberId,
        Url = $"https://example.com/{Guid.NewGuid():N}",
        UrlHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        Title = title,
        Status = status,
        SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
        ReviewedAt = reviewedAt,
        PromotedNewsId = newsId,
    };

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
