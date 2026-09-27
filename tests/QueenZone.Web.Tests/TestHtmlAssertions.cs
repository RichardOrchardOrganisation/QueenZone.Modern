using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace QueenZone.Web.Tests;

/// <summary>
/// Semantic HTML assertions. Prefer these to <c>Assert.Contains</c> on raw markup: they parse the page,
/// so attribute order, quoting and whitespace changes do not break the test.
/// </summary>
internal static class TestHtmlAssertions
{
    public static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    public static void AssertPageTitle(string html, string expectedTitle)
    {
        var title = Parse(html).QuerySelector("title");
        Assert.True(title is not null, "Expected a page title but none was rendered.");
        Assert.Equal(expectedTitle, title.TextContent.Trim());
    }

    /// <summary>Returns the <c>content</c> of <c>&lt;meta name="…"&gt;</c>, failing when the tag is absent.</summary>
    public static string MetaName(string html, string name) => MetaContent(Parse(html), "name", name);

    /// <summary>Returns the <c>content</c> of <c>&lt;meta property="…"&gt;</c>, failing when the tag is absent.</summary>
    public static string MetaProperty(string html, string property) => MetaContent(Parse(html), "property", property);

    public static void AssertMetaProperty(string html, string property, string expectedContent) =>
        Assert.Equal(expectedContent, MetaProperty(html, property));

    public static void AssertMetaName(string html, string name, string expectedContent) =>
        Assert.Equal(expectedContent, MetaName(html, name));

    /// <summary>Returns the <c>href</c> of <c>&lt;link rel="…"&gt;</c>, failing when the tag is absent.</summary>
    public static string LinkHref(string html, string rel)
    {
        var link = Parse(html).QuerySelectorAll("link")
            .FirstOrDefault(l => string.Equals(l.GetAttribute("rel"), rel, StringComparison.OrdinalIgnoreCase));
        Assert.True(link is not null, $"Expected <link rel=\"{rel}\"> but none was rendered.");
        return link.GetAttribute("href") ?? string.Empty;
    }

    /// <summary>Returns every element matching a CSS selector.</summary>
    public static IReadOnlyList<IElement> Select(string html, string selector) =>
        [.. Parse(html).QuerySelectorAll(selector)];

    /// <summary>Returns the single element matching a CSS selector, failing on zero or several matches.</summary>
    public static IElement SingleElement(string html, string selector) =>
        Assert.Single(Select(html, selector));

    private static string MetaContent(IDocument document, string attribute, string key)
    {
        var meta = document.QuerySelectorAll("meta")
            .FirstOrDefault(m => string.Equals(m.GetAttribute(attribute), key, StringComparison.OrdinalIgnoreCase));
        Assert.True(meta is not null, $"Expected <meta {attribute}=\"{key}\"> but none was rendered.");
        return meta.GetAttribute("content") ?? string.Empty;
    }
}
