using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QueenZone.NewsAgent;

public sealed class OpenRouterNewsAiClient(
    HttpClient httpClient,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterNewsAiClient> logger) : INewsAiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsEnabled => options.Value.IsConfigured;

    public async Task<NewsAiChatCompletion> CompleteChatAsync(
        NewsAiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new NewsAiDisabledException();
        }

        settings.Validate();

        var modelId = settings.ResolveModel(request.ModelRole);
        var maxOutputTokens = request.MaxOutputTokens ?? settings.MaxOutputTokens;
        var messages = request.ModelRole == NewsAiModelRole.Drafting
            ? NewsAiPromptBudgeter.RequireCompletePrompt(request.Messages, settings.MaxInputTokens)
            : NewsAiPromptBudgeter.TrimToApproximateTokenBudget(request.Messages, settings.MaxInputTokens);
        var payload = new OpenRouterChatCompletionRequest(
            modelId,
            messages
                .Select(message => new OpenRouterChatMessage(message.Role, message.Content))
                .ToList(),
            Math.Min(maxOutputTokens, settings.MaxOutputTokens),
            new OpenRouterUsageRequest(true));

        if (settings.DryRun)
        {
            logger.LogInformation(
                "OpenRouter dry-run for model {ModelId}, prompt version {PromptVersion}, message count {MessageCount}.",
                modelId,
                request.PromptVersion,
                request.Messages.Count);

            return new NewsAiChatCompletion(
                string.Empty,
                modelId,
                0,
                0,
                0m,
                DryRun: true);
        }

        logger.LogInformation(
            "Calling OpenRouter model {ModelId} for prompt version {PromptVersion}.",
            modelId,
            request.PromptVersion);

        var responseBody = await SendWithRetryAsync(payload, cancellationToken);
        var parsed = OpenRouterChatCompletionParser.Parse(responseBody, modelId);

        logger.LogInformation(
            "OpenRouter completed model {ModelId}: input tokens={InputTokens}, output tokens={OutputTokens}, estimated cost={EstimatedCostUsd}.",
            parsed.ModelId,
            parsed.InputTokens,
            parsed.OutputTokens,
            parsed.EstimatedCostUsd);

        return parsed;
    }

    private async Task<string> SendWithRetryAsync(
        OpenRouterChatCompletionRequest payload,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var attempts = settings.MaxRetryAttempts;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                request.Headers.TryAddWithoutValidation("HTTP-Referer", settings.SiteUrl);
                request.Headers.TryAddWithoutValidation("X-Title", settings.AppName);
                request.Content = JsonContent.Create(payload, options: SerializerOptions);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (!IsTransientStatusCode(response.StatusCode) || attempt == attempts)
                {
                    throw new HttpRequestException(
                        $"OpenRouter request failed with status {(int)response.StatusCode} ({response.StatusCode}): {body}");
                }

                lastException = new HttpRequestException(
                    $"OpenRouter transient failure {(int)response.StatusCode} ({response.StatusCode}): {body}");
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt < attempts)
            {
                lastException = ex;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            logger.LogWarning(
                lastException,
                "OpenRouter request attempt {Attempt} of {MaxAttempts} failed; retrying in {DelaySeconds} seconds.",
                attempt,
                attempts,
                delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
        }

        throw lastException ?? new HttpRequestException("OpenRouter request failed after retries.");
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout;

    private static bool IsTransientException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException;

    private sealed record OpenRouterChatCompletionRequest(
        string Model,
        IReadOnlyList<OpenRouterChatMessage> Messages,
        int MaxTokens,
        OpenRouterUsageRequest Usage);

    private sealed record OpenRouterUsageRequest(bool Include);

    private sealed record OpenRouterChatMessage(string Role, string Content);
}
