using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PublicContentMapperTests
{
    [Fact]
    public void ToNewsArchiveItem_MapsPresentationFieldsAndDetailPath()
    {
        var item = new NewsItem(
            42,
            "Queen tour dates",
            "Excerpt",
            "Body",
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            "https://example.com/source",
            true,
            "custom-slug");

        var view = PublicContentMapper.ToNewsArchiveItem(item);

        Assert.Equal(42, view.Id);
        Assert.Equal("Queen tour dates", view.Title);
        Assert.Equal("Excerpt", view.Excerpt);
        Assert.Equal(item.PublishedAt, view.PublishedAt);
        Assert.Equal("/news/42/custom-slug", view.DetailPath);
        Assert.Null(view.ImageBlobKey);
        Assert.Null(view.ImageGalleryPicId);
        Assert.Null(view.ImageUrl);
        Assert.Null(view.ThumbnailUrl);
        Assert.Equal(NewsArticleImage.PlaceholderPath, view.DisplayImageUrl);
        Assert.Equal(NewsArticleImage.PlaceholderPath, view.DisplayThumbnailUrl);
    }

    [Fact]
    public void ToNewsArchiveItem_ResolvesArticlesBlobUrlsAndLeavesGalleryPickUnresolved()
    {
        var withBlob = new NewsItem(
            42,
            "Queen tour dates",
            "Excerpt",
            "Body",
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            true,
            "custom-slug",
            ImageBlobKey: "editors/me/hero.webp");

        var blobView = PublicContentMapper.ToNewsArchiveItem(withBlob);
        Assert.Equal("editors/me/hero.webp", blobView.ImageBlobKey);
        Assert.Equal("/ugc/articles/editors/me/hero.webp", blobView.ImageUrl);
        Assert.Equal("/ugc/articles/editors/me/hero.webp?size=thumb", blobView.ThumbnailUrl);
        Assert.Equal(blobView.ImageUrl, blobView.DisplayImageUrl);
        Assert.Equal(blobView.ThumbnailUrl, blobView.DisplayThumbnailUrl);

        var withGallery = withBlob with { ImageBlobKey = null, ImageGalleryPicId = 3120 };
        var galleryView = PublicContentMapper.ToNewsArchiveItem(withGallery);
        Assert.Equal(3120, galleryView.ImageGalleryPicId);
        Assert.Null(galleryView.ImageUrl);
        Assert.Null(galleryView.ThumbnailUrl);
        Assert.Equal(NewsArticleImage.PlaceholderPath, galleryView.DisplayImageUrl);
        Assert.Equal(NewsArticleImage.PlaceholderPath, galleryView.DisplayThumbnailUrl);
    }

    [Fact]
    public void ToNewsDetailItem_FromAdminArticle_UsesResolvedSlug()
    {
        var article = new AdminNewsArticle(
            7,
            "Preview title",
            "preview-title",
            "Excerpt",
            "Body text",
            new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            "https://example.com",
            false,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            "editor@example.com");

        var view = PublicContentMapper.ToNewsDetailItem(article);

        Assert.Equal(7, view.Id);
        Assert.Equal("Body text", view.Body);
        Assert.Equal("https://example.com", view.SourceUrl);
        Assert.Equal("/news/7/preview-title", view.DetailPath);
        Assert.Null(view.ImageUrl);
        Assert.Null(view.ThumbnailUrl);
        Assert.Equal(NewsArticleImage.PlaceholderPath, view.DisplayImageUrl);
        Assert.Equal(NewsArticleImage.PlaceholderPath, view.DisplayThumbnailUrl);
    }

    [Fact]
    public void ToArticleArchiveItem_IncludesCategoryAndDetailPath()
    {
        var item = new ArticleItem(
            101,
            "Studio essay",
            "Excerpt",
            "Body",
            new DateTime(2023, 5, 5, 0, 0, 0, DateTimeKind.Utc),
            null,
            "Recording",
            true);

        var view = PublicContentMapper.ToArticleArchiveItem(item);

        Assert.Equal("Recording", view.CategoryName);
        Assert.Equal("/articles/101/studio-essay", view.DetailPath);
    }

    [Fact]
    public void ToCommunityArticleArchiveItem_UsesSlugPathAndCategory()
    {
        var item = new PublishedArticleSubmission(
            Guid.NewGuid(),
            "Fan essay",
            "fan-essay",
            "Community excerpt",
            "Body",
            null,
            null,
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            "Author",
            80,
            Category: "Interviews");

        var view = PublicContentMapper.ToCommunityArticleArchiveItem(item);

        Assert.Equal(0, view.Id);
        Assert.Equal("Fan essay", view.Title);
        Assert.Equal("Community excerpt", view.Excerpt);
        Assert.Equal("Interviews", view.CategoryName);
        Assert.Equal("/articles/fan-essay", view.DetailPath);
    }

    [Fact]
    public void ToArticleDetailItem_MapsOverlayImageAuthorAndTags()
    {
        var item = new ArticleItem(
            101,
            "Studio essay",
            "Excerpt",
            "Body",
            new DateTime(2023, 5, 5, 0, 0, 0, DateTimeKind.Utc),
            null,
            "Recording",
            true,
            "editors/admin/card.webp",
            "Archive Editor",
            "queen,freddie");

        var view = PublicContentMapper.ToArticleDetailItem(item);

        Assert.Equal(NewsArticleImage.ResolveImageUrl("editors/admin/card.webp", null), view.ImageUrl);
        Assert.Equal("Archive Editor", view.AuthorName);
        Assert.Equal("queen,freddie", view.Tags);
        Assert.Equal("/articles/101/studio-essay", view.DetailPath);
    }

    [Fact]
    public void ToForumThreadHeader_TrimsNamesAndBuildsPaths()
    {
        var header = new ForumTopicHeader(1002, " Ranking every studio album ", 3, " The Music ");

        var view = PublicContentMapper.ToForumThreadHeader(header);

        Assert.Equal("Ranking every studio album", view.Title);
        Assert.Equal("The Music", view.ForumName);
        Assert.Equal("/forum/3/the-music", view.CategoryPath);
        Assert.Equal("/forum/topic/1002/ranking-every-studio-album", view.DetailPath);
    }

    [Fact]
    public void ToForumThreadHeader_StripsHtmlFromTitleAndForumName()
    {
        var header = new ForumTopicHeader(1002, "Request <b>non-official</b> Queen related items", 3, "Sharing the Music <i>Request</i>");

        var view = PublicContentMapper.ToForumThreadHeader(header);

        Assert.Equal("Request non-official Queen related items", view.Title);
        Assert.Equal("Sharing the Music Request", view.ForumName);
    }

    [Fact]
    public void ToForumCategorySummary_StripsHtmlFromNameDescriptionAndLatestThreadTitle()
    {
        var category = new ForumCategoryItem(
            9,
            "Sharing the Music <i>Request</i>",
            "Discuss &amp; <b>request</b> items",
            3,
            null,
            "Request <b>non-official</b> Queen related items",
            1);

        var view = PublicContentMapper.ToForumCategorySummary(category);

        Assert.Equal("Sharing the Music Request", view.Name);
        Assert.Equal("Discuss & request items", view.Description);
        Assert.Equal("Request non-official Queen related items", view.LatestThreadTitle);
    }

    [Fact]
    public void ToForumThreadSummary_StripsHtmlFromTitle()
    {
        var topic = new ForumTopicItem(1002, "Request <b>non-official</b> Queen related items", new DateTime(2020, 10, 6, 0, 0, 0, DateTimeKind.Utc), "brian", 4, null, false);

        var view = PublicContentMapper.ToForumThreadSummary(topic);

        Assert.Equal("Request non-official Queen related items", view.Title);
    }

    [Fact]
    public void ToForumRecentThreadSummary_StripsHtmlFromTitleAndCategoryName()
    {
        var item = new ForumRecentThreadItem(1002, "Request <b>non-official</b> items", 9, "Sharing the Music <i>Request</i>", 4, new DateTime(2020, 10, 6, 0, 0, 0, DateTimeKind.Utc));

        var view = PublicContentMapper.ToForumRecentThreadSummary(item);

        Assert.Equal("Request non-official items", view.Title);
        Assert.Equal("Sharing the Music Request", view.CategoryName);
    }

    [Fact]
    public void ToForumPostViewModel_MapsAttachments()
    {
        var post = new ForumPostItem(
            9,
            "Post body",
            new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            "brian",
            "sig",
            12,
            new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            [new ForumPostAttachment("scan.jpg", 2048, ForumAttachmentPaths.LegacyDownloadPath(9))]);

        var view = PublicContentMapper.ToForumPostViewModel(post);

        Assert.Equal("brian", view.AuthorUsername);
        Assert.Single(view.Attachments);
        Assert.Equal("scan.jpg", view.Attachments[0].FileName);
        Assert.Equal("/forum/attachment/legacy/9", view.Attachments[0].Url);
        Assert.Equal("JPG", view.Attachments[0].Extension);
        Assert.Equal("2.0 KB", view.Attachments[0].FormattedSize);
    }

    [Fact]
    public void ToForumIndexStats_AggregatesCategoryPostCounts()
    {
        var categories = new[]
        {
            new ForumCategoryItem(1, "A", null, 10, null, null, 1),
            new ForumCategoryItem(2, "B", null, 5, null, null, 2)
        };

        var stats = PublicContentMapper.ToForumIndexStats(categories, threadCount: 4);

        Assert.Equal(2, stats.ForumCount);
        Assert.Equal(4, stats.ThreadCount);
        Assert.Equal(15, stats.PostCount);
    }
}
