using Microsoft.Extensions.Options;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class LegacyQueenZoneLinksTests
{
    [Theory]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=413200", "/forum/goto/413200")]
    [InlineData("http://queenzone.com/queenzone/forumNew/forum_topic_view.aspx?q=413200", "/forum/goto/413200")]
    [InlineData("https://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=413200&page=3", "/forum/goto/413200")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q_FORUM_ID=1&Q_FORUM_NAME=Queen&Q=413200", "/forum/goto/413200")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q_FORUM_ID=1&amp;Q=413200", "/forum/goto/413200")]
    [InlineData("http://queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=307378[/QUOTE]", "/forum/goto/307378")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=1096480).", "/forum/goto/1096480")]
    [InlineData("http://www.queenzone.com/forums/forum_topic_view.aspx?Q=55", "/forum/goto/55")]
    [InlineData("http://www.queenzone.com/forums/1175549/brian-may-quality-frenzy-index-here.aspx", "/forum/goto/1175549")]
    [InlineData("http://www.queenzone.com/forums/1263013/brian-may-master-vhs-transfer.aspx?page=2", "/forum/goto/1263013")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_view.aspx?Q=7", "/forum/7/forum")]
    [InlineData("http://www.queenzone.com/forums/forum_view.aspx?Q=7", "/forum/7/forum")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/default.aspx", "/forum")]
    [InlineData("http://www.queenzone.com/forums/", "/forum")]
    [InlineData("http://www.queenzone.com/queenzone/news_view.aspx?news_id=1234", "/news/1234/news")]
    [InlineData("http://www.queenzone.com/process/news_view.aspx?NEWS_ID=1234", "/news/1234/news")]
    [InlineData("http://www.queenzone.com/queenzone/news.aspx", "/news")]
    [InlineData("http://www.queenzone.org/forums/forum_topic_view.aspx?Q=55", "/forum/goto/55")]
    public void Maps_legacy_urls_to_modern_paths(string href, string expected) =>
        Assert.Equal(expected, LegacyQueenZoneLinks.TryGetModernPath(href));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/forum/topic/1/x")]
    [InlineData("https://www.queenzone.org/forum/topic/443450/man-from-manhatten")]
    [InlineData("https://example.com/queenzone/forumnew/forum_topic_view.aspx?Q=1")]
    [InlineData("ftp://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=1")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=abc")]
    [InlineData("http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=0")]
    [InlineData("http://www.queenzone.com/queenzone/news_view.aspx?q=12")]
    [InlineData("http://www.queenzone.com/queenzone/mp3.aspx?Q=12")]
    [InlineData("http://www.queenzone.com/queenzone/profile.aspx?Q=12")]
    [InlineData("http://www.queenzone.com/")]
    [InlineData("http://www.queenzone.com/default.aspx")]
    public void Leaves_other_urls_alone(string? href) =>
        Assert.Null(LegacyQueenZoneLinks.TryGetModernPath(href));

    [Fact]
    public void Forum_display_rewrites_legacy_anchor_as_same_tab_link()
    {
        var html = Ugc().FormatForDisplay(
            """<p>See <a href="http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=413200">this thread</a> and <a href="https://example.com/">elsewhere</a>.</p>""");

        Assert.Contains("""<a href="/forum/goto/413200">this thread</a>""", html);
        Assert.Contains("""<a href="https://example.com/" rel="noopener noreferrer" target="_blank">elsewhere</a>""", html);
    }

    [Fact]
    public void Forum_display_rewrites_auto_linked_plain_text_and_keeps_original_text()
    {
        var html = Ugc().FormatForDisplay(
            "Look here:\nhttp://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q_FORUM_ID=1&amp;Q=413200\nThanks");

        Assert.Contains(
            """<a href="/forum/goto/413200">http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q_FORUM_ID=1&amp;amp;Q=413200</a>""",
            html);
        Assert.DoesNotContain("target=\"_blank\">http://www.queenzone.com", html);
    }

    [Fact]
    public void Storage_sanitize_keeps_the_original_legacy_url()
    {
        var stored = Ugc().Sanitize(
            """<p><a href="http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=413200">old</a></p>""");

        Assert.Contains("forum_topic_view.aspx?Q=413200", stored);
        Assert.DoesNotContain("/forum/goto/", stored);
    }

    [Fact]
    public void News_body_rewrites_legacy_links_in_html_and_plain_text()
    {
        var html = NewsArticleContent.FormatBody(
            """<p>Discuss <a href="http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q=5">here</a>.</p>""");
        var plain = NewsArticleContent.FormatBody(
            "Earlier: http://www.queenzone.com/queenzone/news_view.aspx?news_id=42");

        Assert.Contains("""<a href="/forum/goto/5">here</a>""", html);
        Assert.Contains("""<a href="/news/42/news">http://www.queenzone.com/queenzone/news_view.aspx?news_id=42</a>""", plain);
    }

    private static UgcHtml Ugc() => new(Options.Create(new BlobUploadOptions()));
}
