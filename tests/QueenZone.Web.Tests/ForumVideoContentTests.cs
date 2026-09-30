using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class ForumVideoContentTests
{
    private const string Id = "M7lc1UVf-VE";
    private static UgcHtml Create() => new(Options.Create(new BlobUploadOptions()));

    public static IEnumerable<object[]> SupportedUrls()
    {
        foreach (var host in new[] { "youtube.com", "www.youtube.com", "m.youtube.com" })
        {
            foreach (var path in new[] { $"/watch?v={Id}", $"/shorts/{Id}", $"/embed/{Id}" })
            {
                foreach (var scheme in new[] { "http", "https" })
                {
                    yield return new object[] { $"{scheme}://{host}{path}" };
                }
            }
        }

        foreach (var host in new[] { "youtu.be", "www.youtu.be" })
        {
            yield return new object[] { $"http://{host}/{Id}" };
            yield return new object[] { $"https://{host}/{Id}" };
        }
    }

    [Theory]
    [MemberData(nameof(SupportedUrls))]
    public void Approved_hosts_paths_and_legacy_http_produce_fixed_https_urls(string url)
    {
        var video = Assert.IsType<ForumYoutubeVideo>(ForumVideoContent.TryRecognize(url));
        Assert.Equal(Id, video.VideoId);
        Assert.Equal("youtube", video.Provider);
        Assert.Equal($"https://www.youtube.com/watch?v={Id}", video.WatchUrl);
    }

    [Theory]
    [InlineData("https://youtube.com.evil.example/watch?v=M7lc1UVf-VE")]
    [InlineData("https://notyoutube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com./watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com@evil.example/watch?v=M7lc1UVf-VE")]
    [InlineData("https://fan@youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com:444/watch?v=M7lc1UVf-VE")]
    [InlineData("ftp://youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-V")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-VEE")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-ＶE")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-VE&v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-VE&V=abcdefghijk")]
    [InlineData("https://youtu.be/M7lc1UVf-VE?v=abcdefghijk")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-VE%26v=abcdefghijk")]
    [InlineData("https://youtube.com/%77atch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com/a/../watch?v=M7lc1UVf-VE")]
    [InlineData("https://youtube.com/playlist?list=M7lc1UVf-VE")]
    [InlineData("https://youtube.com/live/M7lc1UVf-VE")]
    [InlineData("https://youtube.com/channel/M7lc1UVf-VE")]
    [InlineData("https://youtu.be/M7lc1UVf-VE/extra")]
    [InlineData("https://youtube.com/watch?v=M7lc1UVf-VE\n")]
    [InlineData(" https://youtube.com/watch?v=M7lc1UVf-VE")]
    [InlineData("")]
    [InlineData(null)]
    public void Hostile_or_unsupported_urls_are_not_descriptors(string? url) =>
        Assert.Null(ForumVideoContent.TryRecognize(url));

    [Theory]
    [InlineData("t=90", 90)]
    [InlineData("start=90", 90)]
    [InlineData("t=1m30s", 90)]
    [InlineData("t=1h2m3s", 3723)]
    [InlineData("t=90s", 90)]
    [InlineData("start=86400", 86400)]
    [InlineData("t=0", null)]
    [InlineData("t=24h1s", null)]
    [InlineData("start=86401", null)]
    [InlineData("t=999999999999999999999999999999", null)]
    [InlineData("t=9999999999h", null)]
    [InlineData("t=3s1m", null)]
    [InlineData("t=-1", null)]
    [InlineData("t=1.5", null)]
    [InlineData("t=", null)]
    [InlineData("t=bad", null)]
    [InlineData("t=1&t=2", null)]
    [InlineData("t=1&start=2", null)]
    public void Starts_are_bounded_and_ambiguous_or_malformed_times_are_ignored(string query, int? expected)
    {
        var video = Assert.IsType<ForumYoutubeVideo>(ForumVideoContent.TryRecognize($"https://youtu.be/{Id}?{query}&autoplay=1&origin=evil&list=bad"));
        Assert.Equal(expected, video.StartSeconds);
        Assert.DoesNotContain("autoplay", video.WatchUrl);
        Assert.DoesNotContain("origin", video.WatchUrl);
        Assert.DoesNotContain("list", video.WatchUrl);
    }

    [Fact]
    public void Projection_preserves_body_and_maps_all_anchors_including_quotes_and_images()
    {
        const string body = """
            <h2>Heading</h2><p>See <a href="https://youtu.be/M7lc1UVf-VE">this</a>.</p>
            <blockquote><p><a href="https://youtu.be/M7lc1UVf-VE">quote</a></p></blockquote>
            <div class="qz-bbcode-quote"><p><a href="https://youtu.be/M7lc1UVf-VE">BB quote</a></p></div>
            <p><img src="/ugc/forum/a/b.webp" alt="guitar"></p>
            <p> <strong><a href="http://m.youtube.com/watch?v=M7lc1UVf-VE&amp;t=1m30s">First</a></strong> </p>
            <p><a href="https://youtu.be/M7lc1UVf-VE?t=90">Duplicate</a></p>
            <div><span><a href="https://youtu.be/M7lc1UVf-VE?t=91">Second</a></span></div>
            <p><a href="https://youtu.be/abcdefghijk">Third</a></p>
            <p><a href="https://youtu.be/12345678901">Overflow</a></p>
            """;
        var ugc = Create();
        var result = ForumVideoContent.Project(body, ugc);
        Assert.Equal(ugc.FormatForDisplay(body), result.Body);
        Assert.Equal(new[] { 4, 6, 7 }, result.YoutubeVideos.Select(video => video.AnchorIndex));
        Assert.Equal(new int?[] { 90, 91, null }, result.YoutubeVideos.Select(video => video.StartSeconds));
        var anchors = new HtmlParser().ParseDocument(result.Body).QuerySelectorAll("a");
        Assert.Equal("First", anchors[result.YoutubeVideos[0].AnchorIndex].TextContent);
        Assert.Contains("Overflow", result.Body);
        Assert.DoesNotContain("iframe", result.Body);
    }

    [Fact]
    public void Plain_legacy_lines_and_nested_markup_keep_positions_and_prose()
    {
        var result = ForumVideoContent.Project($"Intro\nhttp://youtu.be/{Id}\nInline https://youtu.be/abcdefghijk text\nhttps://youtu.be/12345678901", Create());
        Assert.Equal(new[] { 0, 2 }, result.YoutubeVideos.Select(video => video.AnchorIndex));
        Assert.Contains("Intro<br>", result.Body);
    }

    [Theory]
    [InlineData("<p>Text <a href='https://youtu.be/M7lc1UVf-VE'>video</a></p>")]
    [InlineData("<p><a href='https://youtu.be/M7lc1UVf-VE'>video</a>!</p>")]
    [InlineData("<p><a href='https://youtu.be/M7lc1UVf-VE'>one</a><a href='https://youtu.be/abcdefghijk'>two</a></p>")]
    [InlineData("<h2><a href='https://youtu.be/M7lc1UVf-VE'>video</a></h2>")]
    [InlineData("<pre><a href='https://youtu.be/M7lc1UVf-VE'>video</a></pre>")]
    [InlineData("<code><a href='https://youtu.be/M7lc1UVf-VE'>video</a></code>")]
    [InlineData("<p><a href='https://youtu.be/M7lc1UVf-VE'><img src='/ugc/forum/a/b.webp'></a></p>")]
    public void Ineligible_sources_remain_without_metadata(string body) =>
        Assert.Empty(ForumVideoContent.Project(body, Create()).YoutubeVideos);

    [Fact]
    public void Injected_markup_cannot_fabricate_metadata_or_relax_sanitizer()
    {
        const string body = """<div class="qz-forum-video" data-video-id="M7lc1UVf-VE"><iframe src="https://youtube.com/embed/M7lc1UVf-VE"></iframe><script>alert(1)</script><a href="https://evil.example" onclick="alert(1)">Link</a></div>""";
        var result = ForumVideoContent.Project(body, Create());
        Assert.Empty(result.YoutubeVideos);
        Assert.DoesNotContain("data-video", result.Body);
        Assert.DoesNotContain("qz-forum-video", result.Body);
        Assert.DoesNotContain("iframe", result.Body);
        Assert.DoesNotContain("script", result.Body);
        Assert.DoesNotContain("onclick", result.Body);
        Assert.Contains("https://evil.example", result.Body);
    }

    [Fact]
    public void Actual_archive_link_shapes_ignore_double_encoded_tracking_and_exclude_bbcode_quotes()
    {
        const string body = """Intro<br><a href="http://youtube.com/watch?v=ueGhujsuuwk">http://youtube.com/watch?v=ueGhujsuuwk</a><br><a href="http://www.youtube.com/watch?v=X7ZI0BXQx_o&amp;amp;mode=related&amp;amp;search=">Legacy tracking</a><blockquote class="qz-bbcode-quote"><div class="qz-bbcode-quote-author">Fan wrote:</div><a href="http://youtube.com/watch?v=ueGhujsuuwk">Quote</a></blockquote>""";
        var result = ForumVideoContent.Project(body, Create());
        Assert.Equal(new[] { "ueGhujsuuwk", "X7ZI0BXQx_o" }, result.YoutubeVideos.Select(video => video.VideoId));
        Assert.All(result.YoutubeVideos, video => Assert.DoesNotContain("mode", video.WatchUrl));
        Assert.Contains("qz-bbcode-quote", result.Body);
    }

    [Fact]
    public void Default_start_deduplicates_and_empty_input_is_safe()
    {
        var result = ForumVideoContent.Project($"https://youtu.be/{Id}\nhttps://youtu.be/{Id}?t=0", Create());
        Assert.Single(result.YoutubeVideos);
        Assert.Empty(ForumVideoContent.Project(null, Create()).YoutubeVideos);
        Assert.Empty(ForumVideoContent.Project("", Create()).Body);
    }
}
