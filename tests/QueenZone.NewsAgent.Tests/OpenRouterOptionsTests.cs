using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class OpenRouterOptionsTests
{
    [Fact]
    public void Validate_allows_missing_api_key_for_fetch_only_mode()
    {
        var options = new OpenRouterOptions();

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Validate_requires_model_ids_when_api_key_is_configured()
    {
        var options = new OpenRouterOptions
        {
            ApiKey = "test-key",
            TriageModel = ""
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("TriageModel", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveModel_returns_configured_defaults()
    {
        var options = new OpenRouterOptions();

        Assert.Equal("openai/gpt-4.1-mini", options.ResolveModel(NewsAiModelRole.Triage));
        Assert.Equal("openai/gpt-4.1-mini", options.ResolveModel(NewsAiModelRole.Drafting));
        Assert.Equal("deepseek/deepseek-chat-v3-0324", options.ResolveModel(NewsAiModelRole.Fallback));
    }

    [Fact]
    public void Validate_rejects_non_positive_limits()
    {
        var options = new OpenRouterOptions
        {
            PerRunCandidateLimit = 0
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("PerRunCandidateLimit", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  sk-or-v1-test  ", "sk-or-v1-test")]
    [InlineData("\"sk-or-v1-test\"", "sk-or-v1-test")]
    [InlineData("'sk-or-v1-test'", "sk-or-v1-test")]
    [InlineData("Bearer sk-or-v1-test", "sk-or-v1-test")]
    [InlineData("  \"sk-or-v1-test\"  ", "sk-or-v1-test")]
    public void NormalizeApiKey_trims_whitespace_quotes_and_bearer_prefix(string input, string expected)
    {
        Assert.Equal(expected, OpenRouterOptions.NormalizeApiKey(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"   \"")]
    public void NormalizeApiKey_returns_null_for_blank_values(string? input)
    {
        Assert.Null(OpenRouterOptions.NormalizeApiKey(input));
    }
}
