using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfArticleRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly Guid authorId;

    public EfArticleRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();

        authorId = Guid.NewGuid();
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = authorId,
            Email = "author@test.local",
            NormalizedEmail = "AUTHOR@TEST.LOCAL",
            DisplayName = "Test Author",
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    private EfArticleRepository Repo() => new(dbContext);

    private void AddPublished(string slug, string title, DateTimeOffset publishedAt,
        string? tags = null, string? excerpt = null, string? coverImageBlobPath = null)
    {
        dbContext.ArticleSubmissions.Add(new ArticleSubmissionEntity
        {
            Id = Guid.NewGuid(),
            AuthorMemberId = authorId,
            Title = title,
            Slug = slug,
            Excerpt = excerpt,
            Body = "Body text for " + title,
            WordCount = EfArticleSubmissionRepository.EstimateWordCount("Body text for " + title),
            Tags = tags,
            CoverImageBlobPath = coverImageBlobPath,
            Status = ArticleSubmissionStatus.Published,
            PublishedAt = publishedAt,
        });
        dbContext.SaveChanges();
    }

    private void AddDraft(string slug, string title)
    {
        dbContext.ArticleSubmissions.Add(new ArticleSubmissionEntity
        {
            Id = Guid.NewGuid(),
            AuthorMemberId = authorId,
            Title = title,
            Slug = slug,
            Body = "Draft body text",
            Status = ArticleSubmissionStatus.Draft,
        });
        dbContext.SaveChanges();
    }

    // --- GetCountAsync ---

    [Fact]
    public async Task GetCountAsync_ReturnsZeroWhenEmpty()
    {
        Assert.Equal(0, await Repo().GetCountAsync());
    }

    [Fact]
    public async Task GetCountAsync_ReturnsOnlyPublishedArticles()
    {
        AddPublished("pub-1", "Published 1", DateTimeOffset.UtcNow.AddDays(-1));
        AddDraft("draft-1", "Draft 1");

        Assert.Equal(1, await Repo().GetCountAsync());
    }

    [Fact]
    public async Task GetCountAsync_WithTag_ReturnsMatchingCount()
    {
        AddPublished("art-a", "Article A", DateTimeOffset.UtcNow.AddDays(-2), tags: "queen,freddie");
        AddPublished("art-b", "Article B", DateTimeOffset.UtcNow.AddDays(-1), tags: "roger");
        AddPublished("art-c", "Article C", DateTimeOffset.UtcNow, tags: "queen,brian");

        Assert.Equal(2, await Repo().GetCountAsync("queen"));
        Assert.Equal(1, await Repo().GetCountAsync("roger"));
        Assert.Equal(0, await Repo().GetCountAsync("john"));
    }

    // --- GetPageAsync ---

    [Fact]
    public async Task GetPageAsync_ReturnsPublishedArticlesOrderedByDateDesc()
    {
        AddPublished("older", "Older Article", DateTimeOffset.UtcNow.AddDays(-2));
        AddPublished("newer", "Newer Article", DateTimeOffset.UtcNow.AddDays(-1));
        AddDraft("draft", "Draft Article");

        var result = await Repo().GetPageAsync(1, 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("newer", result[0].Slug);
        Assert.Equal("older", result[1].Slug);
        Assert.Equal("Test Author", result[0].AuthorDisplayName);
        Assert.Equal(string.Empty, result[0].Body);
        Assert.True(result[0].WordCount > 0);
    }

    [Fact]
    public async Task GetPageAsync_PaginatesResults()
    {
        AddPublished("art-1", "Article 1", DateTimeOffset.UtcNow.AddDays(-3));
        AddPublished("art-2", "Article 2", DateTimeOffset.UtcNow.AddDays(-2));
        AddPublished("art-3", "Article 3", DateTimeOffset.UtcNow.AddDays(-1));

        var page1 = await Repo().GetPageAsync(1, 2);
        var page2 = await Repo().GetPageAsync(2, 2);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
    }

    [Fact]
    public async Task GetPageAsync_WithTag_ReturnsOnlyMatchingArticles()
    {
        AddPublished("art-a", "Article A", DateTimeOffset.UtcNow.AddDays(-2), tags: "queen,freddie");
        AddPublished("art-b", "Article B", DateTimeOffset.UtcNow.AddDays(-1), tags: "roger");
        AddPublished("art-c", "Article C", DateTimeOffset.UtcNow, tags: "queen,brian");

        var result = await Repo().GetPageAsync(1, 10, "queen");

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.True(
            ("," + a.Tags + ",").Contains(",queen,", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetPageAsync_WithTag_PaginatesFilteredResults()
    {
        AddPublished("tagged-1", "Tagged 1", DateTimeOffset.UtcNow.AddDays(-3), tags: "queen");
        AddPublished("tagged-2", "Tagged 2", DateTimeOffset.UtcNow.AddDays(-2), tags: "queen");
        AddPublished("tagged-3", "Tagged 3", DateTimeOffset.UtcNow.AddDays(-1), tags: "queen");
        AddPublished("other", "Other", DateTimeOffset.UtcNow, tags: "roger");

        var page1 = await Repo().GetPageAsync(1, 2, "queen");
        var page2 = await Repo().GetPageAsync(2, 2, "queen");

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
    }

    // --- GetBySlugAsync ---

    [Fact]
    public async Task GetBySlugAsync_ReturnsArticleBySlug()
    {
        AddPublished("my-slug", "My Article", DateTimeOffset.UtcNow, excerpt: "An excerpt.");

        var result = await Repo().GetBySlugAsync("my-slug");

        Assert.NotNull(result);
        Assert.Equal("my-slug", result.Slug);
        Assert.Equal("My Article", result.Title);
        Assert.Equal("An excerpt.", result.Excerpt);
        Assert.Equal("Test Author", result.AuthorDisplayName);
        Assert.NotEmpty(result.Body);
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_WhenNotFound()
    {
        Assert.Null(await Repo().GetBySlugAsync("nonexistent"));
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_ForDraftSlug()
    {
        AddDraft("draft-slug", "Draft Article");

        Assert.Null(await Repo().GetBySlugAsync("draft-slug"));
    }

    [Fact]
    public async Task GetBySlugAsync_MapsWordCountAndCoverImage()
    {
        AddPublished("cover-art", "Cover Article", DateTimeOffset.UtcNow,
            coverImageBlobPath: "members/test/cover.webp");

        var result = await Repo().GetBySlugAsync("cover-art");

        Assert.NotNull(result);
        Assert.Equal("members/test/cover.webp", result.CoverImageBlobPath);
        Assert.True(result.WordCount > 0);
        Assert.True(result.ReadTimeMinutes >= 1);
    }

    // --- GetAdjacentAsync ---

    [Fact]
    public async Task GetAdjacentAsync_ReturnsPreviousAndNext()
    {
        var t1 = DateTimeOffset.UtcNow.AddDays(-2);
        var t2 = DateTimeOffset.UtcNow.AddDays(-1);
        var t3 = DateTimeOffset.UtcNow;
        AddPublished("prev-art", "Previous Article", t1);
        AddPublished("middle-art", "Middle Article", t2);
        AddPublished("next-art", "Next Article", t3);

        var (prev, next) = await Repo().GetAdjacentAsync(t2);

        Assert.NotNull(prev);
        Assert.Equal("prev-art", prev.Slug);
        Assert.NotNull(next);
        Assert.Equal("next-art", next.Slug);
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsNulls_WhenOnlyOneArticle()
    {
        var t = DateTimeOffset.UtcNow;
        AddPublished("only-art", "Only Article", t);

        var (prev, next) = await Repo().GetAdjacentAsync(t);

        Assert.Null(prev);
        Assert.Null(next);
    }

    [Fact]
    public async Task GetAdjacentAsync_ReturnsNullPrev_ForOldestArticle()
    {
        var t1 = DateTimeOffset.UtcNow.AddDays(-1);
        var t2 = DateTimeOffset.UtcNow;
        AddPublished("older", "Older Article", t1);
        AddPublished("newer", "Newer Article", t2);

        var (prev, next) = await Repo().GetAdjacentAsync(t1);

        Assert.Null(prev);
        Assert.NotNull(next);
        Assert.Equal("newer", next.Slug);
    }

    // --- GetSitemapEntriesAsync ---

    [Fact]
    public async Task GetSitemapEntriesAsync_ReturnsPublishedArticles()
    {
        AddPublished("sitemap-1", "Sitemap Article 1", DateTimeOffset.UtcNow.AddDays(-1));
        AddPublished("sitemap-2", "Sitemap Article 2", DateTimeOffset.UtcNow);
        AddDraft("sitemap-draft", "Draft");

        var result = await Repo().GetSitemapEntriesAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.NotEmpty(a.Slug));
    }

    [Fact]
    public async Task GetSitemapEntriesAsync_ReturnsEmptyWhenNoPublished()
    {
        AddDraft("draft-1", "Draft 1");

        var result = await Repo().GetSitemapEntriesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAndCount_IncludeStandaloneEditorialArticles_WithoutLoadingBodies()
    {
        AddPublished("community", "Community Article", DateTimeOffset.UtcNow.AddDays(-1));
        dbContext.EditorialArticles.Add(new EditorialArticleEntity
        {
            Id = Guid.NewGuid(),
            Title = "Editorial draft",
            Slug = "editorial-draft",
            Excerpt = "Draft excerpt",
            Body = "Draft body",
            AuthorName = "Editor",
            Category = "Feature",
            Status = EditorialArticleStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "editor@test.local",
            LiveTitle = "Editorial live",
            LiveSlug = "editorial-live",
            LiveExcerpt = "Live excerpt",
            LiveBody = "This full live body must stay out of list queries.",
            LiveWordCount = 10,
            LiveAuthorName = "Editor",
            LiveCategory = "Feature",
            LivePublishedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var repository = Repo();
        var page = await repository.GetPageAsync(1, 10);

        Assert.Equal(2, await repository.GetCountAsync());
        var editorial = Assert.Single(page, article => article.Slug == "editorial-live");
        Assert.Equal(string.Empty, editorial.Body);
        Assert.Equal(10, editorial.WordCount);
    }

    [Fact]
    public void PublishedListSql_OmitsBodies_AndPagesInSqlServer()
    {
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer("Server=(local);Database=QueryShapeOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var sqlServerContext = new QueenZoneDbContext(options);
        var sql = new EfArticleRepository(sqlServerContext)
            .PublishedListQuery()
            .OrderByDescending(article => article.PublishedAt)
            .Skip(12)
            .Take(12)
            .ToQueryString();

        Assert.DoesNotContain("Body", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
    }
}
