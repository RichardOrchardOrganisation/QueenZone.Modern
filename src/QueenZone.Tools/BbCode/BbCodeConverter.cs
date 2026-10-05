using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace QueenZone.Tools.BbCode;

/// <summary>
/// One-time conversion of legacy BBCode markup (carried through unconverted into
/// ModernForumPost.BodyHtml during import) into the same HTML vocabulary that
/// QueenZone.Web.UgcHtml's sanitizer already allows: p, br, div, span, strong, b, em, i, u,
/// ul, ol, li, a, h2, h3, h4, blockquote (attrs: href, class; http/https only).
/// Keep the tag/attribute allow-list here in sync with src/QueenZone.Web/Content/UgcHtml.cs
/// if either changes.
/// </summary>
internal static partial class BbCodeConverter
{
    private const int MaxNestingDepth = 20;

    private static readonly HashSet<string> SupportedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "quote", "quotename", "b", "i", "u", "url",
    };

    public static string Convert(string bbcode)
    {
        if (string.IsNullOrEmpty(bbcode))
        {
            return string.Empty;
        }

        var tokens = Tokenize(bbcode);
        var root = BuildTree(tokens);
        var html = Render(root);
        return Sanitize(html);
    }

    /// <summary>
    /// True if the text contains at least one recognized, supported BBCode tag (quote,
    /// quotename, b, i, u, url). Deliberately narrower than "contains a bracketed tag-like
    /// string" — forum text (song lyrics especially: "[CHORUS]", "[x3]", etc.) is full of
    /// bracket patterns that are not BBCode, and converting those posts would pointlessly
    /// rewrite their newlines into &lt;br&gt; for no real formatting gain.
    /// </summary>
    public static bool ContainsBbCode(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return Tokenize(text).Any(token => token.Kind is TokenKind.Open or TokenKind.Close);
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var lastEnd = 0;

        foreach (Match match in TagTokenRegex().Matches(input))
        {
            if (match.Index > lastEnd)
            {
                tokens.Add(Token.OfText(input[lastEnd..match.Index]));
            }

            var isClosing = match.Groups[1].Value == "/";
            var name = match.Groups[2].Value;
            var attrValue = match.Groups[3].Success ? match.Groups[3].Value : null;

            if (!SupportedTags.Contains(name))
            {
                // Unknown tag: preserve verbatim as literal text rather than failing.
                tokens.Add(Token.OfText(match.Value));
            }
            else if (isClosing)
            {
                tokens.Add(Token.Close(name));
            }
            else
            {
                tokens.Add(Token.Open(name, attrValue));
            }

            lastEnd = match.Index + match.Length;
        }

        if (lastEnd < input.Length)
        {
            tokens.Add(Token.OfText(input[lastEnd..]));
        }

        return tokens;
    }

    private static ElementNode BuildTree(List<Token> tokens)
    {
        var root = new ElementNode("#root", null);
        var stack = new List<ElementNode> { root };

        foreach (var token in tokens)
        {
            var current = stack[^1];

            switch (token.Kind)
            {
                case TokenKind.Text:
                    current.Children.Add(new TextNode(token.Text!));
                    break;

                case TokenKind.Open:
                    if (stack.Count > MaxNestingDepth)
                    {
                        // Past the nesting cap: treat as literal text, do not push a new element.
                        current.Children.Add(new TextNode(ReconstructOpenTag(token)));
                        break;
                    }

                    var element = new ElementNode(token.Name!, token.AttrValue);
                    current.Children.Add(element);
                    stack.Add(element);
                    break;

                case TokenKind.Close:
                    // Search the stack (excluding root) for the nearest matching open element.
                    var matchIndex = FindMatchingOpenTag(stack, token.Name);

                    if (matchIndex < 0)
                    {
                        // Orphan close tag with nothing open to match: literal text.
                        current.Children.Add(new TextNode($"[/{token.Name}]"));
                        break;
                    }

                    // Pop back to (but not including) the matched element. Elements above it that
                    // were never explicitly closed keep whatever children they already gathered —
                    // rendering closes them correctly since it walks the tree, not the token stream.
                    stack.RemoveRange(matchIndex, stack.Count - matchIndex);
                    break;
            }
        }

        return root;
    }

    private static int FindMatchingOpenTag(IReadOnlyList<ElementNode> stack, string? name)
    {
        for (var i = stack.Count - 1; i >= 1; i--)
        {
            if (string.Equals(stack[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static string ReconstructOpenTag(Token token) =>
        token.AttrValue is null ? $"[{token.Name}]" : $"[{token.Name}={token.AttrValue}]";

    private static string Render(ElementNode node)
    {
        var sb = new StringBuilder();
        RenderChildren(node, sb);
        return sb.ToString();
    }

    private static void RenderChildren(ElementNode node, StringBuilder sb)
    {
        foreach (var child in node.Children)
        {
            RenderNode(child, sb);
        }
    }

    private static void RenderNode(Node node, StringBuilder sb)
    {
        if (node is TextNode textNode)
        {
            sb.Append(EncodeText(textNode.Text));
            return;
        }

        var element = (ElementNode)node;
        switch (element.Name.ToLowerInvariant())
        {
            case "quote":
                sb.Append("<blockquote class=\"qz-bbcode-quote\">");
                RenderChildren(element, sb);
                sb.Append("</blockquote>");
                break;

            case "quotename":
                sb.Append("<div class=\"qz-bbcode-quote-author\"><strong>");
                RenderChildren(element, sb);
                sb.Append("</strong></div>");
                break;

            case "b":
                sb.Append("<strong>");
                RenderChildren(element, sb);
                sb.Append("</strong>");
                break;

            case "i":
                sb.Append("<em>");
                RenderChildren(element, sb);
                sb.Append("</em>");
                break;

            case "u":
                sb.Append("<u>");
                RenderChildren(element, sb);
                sb.Append("</u>");
                break;

            case "url":
                RenderUrl(element, sb);
                break;
        }
    }

    private static void RenderUrl(ElementNode element, StringBuilder sb)
    {
        var childHtml = new StringBuilder();
        RenderChildren(element, childHtml);

        var href = element.AttrValue ?? PlainText(element);
        if (IsAllowedHttpUrl(href))
        {
            sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(href)).Append('"');
            sb.Append('>');
            sb.Append(childHtml.Length > 0 ? childHtml.ToString() : WebUtility.HtmlEncode(href));
            sb.Append("</a>");
        }
        else
        {
            // Not a safe absolute http(s) URL: fall back to the (already-escaped) plain text,
            // dropping the link wrapper rather than emitting a broken/unsafe href.
            sb.Append(childHtml.Length > 0 ? childHtml.ToString() : WebUtility.HtmlEncode(href ?? string.Empty));
        }
    }

    private static string PlainText(ElementNode element)
    {
        var sb = new StringBuilder();
        foreach (var child in element.Children)
        {
            if (child is TextNode text)
            {
                sb.Append(text.Text);
            }
        }

        return sb.ToString();
    }

    private static bool IsAllowedHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string EncodeText(string text) =>
        WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal);

    private static string Sanitize(string html)
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "br", "div", "span", "strong", "b", "em", "i", "u",
                     "ul", "ol", "li", "a", "h2", "h3", "h4", "blockquote",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("class");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add(Uri.UriSchemeHttp);
        sanitizer.AllowedSchemes.Add(Uri.UriSchemeHttps);

        sanitizer.AllowedCssProperties.Clear();

        return sanitizer.Sanitize(html);
    }

    [GeneratedRegex(@"\[(/?)([a-zA-Z]+)(?:=([^\]]*))?\]", RegexOptions.IgnoreCase)]
    private static partial Regex TagTokenRegex();

    private enum TokenKind
    {
        Text,
        Open,
        Close,
    }

    private sealed class Token
    {
        public TokenKind Kind { get; private init; }

        public string? Text { get; private init; }

        public string? Name { get; private init; }

        public string? AttrValue { get; private init; }

        public static Token OfText(string text) => new() { Kind = TokenKind.Text, Text = text };

        public static Token Open(string name, string? attrValue) =>
            new() { Kind = TokenKind.Open, Name = name, AttrValue = attrValue };

        public static Token Close(string name) => new() { Kind = TokenKind.Close, Name = name };
    }

    private abstract class Node
    {
    }

    private sealed class TextNode(string text) : Node
    {
        public string Text { get; } = text;
    }

    private sealed class ElementNode(string name, string? attrValue) : Node
    {
        public string Name { get; } = name;

        public string? AttrValue { get; } = attrValue;

        public List<Node> Children { get; } = [];
    }
}
