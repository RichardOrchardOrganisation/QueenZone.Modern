using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumUgcHtmlTests
{
    private static UgcHtml Create() =>
        new(Options.Create(new BlobUploadOptions()));

    [Fact]
    public void Sanitize_StripsScriptTagsBeforePersistence()
    {
        var result = Create().Sanitize("<script>alert(1)</script>");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_KeepsForumProxyImg_StripsExternalImg()
    {
        var input =
            """<p>Hi</p><img src="/ugc/forum/a/b.webp" alt="ok"><img src="https://evil.example.com/x.jpg" alt="no">""";

        var result = Create().Sanitize(input);

        Assert.Contains("/ugc/forum/a/b.webp", result);
        Assert.DoesNotContain("evil.example.com", result);
    }

    [Fact]
    public void Sanitize_KeepsConfiguredCdnUgcImg()
    {
        var sanitizer = new UgcHtml(Options.Create(new BlobUploadOptions
        {
            PublicBaseUrl = "https://ugc.queenzone.org",
        }));

        var result = sanitizer.Sanitize(
            """<img src="https://ugc.queenzone.org/ugc-forum/editors/x.webp" alt="x">""");

        Assert.Contains("ugc.queenzone.org/ugc-forum/", result);
    }

    [Fact]
    public void WrapPlainText_EscapesMarkupAndSplitsParagraphs()
    {
        var html = UgcHtml.WrapPlainText("Hello <em>fans</em>\nsecond line\n\nNext paragraph");

        Assert.Equal(
            "<p>Hello &lt;em&gt;fans&lt;/em&gt;<br>second line</p><p>Next paragraph</p>",
            html);
    }

    [Fact]
    public void NormalizeForStorage_WrapsPlainTextAndSanitizesHtml()
    {
        var sanitizer = Create();

        var wrapped = sanitizer.NormalizeForStorage("Hello from the app");
        Assert.Equal("<p>Hello from the app</p>", wrapped);

        var sanitized = sanitizer.NormalizeForStorage("<p>Hi<script>alert(1)</script></p>");
        Assert.Equal("<p>Hi</p>", sanitized);

        Assert.Equal(string.Empty, sanitizer.NormalizeForStorage("   "));
    }

    [Fact]
    public void NormalizeForStorage_RoundTripsBmpAndSupplementaryPlaneEmoji()
    {
        var sanitizer = Create();

        Assert.Equal("<p>True ❤️</p>", sanitizer.NormalizeForStorage("True ❤️"));
        Assert.Equal("<p>🎸</p>", sanitizer.NormalizeForStorage("🎸"));
        Assert.Equal("<p>True ❤️</p>", sanitizer.NormalizeForStorage("<p>True ❤️</p>"));
        Assert.Equal("<p>🎸</p>", sanitizer.NormalizeForStorage("<p>🎸</p>"));
    }

    [Theory]
    [InlineData("<p>See <strong>https://example.com/page?a=1&amp;b=2</strong> for details.</p>")]
    [InlineData("See https://example.com/page?a=1&b=2 for details.")]
    public void FormatForDisplay_AutoLinksExternalUrls(string body)
    {
        using var document = new HtmlParser().ParseDocument(Create().FormatForDisplay(body));
        var anchor = Assert.Single(document.QuerySelectorAll("a"));
        Assert.Equal("https://example.com/page?a=1&b=2", anchor.GetAttribute("href"));
        Assert.Equal("https://example.com/page?a=1&b=2", anchor.TextContent);
        Assert.Equal("noopener noreferrer nofollow ugc", anchor.GetAttribute("rel"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
    }

    [Fact]
    public void FormatForDisplay_SkipsExistingAnchorsAndCodeSubtrees()
    {
        const string body = """
            <p><a href="https://example.com"><strong>https://example.com</strong></a></p>
            <code>https://example.com/code</code><pre><em>https://example.com/pre</em></pre>
            <p>http://example.com/outside</p>
            """;
        using var document = new HtmlParser().ParseDocument(Create().FormatForDisplay(body));

        Assert.Equal(2, document.QuerySelectorAll("a").Length);
        Assert.Empty(document.QuerySelectorAll("a a, code a, pre a"));
        Assert.Equal("https://example.com/code", document.QuerySelector("code")!.TextContent);
        Assert.Equal("https://example.com/pre", document.QuerySelector("pre")!.TextContent);
        Assert.NotNull(document.QuerySelector("a strong"));
    }

    [Fact]
    public void FormatForDisplay_HtmlLegacyUrlUsesPlainTextMapping()
    {
        const string url = "http://www.queenzone.com/queenzone/forumnew/forum_topic_view.aspx?Q_FORUM_ID=1&Q=413200";
        using var plain = new HtmlParser().ParseDocument(Create().FormatForDisplay(url));
        using var html = new HtmlParser().ParseDocument(Create().FormatForDisplay($"<p>{url.Replace("&", "&amp;", StringComparison.Ordinal)}</p>"));
        var anchor = Assert.Single(html.QuerySelectorAll("a"));

        Assert.Equal("/forum/goto/413200", anchor.GetAttribute("href"));
        Assert.Equal(plain.QuerySelector("a")!.GetAttribute("href"), anchor.GetAttribute("href"));
        Assert.Equal(url, anchor.TextContent);
        Assert.False(anchor.HasAttribute("target"));
    }

    [Theory]
    [InlineData("<p>javascript:alert(1)</p><a href='javascript:alert(1)'>bad</a>")]
    [InlineData("<p>https://example.com/&quot;onclick=&quot;alert(1)</p><script>alert(2)</script>")]
    [InlineData("<p>https://example.com/&lt;script&gt;alert(1)&lt;/script&gt;</p>")]
    public void FormatForDisplay_SanitizesAfterAutoLinking(string body)
    {
        using var document = new HtmlParser().ParseDocument(Create().FormatForDisplay(body));

        Assert.Empty(document.QuerySelectorAll("script, [onclick]"));
        if (body.Contains("https://", StringComparison.Ordinal))
        {
            Assert.Single(document.QuerySelectorAll("a[href]"));
        }
        else
        {
            Assert.Empty(document.QuerySelectorAll("a[href]"));
        }
        Assert.All(document.QuerySelectorAll("a[href]"), anchor =>
            Assert.StartsWith("https://example.com/", anchor.GetAttribute("href")));
    }

    [Theory]
    [InlineData("/news/42/story", false)]
    [InlineData("//example.com/story", true)]
    public void InternalLinksStayInSameTab(string href, bool external)
    {
        var sanitizer = Create();
        var stored = sanitizer.NormalizeForStorage($"<p><a href=\"{href}\" target=\"_blank\">Read</a></p>");
        using var document = new HtmlParser().ParseDocument(sanitizer.FormatForDisplay(stored));
        var anchor = Assert.Single(document.QuerySelectorAll("a"));

        Assert.Equal(href, anchor.GetAttribute("href"));
        Assert.Equal(external, anchor.HasAttribute("target"));
        Assert.Equal(external, anchor.HasAttribute("rel"));
    }
}
