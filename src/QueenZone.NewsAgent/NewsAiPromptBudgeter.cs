namespace QueenZone.NewsAgent;

internal static class NewsAiPromptBudgeter
{
    private const int ApproximateCharactersPerToken = 4;
    private const string TruncationNotice = "\n\n[Content truncated to fit the configured input budget.]";

    public static IReadOnlyList<NewsAiChatMessage> RequireCompletePrompt(
        IReadOnlyList<NewsAiChatMessage> messages,
        int maxInputTokens)
    {
        if (messages.Sum(message => (long)message.Content.Length) > (long)maxInputTokens * ApproximateCharactersPerToken)
        {
            throw new InvalidOperationException(
                "The complete source article and editorial guidance exceed OpenRouter MaxInputTokens. Increase the configured input budget; drafting will not silently truncate source evidence.");
        }
        return messages;
    }

    public static IReadOnlyList<NewsAiChatMessage> TrimToApproximateTokenBudget(
        IReadOnlyList<NewsAiChatMessage> messages,
        int maxInputTokens)
    {
        var maxCharacters = Math.Max(1, maxInputTokens) * ApproximateCharactersPerToken;
        var totalCharacters = messages.Sum(message => message.Content.Length);
        if (totalCharacters <= maxCharacters)
        {
            return messages;
        }

        var trimmed = messages
            .Select(message => new NewsAiChatMessage(message.Role, message.Content))
            .ToArray();
        var overflow = totalCharacters - maxCharacters;

        for (var index = trimmed.Length - 1; index >= 0 && overflow > 0; index--)
        {
            var message = trimmed[index];
            var minimumLength = message.Role.Equals("system", StringComparison.OrdinalIgnoreCase)
                ? Math.Min(message.Content.Length, 1_000)
                : 0;
            var removable = message.Content.Length - minimumLength;
            if (removable <= 0)
            {
                continue;
            }

            var remove = Math.Min(removable, overflow + TruncationNotice.Length);
            var keepLength = Math.Max(minimumLength, message.Content.Length - remove);
            var content = message.Content[..keepLength].TrimEnd();
            if (!content.EndsWith(TruncationNotice, StringComparison.Ordinal))
            {
                content += TruncationNotice;
            }

            trimmed[index] = message with { Content = content };
            overflow = trimmed.Sum(item => item.Content.Length) - maxCharacters;
        }

        return trimmed;
    }
}
