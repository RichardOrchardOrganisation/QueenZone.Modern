namespace QueenZone.NewsAgent;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string? ApiKey { get; set; }

    public string TriageModel { get; set; } = "openai/gpt-4.1-mini";

    public string DraftingModel { get; set; } = "openai/gpt-4.1-mini";

    public string FallbackModel { get; set; } = "deepseek/deepseek-chat-v3-0324";

    public int MaxInputTokens { get; set; } = 8_000;

    public int MaxOutputTokens { get; set; } = 1_500;

    public int PerRunCandidateLimit { get; set; } = 25;

    public decimal PerRunBudgetUsd { get; set; } = 0.50m;

    public decimal DailyBudgetUsd { get; set; } = 2.00m;

    public bool DryRun { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int MaxRetryAttempts { get; set; } = 3;

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string SiteUrl { get; set; } = "https://queenzone.com";

    public string AppName { get; set; } = "QueenZone News Agent";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static string? NormalizeApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var normalized = apiKey.Trim().Trim('"').Trim('\'').Trim();
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Bearer ".Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public string ResolveModel(NewsAiModelRole role) =>
        role switch
        {
            NewsAiModelRole.Triage => TriageModel,
            NewsAiModelRole.Drafting => DraftingModel,
            NewsAiModelRole.Fallback => FallbackModel,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown AI model role.")
        };

    public void Validate()
    {
        if (MaxInputTokens <= 0)
        {
            throw new InvalidOperationException("OpenRouter MaxInputTokens must be greater than zero.");
        }

        if (MaxOutputTokens <= 0)
        {
            throw new InvalidOperationException("OpenRouter MaxOutputTokens must be greater than zero.");
        }

        if (PerRunCandidateLimit <= 0)
        {
            throw new InvalidOperationException("OpenRouter PerRunCandidateLimit must be greater than zero.");
        }

        if (PerRunBudgetUsd < 0)
        {
            throw new InvalidOperationException("OpenRouter PerRunBudgetUsd cannot be negative.");
        }

        if (DailyBudgetUsd < 0)
        {
            throw new InvalidOperationException("OpenRouter DailyBudgetUsd cannot be negative.");
        }

        if (RequestTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("OpenRouter RequestTimeoutSeconds must be greater than zero.");
        }

        if (MaxRetryAttempts < 1)
        {
            throw new InvalidOperationException("OpenRouter MaxRetryAttempts must be at least one.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException("OpenRouter BaseUrl is required.");
        }

        if (IsConfigured)
        {
            if (string.IsNullOrWhiteSpace(TriageModel))
            {
                throw new InvalidOperationException("OpenRouter TriageModel is required when an API key is configured.");
            }

            if (string.IsNullOrWhiteSpace(DraftingModel))
            {
                throw new InvalidOperationException("OpenRouter DraftingModel is required when an API key is configured.");
            }

            if (string.IsNullOrWhiteSpace(FallbackModel))
            {
                throw new InvalidOperationException("OpenRouter FallbackModel is required when an API key is configured.");
            }
        }
    }
}
