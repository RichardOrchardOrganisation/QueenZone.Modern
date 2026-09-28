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
}
