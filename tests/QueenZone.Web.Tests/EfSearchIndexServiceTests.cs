using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfSearchIndexServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfSearchIndexService service;
    private readonly SearchIndexRevision revision = new();

    public EfSearchIndexServiceTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        service = new EfSearchIndexService(dbContext, revision);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Writes_AfterCommit_AdvanceSearchRevision()
    {
        var initial = revision.Value;
        await service.UpsertAsync(Document("news:1", SiteSearchContentType.News, "Queen"));
        Assert.Equal(initial + 1, revision.Value);
        await service.RemoveAsync("news:1");
        Assert.Equal(initial + 2, revision.Value);
        await service.ReplaceContentTypeAsync(SiteSearchContentType.News, []);
        Assert.Equal(initial + 3, revision.Value);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveAsync(" "));
        Assert.Equal(initial + 3, revision.Value);
    }

    [Fact]
    public async Task ReplaceContentTypeAsync_ReplacesOnlyTheTargetContentType()
    {
        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.News,
            [
                Document("news:1", SiteSearchContentType.News, "Old news"),
                Document("news:2", SiteSearchContentType.News, "Also old"),
            ]);
        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.Forum,
            [Document("forum-thread:9", SiteSearchContentType.Forum, "Keep me")]);

        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.News,
            [Document("news:3", SiteSearchContentType.News, "Fresh news")]);

        var rows = await dbContext.SearchDocuments.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.SourceKey == "news:3" && row.Title == "Fresh news");
        Assert.Contains(rows, row => row.SourceKey == "forum-thread:9");
        Assert.DoesNotContain(rows, row => row.SourceKey is "news:1" or "news:2");
    }

    [Fact]
    public async Task ReplaceContentTypeAsync_EmptyList_ClearsContentType()
    {
        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.Article,
            [Document("article:a", SiteSearchContentType.Article, "Gone soon")]);

        await service.ReplaceContentTypeAsync(SiteSearchContentType.Article, []);

        Assert.Empty(await dbContext.SearchDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReplaceContentTypeAsync_AssignsIdsAndIndexedAt()
    {
        var document = Document("news:42", SiteSearchContentType.News, "Has no id yet");
        Assert.Equal(Guid.Empty, document.Id);
        Assert.Equal(default, document.IndexedAt);

        await service.ReplaceContentTypeAsync(SiteSearchContentType.News, [document]);

        var stored = await dbContext.SearchDocuments.AsNoTracking().SingleAsync();
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.NotEqual(default, stored.IndexedAt);
        Assert.Equal(SiteSearchContentType.News, stored.ContentType);
    }

    [Fact]
    public async Task GetContentTypeCountsAsync_GroupsByContentType()
    {
        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.News,
            [
                Document("news:1", SiteSearchContentType.News, "One"),
                Document("news:2", SiteSearchContentType.News, "Two"),
            ]);
        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.Forum,
            [Document("forum-thread:1", SiteSearchContentType.Forum, "Thread")]);

        var counts = await service.GetContentTypeCountsAsync();

        Assert.Equal(2, counts[SiteSearchContentType.News]);
        Assert.Equal(1, counts[SiteSearchContentType.Forum]);
    }

    [Fact]
    public async Task UpsertAsync_InsertsThenUpdatesBySourceKey()
    {
        await service.UpsertAsync(Document("news:7", SiteSearchContentType.News, "Original"));
        await service.UpsertAsync(Document("news:7", SiteSearchContentType.News, "Updated title"));

        var rows = await dbContext.SearchDocuments.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Updated title", rows[0].Title);
    }

    [Fact]
    public async Task UpsertAsync_DoesNotWriteFreddieTributeDocuments()
    {
        await service.UpsertAsync(Document(
            SearchDocumentSourceKey.ForTribute(9),
            SiteSearchContentType.Tribute,
            "A leftover tribute thought"));

        Assert.Empty(await dbContext.SearchDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UpsertAsync_RemovesExistingFreddieTributeRow()
    {
        await service.UpsertAsync(Document("news:8", SiteSearchContentType.News, "Keep me"));
        dbContext.SearchDocuments.Add(Document(
            SearchDocumentSourceKey.ForTribute(11),
            SiteSearchContentType.Tribute,
            "Already indexed tribute"));
        await dbContext.SaveChangesAsync();

        await service.UpsertAsync(Document(
            SearchDocumentSourceKey.ForTribute(11),
            SiteSearchContentType.Tribute,
            "Refreshed tribute"));

        var rows = await dbContext.SearchDocuments.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("news:8", rows[0].SourceKey);
    }

    [Fact]
    public async Task ReplaceContentTypeAsync_Tribute_DeletesWithoutInserting()
    {
        dbContext.SearchDocuments.AddRange(
            Document(
                SearchDocumentSourceKey.ForTribute(4),
                SiteSearchContentType.Tribute,
                "Old tribute"),
            Document(
                "freddie-tribute:6",
                SiteSearchContentType.FreddieTribute,
                "Alias leftover"),
            Document(
                SearchDocumentSourceKey.ForTribute(7),
                SiteSearchContentType.News,
                "Mismatched tribute key"),
            Document(
                "news:8",
                SiteSearchContentType.News,
                "Keep this news"));
        await dbContext.SaveChangesAsync();

        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.Tribute,
            [Document(SearchDocumentSourceKey.ForTribute(5), SiteSearchContentType.Tribute, "New tribute")]);

        var rows = await dbContext.SearchDocuments.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("news:8", rows[0].SourceKey);
    }

    [Fact]
    public async Task ReplaceContentTypeAsync_Skips_per_document_tribute_rows()
    {
        await service.ReplaceContentTypeAsync(
            SiteSearchContentType.News,
            [
                Document("news:3", SiteSearchContentType.News, "Fresh news"),
                Document(SearchDocumentSourceKey.ForTribute(9), SiteSearchContentType.News, "Tribute key"),
                Document("news:4", SiteSearchContentType.FreddieTribute, "Tribute type"),
            ]);

        var rows = await dbContext.SearchDocuments.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("news:3", rows[0].SourceKey);
        Assert.Equal(SiteSearchContentType.News, rows[0].ContentType);
    }

    [Theory]
    [InlineData("freddie-tribute:2", "news")]
    [InlineData("news:2", "freddie-tribute")]
    public async Task UpsertAsync_DoesNotWrite_alias_or_mismatched_tribute_rows(string sourceKey, string contentType)
    {
        await service.UpsertAsync(Document(sourceKey, contentType, "Should not be indexed"));

        Assert.Empty(await dbContext.SearchDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RemoveAsync_DeletesBySourceKey()
    {
        await service.UpsertAsync(Document("news:8", SiteSearchContentType.News, "Delete me"));
        await service.RemoveAsync("news:8");

        Assert.Empty(await dbContext.SearchDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task FindByExactTitleAsync_ReturnsMatchingTitlesAndKeepsInMemoryFilter()
    {
        await service.UpsertAsync(Document("news:rhapsody", SiteSearchContentType.News, "Bohemian Rhapsody"));
        await service.UpsertAsync(Document("forum-thread:other", SiteSearchContentType.Forum, "Somebody to Love"));

        var matches = await service.FindByExactTitleAsync("Bohemian Rhapsody");

        var match = Assert.Single(matches);
        Assert.Equal("Bohemian Rhapsody", match.Title);
        Assert.Equal("/search-test/news:rhapsody", match.Url);
        Assert.Equal(SiteSearchContentType.News, match.ContentType);
        Assert.True(string.IsNullOrEmpty(match.Body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindByExactTitleAsync_BlankTitle_ReturnsEmpty(string? title)
    {
        await service.UpsertAsync(Document("news:rhapsody", SiteSearchContentType.News, "Bohemian Rhapsody"));

        var matches = await service.FindByExactTitleAsync(title!);

        Assert.Empty(matches);
    }

    private static SearchDocumentEntity Document(string sourceKey, string contentType, string title) =>
        new()
        {
            SourceKey = sourceKey,
            ContentType = contentType,
            Title = title,
            Body = title,
            Summary = title,
            Url = $"/search-test/{sourceKey}",
            PublishedAt = DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
        };
}
