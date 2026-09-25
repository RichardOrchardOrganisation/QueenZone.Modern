using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class SearchResultHighlighterTests
{
    [Fact]
    public void Highlight_wraps_matching_terms_in_mark_tags()
    {
        var html = SearchResultHighlighter.Highlight("Queen modernisation begins", "modernisation");

        Assert.Equal("Queen <mark>modernisation</mark> begins", html.ToString());
    }

    [Fact]
    public void Highlight_returns_encoded_summary_without_marks_when_match_times_out()
    {
        var encoded = new string('a', 40) + "b";

        var result = SearchResultHighlighter.ReplaceHighlighted(
            encoded,
            "(a+)+$",
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(encoded, result);
        Assert.DoesNotContain("<mark>", result, StringComparison.Ordinal);
    }
}
