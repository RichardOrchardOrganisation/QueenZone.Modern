using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace QueenZone.Web;

public sealed record ForumVideoCard(ForumYoutubeVideo Video, string OriginalLink);
public sealed record ForumVideoPart(string Html, ForumVideoCard? Card = null);

/// <summary>Splits a trusted projection into sanitized markup and Razor card insertion points.</summary>
public static class ForumVideoRendering
{
    public static IReadOnlyList<ForumVideoPart> Split(ForumVideoProjection projection)
    {
        if (projection.YoutubeVideos.Count == 0)
        {
            return [new(projection.Body)];
        }

        var document = new HtmlParser().ParseDocument(projection.Body);
        var anchors = document.Body!.QuerySelectorAll("a");
        // A fresh server-only comment cannot be supplied or guessed through forum HTML.
        var marker = "qz-video-" + Guid.NewGuid().ToString("N");
        var cards = new List<ForumVideoCard>();
        foreach (var video in projection.YoutubeVideos)
        {
            var anchor = anchors[video.AnchorIndex];
            cards.Add(new(video, anchor.OuterHtml));
            anchor.ReplaceWith(document.CreateComment(marker));
        }

        var chunks = document.Body.InnerHtml.Split("<!--" + marker + "-->", StringSplitOptions.None);
        var parts = new List<ForumVideoPart>();
        for (var index = 0; index < cards.Count; index++)
        {
            parts.Add(new(chunks[index], cards[index]));
        }

        parts.Add(new(chunks[^1]));
        return parts;
    }
}
