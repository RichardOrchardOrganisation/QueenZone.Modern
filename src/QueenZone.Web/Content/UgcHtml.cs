using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Server-side sanitization for user-generated rich HTML (forum, future UGC articles).
/// Separate from <see cref="NewsArticleContent"/> so public news allowlists do not expand by accident.
/// </summary>
/// <remarks>
/// <para><b>Image src policy (forum / UGC):</b> only app-proxy UGC paths
/// (<c>/ugc/forum/...</c>, etc.) and configured UGC hosts (CDN base or Azure blob paths under known
/// <c>ugc-*</c> containers) are kept. Arbitrary external hosts such as
/// <c>https://evil.example.com/...</c> are stripped. Prefer proxy paths so private containers are not
/// exposed via direct blob URLs.</para>
/// </remarks>
public sealed partial class UgcHtml(IOptions<BlobUploadOptions> blobUploadOptions)
{
    private readonly BlobUploadOptions blobOptions = blobUploadOptions.Value;

    public string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        return CreateSanitizer(forDisplay: false).Sanitize(html);
    }

    /// <summary>
    /// Prepares a member-authored body for the same storage path as website Quill posts:
    /// plain text becomes escaped <c>&lt;p&gt;</c> paragraphs, then the UGC allowlist runs.
    /// </summary>
    public string NormalizeForStorage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var source = LooksLikeHtml(body) ? body : WrapPlainText(body);
        return Sanitize(source);
    }

    public static string WrapPlainText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var paragraphs = normalized.Split(
            "\n\n",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var html = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            html.Append("<p>");
            html.Append(WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>", StringComparison.Ordinal));
            html.Append("</p>");
        }

        return html.ToString();
    }

    /// <summary>
    /// Sanitizes UGC HTML for public rendering. Images use the stored thumbnail proxy path
    /// (or <c>?size=thumb</c>) and wrap a link to the full-size image.
    /// Plain-text legacy bodies keep auto-linked URLs via <see cref="NewsArticleContent.FormatBody"/>.
    /// </summary>
    public string FormatForDisplay(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        if (!LooksLikeHtml(body))
        {
            return NewsArticleContent.FormatBody(body);
        }

        return CreateSanitizer(forDisplay: true).Sanitize(body);
    }

    public bool IsAllowedImageSrc(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        var trimmed = src.Trim();

        // App-relative UGC proxy paths — preferred product contract (served by this web app).
        if (trimmed.StartsWith("/ugc/", StringComparison.OrdinalIgnoreCase)
            && UgcProxyPaths.IsProxyImageSrc(trimmed))
        {
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.Host;

        // Configured CDN / public base for UGC.
        if (!string.IsNullOrWhiteSpace(blobOptions.PublicBaseUrl)
            && Uri.TryCreate(blobOptions.PublicBaseUrl.Trim(), UriKind.Absolute, out var publicUri)
            && (string.Equals(host, publicUri.Host, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + publicUri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Direct Azure blob URLs only when the path references a known UGC container.
        if (host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
            && PathContainsUgcContainer(uri.AbsolutePath))
        {
            return true;
        }

        return false;
    }

    private static bool PathContainsUgcContainer(string path)
    {
        foreach (var container in BlobUploadContainers.All)
        {
            if (path.Contains("/" + container + "/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/" + container + "/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/" + container, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeHtml(string value) => HtmlTags.Pattern().IsMatch(value);

    [ExcludeFromCodeCoverage(Justification = "HtmlSanitizer wiring; covered via Sanitize tests.")]
    private HtmlSanitizer CreateSanitizer(bool forDisplay)
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "br", "div", "span", "strong", "b", "em", "i", "u",
                     "ul", "ol", "li", "a", "h2", "h3", "h4", "blockquote", "img",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("src");
        sanitizer.AllowedAttributes.Add("alt");
        sanitizer.AllowedAttributes.Add("title");
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("loading");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add(Uri.UriSchemeHttp);
        sanitizer.AllowedSchemes.Add(Uri.UriSchemeHttps);

        sanitizer.AllowedCssProperties.Clear();

        sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is not IElement element)
            {
                return;
            }

            RestrictClasses(element);

            if (string.Equals(element.TagName, "A", StringComparison.OrdinalIgnoreCase)
                && element.HasAttribute("href"))
            {
                element.SetAttribute("rel", "noopener noreferrer");
                element.SetAttribute("target", "_blank");
            }

            if (string.Equals(element.TagName, "IMG", StringComparison.OrdinalIgnoreCase))
            {
                var src = element.GetAttribute("src");
                if (!IsAllowedImageSrc(src))
                {
                    element.Remove();
                    return;
                }

                if (forDisplay)
                {
                    RewriteImageForDisplay(element, src!);
                }
            }
        };

        return sanitizer;
    }

    /// <summary>
    /// Classes member HTML may carry (#1662): the ones this app adds for images and the legacy
    /// BBCode quote markup that <c>site.css</c> styles. The Quill composer enables no class-based
    /// formats, so nothing else is legitimate. Anything else could hook site or admin styling.
    /// </summary>
    private static readonly HashSet<string> AllowedClasses = new(StringComparer.Ordinal)
    {
        "qz-ugc-img",
        "qz-ugc-img-link",
        "qz-bbcode-quote",
        "qz-bbcode-quote-author",
    };

    private static void RestrictClasses(IElement element)
    {
        var existing = element.GetAttribute("class");
        if (existing is null)
        {
            return;
        }

        var kept = existing
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(AllowedClasses.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (kept.Length == 0)
        {
            element.RemoveAttribute("class");
            return;
        }

        element.SetAttribute("class", string.Join(' ', kept));
    }

    private static void RewriteImageForDisplay(IElement img, string src)
    {
        // Prefer stored -thumb.webp sibling; fall back to ?size=thumb on the proxy.
        string fullHref;
        string thumbSrc;

        if (UgcProxyPaths.TryParseProxySrc(src, out var container, out var blobName))
        {
            // If the src already points at a thumb blob, link full to the non-thumb sibling.
            var isThumbBlob = blobName.EndsWith("-thumb.webp", StringComparison.OrdinalIgnoreCase)
                || blobName.EndsWith("-thumb", StringComparison.OrdinalIgnoreCase);

            if (isThumbBlob)
            {
                var fullBlob = blobName
                    .Replace("-thumb.webp", ".webp", StringComparison.OrdinalIgnoreCase)
                    .Replace("-thumb", string.Empty, StringComparison.OrdinalIgnoreCase);
                fullHref = UgcProxyPaths.GetPath(container, fullBlob);
                thumbSrc = UgcProxyPaths.GetPath(container, blobName);
            }
            else
            {
                fullHref = UgcProxyPaths.GetPath(container, blobName);
                thumbSrc = UgcProxyPaths.GetPath(container, UgcProxyPaths.ToThumbBlobName(blobName));
            }
        }
        else
        {
            fullHref = src;
            thumbSrc = src.Contains('?', StringComparison.Ordinal)
                ? src + "&size=thumb"
                : src + "?size=thumb";
        }

        img.SetAttribute("src", thumbSrc);
        if (!img.HasAttribute("alt"))
        {
            img.SetAttribute("alt", string.Empty);
        }

        img.SetAttribute("class", MergeClass(img.GetAttribute("class"), "qz-ugc-img"));
        img.SetAttribute("loading", "lazy");

        // Skip wrapping if already inside a link.
        if (img.ParentElement is { } parent
            && string.Equals(parent.TagName, "A", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var owner = img.Owner;
        if (owner is null)
        {
            return;
        }

        var anchor = owner.CreateElement("a");
        anchor.SetAttribute("href", fullHref);
        anchor.SetAttribute("target", "_blank");
        anchor.SetAttribute("rel", "noopener noreferrer");
        anchor.SetAttribute("class", "qz-ugc-img-link");

        img.ReplaceWith(anchor);
        anchor.AppendChild(img);
    }

    private static string MergeClass(string? existing, string add)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return add;
        }

        if (existing.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(add, StringComparer.Ordinal))
        {
            return existing;
        }

        return existing + " " + add;
    }
}
