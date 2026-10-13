using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class UgcHtmlTests
{
    private static UgcHtml Create(string? publicBaseUrl = "https://cdn.queenzone.test") =>
        new(Options.Create(new BlobUploadOptions { PublicBaseUrl = publicBaseUrl }));

    [Fact]
    public void Sanitize_strips_script_and_event_handlers()
    {
        var html = Create().Sanitize(
            """<p onclick="alert(1)">Hi<script>alert(2)</script></p><iframe src="https://evil.test"></iframe>""");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hi", html);
    }

    [Fact]
    public void Sanitize_keeps_allowed_markup_and_approved_images()
    {
        var input = """
            <p><strong>Bold</strong> and <em>em</em></p>
            <ul><li>One</li></ul>
            <img src="https://cdn.queenzone.test/ugc-forum/x.jpg" alt="x">
            <img src="https://evil.test/phish.jpg" alt="no">
            """;

        var html = Create().Sanitize(input);

        Assert.Contains("<strong>", html);
        Assert.Contains("<em>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("cdn.queenzone.test/ugc-forum/x.jpg", html);
        Assert.DoesNotContain("evil.test", html);
    }

    [Fact]
    public void Sanitize_preserves_ugc_proxy_img_and_strips_external()
    {
        // Policy: only UGC proxy paths (and configured UGC hosts) — not arbitrary external images.
        var input = """
            <p>Hello</p>
            <img src="/ugc/forum/members/abc/photo.webp" alt="ok">
            <img src="https://evil.example.com/phish.jpg" alt="bad">
            """;

        var html = Create(publicBaseUrl: null).Sanitize(input);

        Assert.Contains("/ugc/forum/members/abc/photo.webp", html);
        Assert.DoesNotContain("evil.example.com", html);
    }

    [Fact]
    public void Sanitize_allows_azure_blob_ugc_container_urls()
    {
        var html = Create(publicBaseUrl: null).Sanitize(
            """<img src="https://acct.blob.core.windows.net/ugc-articles/editors/a/b.jpg" alt="ok">""");

        Assert.Contains("blob.core.windows.net/ugc-articles/", html);
    }

    [Fact]
    public void Sanitize_rejects_azure_blob_non_ugc_paths()
    {
        var html = Create(publicBaseUrl: null).Sanitize(
            """<img src="https://acct.blob.core.windows.net/legacy-photos/a.jpg" alt="no">""");

        Assert.DoesNotContain("legacy-photos", html);
    }

    [Fact]
    public void FormatForDisplay_wraps_proxy_image_with_thumb_and_full_link()
    {
        var html = Create(publicBaseUrl: null).FormatForDisplay(
            """<p><img src="/ugc/forum/editors/me/abc.webp" alt="scan"></p>""");

        Assert.Contains("href=\"/ugc/forum/editors/me/abc.webp\"", html);
        Assert.Contains("src=\"/ugc/forum/editors/me/abc-thumb.webp\"", html);
        Assert.Contains("qz-ugc-img", html);
        Assert.DoesNotContain("evil", html);
    }

    [Fact]
    public void FormatForDisplay_does_not_double_wrap_linked_thumb()
    {
        var html = Create(publicBaseUrl: null).FormatForDisplay(
            """<a href="/ugc/forum/a/full.webp"><img src="/ugc/forum/a/full-thumb.webp" alt="x" class="qz-ugc-img"></a>""");

        Assert.Contains("full-thumb.webp", html);
        // One anchor only (no nested re-wrap).
        Assert.Equal(1, html.Split("<a ", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arbitrary_class_tokens_are_stripped_on_save_and_display(bool forDisplay)
    {
        const string input = """<p class="qz-admin-bar evil">a</p><a href="https://example.com" class="qz-button">b</a>""";
        var sut = Create();

        var html = forDisplay ? sut.FormatForDisplay(input) : sut.Sanitize(input);

        Assert.DoesNotContain("class=", html);
        Assert.DoesNotContain("qz-admin-bar", html);
        Assert.DoesNotContain("qz-button", html);
    }

    [Fact]
    public void Allowed_legacy_quote_classes_survive_and_unknown_neighbours_do_not()
    {
        var html = Create().Sanitize(
            """<blockquote class="qz-bbcode-quote evil"><div class="qz-bbcode-quote-author"><strong>Fred</strong></div>hi</blockquote>""");

        Assert.Contains("<blockquote class=\"qz-bbcode-quote\">", html);
        Assert.Contains("class=\"qz-bbcode-quote-author\"", html);
        Assert.DoesNotContain("evil", html);
    }

    [Fact]
    public void FormatForDisplay_image_keeps_only_the_ugc_class_and_still_gets_it()
    {
        var html = Create(publicBaseUrl: null).FormatForDisplay(
            """<p><img src="/ugc/forum/editors/me/abc.webp" alt="scan" class="qz-admin-bar"></p>""");

        Assert.Contains("class=\"qz-ugc-img\"", html);
        Assert.DoesNotContain("qz-admin-bar", html);
        Assert.Contains("class=\"qz-ugc-img-link\"", html);
    }

    [Fact]
    public void FormatForDisplay_plain_text_auto_links()
    {
        var html = Create().FormatForDisplay("See https://example.com/page for details.");
        Assert.Contains("<a href=\"https://example.com/page\"", html);
    }

    [Fact]
    public void IsAllowedImageSrc_rejects_foreign_host_even_with_ugc_path()
    {
        Assert.True(Create().IsAllowedImageSrc("/ugc/forum/x.webp"));
        Assert.False(Create().IsAllowedImageSrc("https://evil.example.com/ugc/forum/x.webp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitize_empty_returns_empty(string? input) =>
        Assert.Equal(string.Empty, Create().Sanitize(input));

    [Theory]
    [InlineData("//evil.com", true)]
    [InlineData("/\\evil.com", true)]
    [InlineData("/forum\\evil.com", true)]
    [InlineData("/&#9;/evil.com", true)]
    [InlineData("/\t/evil.com", true)]
    [InlineData("/\n/evil.com", true)]
    [InlineData("/\r/evil.com", true)]
    [InlineData("/&#1;/evil.com", true)]
    [InlineData("/&#127;/evil.com", true)]
    [InlineData(" \t//evil.com\r ", true)]
    [InlineData("/forum/1", false)]
    [InlineData(" \t/forum/1\r ", false)]
    [InlineData("/", false)]
    public void Links_use_browser_normalized_internal_path_check(string href, bool external)
    {
        var sut = Create();
        var input = $"<a href=\"{href}\">Read</a>";
        foreach (var forDisplay in new[] { false, true })
        {
            var html = forDisplay ? sut.FormatForDisplay(input) : sut.Sanitize(input);
            using var document = new HtmlParser().ParseDocument(html);
            var anchor = Assert.Single(document.QuerySelectorAll("a[href]"));

            Assert.Equal(external ? "_blank" : null, anchor.GetAttribute("target"));
            Assert.Equal(external
                ? forDisplay ? "noopener noreferrer nofollow ugc" : "noopener noreferrer"
                : null, anchor.GetAttribute("rel"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Code_and_pre_have_no_attributes_on_save_and_display(bool forDisplay)
    {
        const string input = """<pre class="qz-bbcode-quote" title="x">pre</pre><code title="x" class="qz-ugc-img" href="https://example.com" src="/ugc/forum/a.webp" alt="x" loading="lazy">code</code>""";
        var sut = Create();

        var html = forDisplay ? sut.FormatForDisplay(input) : sut.Sanitize(input);

        Assert.Equal("<pre>pre</pre><code>code</code>", html);
    }

    [Theory]
    [InlineData("/ugc/forum/editors/me/abc.webp")]
    [InlineData("https://cdn.queenzone.test/ugc-forum/x.jpg")]
    public void FormatForDisplay_image_wrapper_has_external_link_rel(string src)
    {
        using var document = new HtmlParser().ParseDocument(Create().FormatForDisplay($"<img src=\"{src}\">"));
        var anchor = Assert.Single(document.QuerySelectorAll("a.qz-ugc-img-link"));

        Assert.Equal(src, anchor.GetAttribute("href"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Equal("noopener noreferrer nofollow ugc", anchor.GetAttribute("rel"));
    }

    [Theory]
    [InlineData("https://example.com/path).,;:!?", "https://example.com/path", ").,;:!?")]
    [InlineData("https://example.com/path.", "https://example.com/path", ".")]
    [InlineData("https://example.com/path,", "https://example.com/path", ",")]
    [InlineData("https://example.com/path;", "https://example.com/path", ";")]
    [InlineData("https://example.com/path:", "https://example.com/path", ":")]
    [InlineData("https://example.com/path!", "https://example.com/path", "!")]
    [InlineData("https://example.com/path?", "https://example.com/path", "?")]
    [InlineData("https://example.com/wiki/Queen_(band)", "https://example.com/wiki/Queen_(band)", "")]
    [InlineData("https://example.com/wiki/Queen_(band)).", "https://example.com/wiki/Queen_(band)", ").")]
    [InlineData("https://example.com/a_(b_(c)))!", "https://example.com/a_(b_(c))", ")!")]
    public void FormatForDisplay_auto_links_without_trailing_punctuation(string text, string url, string suffix)
    {
        foreach (var input in new[] { text, $"<p>{text}</p>" })
        {
            using var document = new HtmlParser().ParseDocument(Create().FormatForDisplay(input));
            var anchor = Assert.Single(document.QuerySelectorAll("a"));

            Assert.Equal(url, anchor.GetAttribute("href"));
            Assert.Equal(url, anchor.TextContent);
            Assert.Equal(text, document.Body!.TextContent);
            Assert.Equal(suffix, anchor.NextSibling?.TextContent ?? string.Empty);
        }
    }

    [Theory]
    [InlineData("data:text/plain,hello")]
    [InlineData("javascript:alert(1)")]
    [InlineData("java&#115;cript:alert(1)")]
    [InlineData("javascript&#58;alert(1)")]
    public void Unsafe_schemes_are_stripped_and_not_auto_linked(string url)
    {
        var sut = Create();
        var input = $"<p>{url}</p><a href=\"{url}\">bad</a>";
        foreach (var html in new[] { sut.Sanitize(input), sut.FormatForDisplay(input) })
        {
            using var document = new HtmlParser().ParseDocument(html);

            Assert.Empty(document.QuerySelectorAll("a[href]"));
            Assert.Single(document.QuerySelectorAll("a"));
            Assert.Contains("bad", document.Body!.TextContent);
        }
    }

    [Theory]
    [InlineData("span")]
    [InlineData("div")]
    public void FormatForDisplay_auto_links_deeply_nested_html(string tag)
    {
        var input = string.Concat(Enumerable.Repeat($"<{tag}>", 500))
            + "https://example.com/deep"
            + string.Concat(Enumerable.Repeat($"</{tag}>", 500));

        using var document = new HtmlParser().ParseDocument(Create().FormatForDisplay(input));
        var anchor = Assert.Single(document.QuerySelectorAll("a"));

        Assert.Equal("https://example.com/deep", anchor.GetAttribute("href"));
        Assert.Equal(500, document.QuerySelectorAll(tag).Length);
    }

    [Fact]
    public void Standalone_bare_youtube_url_in_html_post_produces_video_card()
    {
        var projection = ForumVideoContent.Project("<p>https://youtu.be/M7lc1UVf-VE</p>", Create());

        var part = Assert.Single(ForumVideoRendering.Split(projection), part => part.Card is not null);

        Assert.Equal("M7lc1UVf-VE", part.Card!.Video.VideoId);
        Assert.Contains("href=\"https://youtu.be/M7lc1UVf-VE\"", part.Card.OriginalLink);
    }
}
