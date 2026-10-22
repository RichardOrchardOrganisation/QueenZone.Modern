using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class OpenRouterNewsAiClientTests
{
    [Fact]
    public async Task CompleteChatAsync_rejects_oversized_drafting_prompt_instead_of_truncating_evidence()
    {
        var sent = false;
        var client = CreateClient((_, _) =>
        {
            sent = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, new OpenRouterOptions { ApiKey = "test-key", MaxInputTokens = 20 });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteChatAsync(
            new NewsAiChatRequest(NewsAiModelRole.Drafting, NewsDraftPrompt.Version,
                [new NewsAiChatMessage("user", new string('x', 300) + "Brian May's quote at the end.")])));

        Assert.False(sent);
        Assert.Contains("complete source article", error.Message, StringComparison.Ordinal);
        Assert.Contains("MaxInputTokens", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteChatAsync_sends_complete_drafting_prompt_with_late_quotes()
    {
        string? requestBody = null;
        var client = CreateClient((request, _) =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", Encoding.UTF8, "application/json")
            };
        }, new OpenRouterOptions { ApiKey = "test-key", MaxInputTokens = 20_000 });
        var article = new string('x', 40_001) + "Brian May said, “We made this together.”";

        await client.CompleteChatAsync(new NewsAiChatRequest(NewsAiModelRole.Drafting, NewsDraftPrompt.Version,
            [new NewsAiChatMessage("user", article)]));

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal(article, document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteChatAsync_throws_when_api_key_missing()
    {
        var client = CreateClient(
            (_, _) => throw new InvalidOperationException("HTTP should not be called."),
            new OpenRouterOptions());

        await Assert.ThrowsAsync<NewsAiDisabledException>(() => client.CompleteChatAsync(CreateRequest()));
    }

    [Fact]
    public async Task CompleteChatAsync_dry_run_skips_provider_call()
    {
        var client = CreateClient(
            (_, _) => throw new InvalidOperationException("HTTP should not be called."),
            new OpenRouterOptions
            {
                ApiKey = "test-key",
                DryRun = true
            });

        var completion = await client.CompleteChatAsync(CreateRequest());

        Assert.True(completion.DryRun);
        Assert.Equal("openai/gpt-4.1-mini", completion.ModelId);
        Assert.Equal(0, completion.InputTokens);
    }

    [Fact]
    public async Task CompleteChatAsync_parses_successful_provider_response()
    {
        string? requestBody = null;
        const string responseBody = """
            {
              "model": "openai/gpt-4.1-nano",
              "choices": [
                {
                  "message": {
                    "content": "{\"relevant\":true}"
                  }
                }
              ],
              "usage": {
                "prompt_tokens": 120,
                "completion_tokens": 30,
                "cost": 0.0004
              }
            }
            """;

        var client = CreateClient(
            (request, _) =>
            {
                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            },
            new OpenRouterOptions
            {
                ApiKey = "test-key",
                MaxRetryAttempts = 1
            });

        var completion = await client.CompleteChatAsync(CreateRequest());

        Assert.False(completion.DryRun);
        Assert.Equal("{\"relevant\":true}", completion.Content);
        Assert.Equal(120, completion.InputTokens);
        Assert.Equal(30, completion.OutputTokens);
        Assert.Equal(0.0004m, completion.EstimatedCostUsd);

        using var document = JsonDocument.Parse(requestBody!);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("usage", out var usage));
        Assert.True(usage.GetProperty("include").GetBoolean());
        Assert.False(root.TryGetProperty("max_input_tokens", out _));
    }

    [Fact]
    public async Task CompleteChatAsync_trims_messages_to_configured_input_budget()
    {
        string? requestBody = null;
        const string responseBody = """
            {
              "model": "openai/gpt-4.1-nano",
              "choices": [{ "message": { "content": "ok" } }],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1, "cost": 0.0001 }
            }
            """;

        var client = CreateClient(
            (request, _) =>
            {
                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            },
            new OpenRouterOptions
            {
                ApiKey = "test-key",
                MaxInputTokens = 20,
                MaxRetryAttempts = 1
            });

        await client.CompleteChatAsync(new NewsAiChatRequest(
            NewsAiModelRole.Triage,
            "triage-v1",
            [
                new NewsAiChatMessage("system", "Respond with JSON."),
                new NewsAiChatMessage("user", new string('x', 300))
            ]));

        using var document = JsonDocument.Parse(requestBody!);
        var messages = document.RootElement.GetProperty("messages");
        var userContent = messages[1].GetProperty("content").GetString();
        Assert.NotNull(userContent);
        Assert.True(userContent!.Length < 300);
        Assert.Contains("Content truncated", userContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteChatAsync_retries_transient_failures()
    {
        var attempts = 0;
        const string responseBody = """
            {
              "model": "openai/gpt-4.1-nano",
              "choices": [{ "message": { "content": "ok" } }],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1 }
            }
            """;

        var client = CreateClient(
            (_, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            },
            new OpenRouterOptions
            {
                ApiKey = "test-key",
                MaxRetryAttempts = 2
            });

        var completion = await client.CompleteChatAsync(CreateRequest());

        Assert.Equal("ok", completion.Content);
        Assert.Equal(2, attempts);
    }

    private static OpenRouterNewsAiClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler,
        OpenRouterOptions options)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handler));
        return new OpenRouterNewsAiClient(
            httpClient,
            Options.Create(options),
            NullLogger<OpenRouterNewsAiClient>.Instance);
    }

    private static NewsAiChatRequest CreateRequest() =>
        new(
            NewsAiModelRole.Triage,
            "triage-v1",
            [new NewsAiChatMessage("user", "Classify this headline.")]);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }
}
