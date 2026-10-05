using System.Text.Json;

namespace QueenZone.NewsAgent;

internal static class OpenRouterChatCompletionParser
{
    public static NewsAiChatCompletion Parse(string responseBody, string requestedModelId)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        var modelId = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? requestedModelId
            : requestedModelId;

        var content = string.Empty;
        if (root.TryGetProperty("choices", out var choicesElement)
            && choicesElement.ValueKind == JsonValueKind.Array
            && choicesElement.GetArrayLength() > 0)
        {
            var firstChoice = choicesElement[0];
            if (firstChoice.TryGetProperty("message", out var messageElement)
                && messageElement.TryGetProperty("content", out var contentElement))
            {
                content = contentElement.GetString() ?? string.Empty;
            }
        }

        var (inputTokens, outputTokens, estimatedCostUsd) = ParseUsage(root);

        return new NewsAiChatCompletion(
            content,
            modelId,
            inputTokens,
            outputTokens,
            estimatedCostUsd,
            DryRun: false);
    }
    private static (int InputTokens, int OutputTokens, decimal? EstimatedCostUsd) ParseUsage(JsonElement root)
    {
        var inputTokens = 0;
        var outputTokens = 0;
        decimal? estimatedCostUsd = null;

        if (root.TryGetProperty("usage", out var usageElement))
        {
            if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement)
                && promptTokensElement.TryGetInt32(out var promptTokens))
            {
                inputTokens = promptTokens;
            }

            if (usageElement.TryGetProperty("completion_tokens", out var completionTokensElement)
                && completionTokensElement.TryGetInt32(out var completionTokens))
            {
                outputTokens = completionTokens;
            }

            if (usageElement.TryGetProperty("cost", out var costElement)
                && costElement.TryGetDecimal(out var cost))
            {
                estimatedCostUsd = cost;
            }
            else if (usageElement.TryGetProperty("total_cost", out var totalCostElement)
                && totalCostElement.TryGetDecimal(out var totalCost))
            {
                estimatedCostUsd = totalCost;
            }
        }

        return (inputTokens, outputTokens, estimatedCostUsd);
    }

}
