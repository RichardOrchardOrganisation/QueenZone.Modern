using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsDraftQuotePolicyTests
{
    [Fact]
    public void Enforce_keeps_evidence_backed_quotes_and_strips_unverified_marks()
    {
        var evidence = CreateEvidence("Brian said \"We love the fans\" at the show.");
        var draft = new NewsDraftStructuredResult(
            "Title",
            "slug",
            "Excerpt",
            "Brian May said \"We love the fans\" while also saying \"invented quote here\".",
            [],
            ["https://example.com/story"],
            ["Example"],
            null,
            null,
            null,
            null,
            false,
            [
                new NewsDraftPreservedQuote(
                    "Brian May",
                    "We love the fans",
                    "https://example.com/story",
                    null),
                new NewsDraftPreservedQuote(
                    "Brian May",
                    "invented quote here",
                    "https://example.com/story",
                    null)
            ]);

        var enforced = NewsDraftQuotePolicy.Enforce(draft, evidence);

        Assert.Single(enforced.PreservedQuotes);
        Assert.Equal("We love the fans", enforced.PreservedQuotes[0].ExactText);
        Assert.Contains("\"We love the fans\"", enforced.Body, StringComparison.Ordinal);
        Assert.Contains("invented quote here", enforced.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"invented quote here\"", enforced.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Enforce_keeps_evidence_backed_quote_from_artist_about_Queen_member()
    {
        var quote = "Brian came in and played a fantastic solo";
        var evidence = CreateEvidence($"Tony Iommi said, \"{quote}\".");
        var draft = new NewsDraftStructuredResult(
            "Iommi discusses Brian May guest appearance",
            "iommi-brian-may-guest-appearance",
            "Tony Iommi has discussed Brian May's contribution to his album.",
            $"Iommi said \"{quote}\" when discussing May's guest appearance.",
            ["Brian May", "Tony Iommi"],
            ["https://example.com/story"],
            ["Example"],
            null,
            null,
            null,
            null,
            true,
            [
                new NewsDraftPreservedQuote(
                    "Tony Iommi",
                    quote,
                    "https://example.com/story",
                    "Discussing Brian May's guest solo")
            ]);

        var enforced = NewsDraftQuotePolicy.Enforce(draft, evidence);

        var preserved = Assert.Single(enforced.PreservedQuotes);
        Assert.Equal("Tony Iommi", preserved.Speaker);
        Assert.Equal(quote, preserved.ExactText);
        Assert.Contains($"\"{quote}\"", enforced.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_includes_preserved_quotes_and_prompt_is_draft_v4()
    {
        var json = """
            {
              "title": "Queen announce 2026 tour",
              "slug": "queen-announce-2026-tour",
              "excerpt": "Queen have announced new 2026 tour dates.",
              "body": "Brian May said \"We love the fans\".",
              "related_entities": ["Queen"],
              "source_urls": ["https://www.queenonline.com/news/tour-2026"],
              "source_names": ["Queen Online"],
              "attribution_text": "Source: Queen Online",
              "confidence_notes": "Primary official source.",
              "source_notes": "Official Queen Online announcement.",
              "suggested_publish_at": "2026-07-02T10:00:00Z",
              "secondary_source_warning": false,
              "preserved_quotes": [
                {
                  "speaker": "Brian May",
                  "exact_text": "We love the fans",
                  "source_url": "https://www.queenonline.com/news/tour-2026",
                  "source_context": "closing remarks"
                }
              ]
            }
            """;

        var parsed = NewsDraftResultParser.Parse(json);

        Assert.Equal("draft-v5", NewsDraftPrompt.Version);
        Assert.Single(parsed.PreservedQuotes);
        Assert.Equal("Brian May", parsed.PreservedQuotes[0].Speaker);
        Assert.Equal("We love the fans", parsed.PreservedQuotes[0].ExactText);
        Assert.Contains("Quote policy", NewsDraftPrompt.BuildMessages(
            CreateCandidate(),
            CreateSource(),
            CreateEvidence("We love the fans"))[0].Content, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<NewsCandidateEvidence> CreateEvidence(string excerpt) =>
    [
        new(
            1,
            7,
            "https://example.com/story",
            "https://example.com/story",
            "Example",
            NewsDiscoveryTrustTier.Secondary,
            "Story",
            null,
            excerpt,
            null,
            DateTime.UtcNow,
            null,
            DateTime.UtcNow)
    ];

    private static NewsCandidate CreateCandidate() =>
        new(
            7,
            3,
            "https://example.com/story",
            "https://example.com/story",
            "hash",
            "Story",
            null,
            DateTime.UtcNow,
            null,
            NewsCandidateStatus.NeedsReview,
            0.9m,
            0.9m,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "example",
            "Example",
            NewsDiscoveryTrustTier.Secondary);

    private static NewsDiscoverySource CreateSource() =>
        new(
            3,
            "example",
            "Example",
            "https://example.com/",
            null,
            NewsDiscoverySourceType.AllowlistedPage,
            NewsDiscoveryTrustTier.Secondary,
            60,
            true,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);
}
