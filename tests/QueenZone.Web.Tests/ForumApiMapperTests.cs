using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumApiMapperTests
{
    [Fact]
    public void ToCategoryListItem_StripsHtmlFromNameDescriptionAndLatestThreadTitle()
    {
        var category = new ForumCategoryItem(
            9,
            "Sharing the Music <i>Request</i>",
            "Discuss &amp; <b>request</b> items",
            3,
            null,
            "Request <b>non-official</b> Queen related items",
            1);

        var dto = ForumApiMapper.ToCategoryListItem(category);

        Assert.Equal("Sharing the Music Request", dto.Name);
        Assert.Equal("Discuss & request items", dto.Description);
        Assert.Equal("Request non-official Queen related items", dto.LatestThreadTitle);
        Assert.Equal("/forum/9/sharing-the-music-request", dto.DetailPath);
        Assert.DoesNotContain("<", dto.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("<", dto.Description, StringComparison.Ordinal);
        Assert.Equal(ForumApiMapper.ToCategoryListItems([category])[0], dto);
    }

    [Fact]
    public void ToTopicListItem_StripsHtmlFromTitle()
    {
        var topic = new ForumTopicItem(
            1002,
            "Request <b>non-official</b> Queen related items",
            new DateTime(2020, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            "brian",
            4,
            null,
            false);

        var dto = ForumApiMapper.ToTopicListItem(topic);

        Assert.Equal("Request non-official Queen related items", dto.Title);
        Assert.Equal("/forum/topic/1002/request-non-official-queen-related-items", dto.DetailPath);
        Assert.Equal(ForumApiMapper.ToTopicListItems([topic])[0], dto);
    }

    [Fact]
    public void ToRecentThread_StripsHtmlFromTitleAndCategoryName()
    {
        var item = new ForumRecentThreadItem(
            1002,
            "Request <b>non-official</b> items",
            9,
            "Sharing the Music <i>Request</i>",
            4,
            new DateTime(2020, 10, 6, 0, 0, 0, DateTimeKind.Utc));

        var dto = ForumApiMapper.ToRecentThread(item);

        Assert.Equal("Request non-official items", dto.Title);
        Assert.Equal("Sharing the Music Request", dto.CategoryName);
        Assert.Equal("/forum/topic/1002/request-non-official-items", dto.DetailPath);
        Assert.Equal(ForumApiMapper.ToRecentThreads([item])[0], dto);
    }

    [Theory]
    [InlineData("中文")]
    [InlineData("!!!")]
    [InlineData("")]
    public void Topic_link_builders_share_the_fallback_slug(string title)
    {
        var topic = new ForumTopicItem(1002, title, DateTime.UtcNow, "fan", 0, null, false);
        var recent = new ForumRecentThreadItem(1002, title, 1, "The Music", 0, DateTime.UtcNow);
        var header = new ForumTopicHeader(1002, title, 1, "The Music");
        Assert.Equal("/forum/topic/1002/topic", ForumApiMapper.ToTopicListItem(topic).DetailPath);
        Assert.Equal("/forum/topic/1002/topic", ForumApiMapper.ToRecentThread(recent).DetailPath);
        Assert.Equal("/forum/topic/1002/topic", ForumApiMapper.ToTopicDetail(header, 1).DetailPath);
    }

    [Fact]
    public void ToCategoryListItem_TreatsBlankDescriptionAsNull()
    {
        var category = new ForumCategoryItem(9, "Sharing the Music", "   ", 3, null, null, 1);

        var dto = ForumApiMapper.ToCategoryListItem(category);

        Assert.Null(dto.Description);
        Assert.Null(dto.LatestThreadTitle);
    }
}
