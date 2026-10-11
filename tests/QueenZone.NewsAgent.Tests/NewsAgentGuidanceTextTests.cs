using QueenZone.Data;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsAgentGuidanceTextTests
{
    [Fact]
    public void Sanitize_strips_control_characters_except_newlines_and_tabs()
    {
        var sanitized = NewsAgentGuidanceText.Sanitize("prefer\tshort\r\nsummaries\u0001please");

        Assert.Equal("prefer\tshort\r\nsummariesplease", sanitized);
    }

    [Fact]
    public void Sanitize_preserves_guidance_beyond_the_former_character_limit()
    {
        var content = new string('a', 40_001) + "Always preserve band member quotes.";

        Assert.Equal(content, NewsAgentGuidanceText.Sanitize(content));
    }

    [Fact]
    public void ComputeContentHash_is_lowercase_sha256_hex()
    {
        var hash = NewsAgentGuidanceText.ComputeContentHash("prefer short summaries");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Equal(NewsAgentGuidanceText.ComputeContentHash("prefer short summaries"), hash);
        Assert.NotEqual(hash, NewsAgentGuidanceText.ComputeContentHash("Prefer short summaries"));
    }
}
