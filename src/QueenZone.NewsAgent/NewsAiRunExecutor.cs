using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsAiRunExecutor(
    INewsAiClient aiClient,
    INewsDiscoveryRepository repository,
    NewsAiBudgetGuard budgetGuard,
    IOptions<OpenRouterOptions> options,
    ILogger<NewsAiRunExecutor> logger)
{
    public const string ModelProvider = "openrouter";

    public bool IsAiEnabled => aiClient.IsEnabled;

    public void BeginRun() => budgetGuard.BeginRun();

    public async Task<NewsAiRunExecutionResult> ExecuteAsync(
        int candidateId,
        NewsAiRunKind kind,
        NewsAiChatRequest request,
        DateTime? runAtUtc = null,
        CancellationToken cancellationToken = default,
        NewsAgentGuidanceSnapshot? guidance = null)
    {
        if (!aiClient.IsEnabled)
        {
            throw new NewsAiDisabledException();
        }

        var utcNow = runAtUtc ?? DateTime.UtcNow;
        budgetGuard.RegisterCandidateAttempt();
        await budgetGuard.EnsureWithinBudgetAsync(utcNow, cancellationToken);

        var modelId = options.Value.ResolveModel(request.ModelRole);
        var recordedGuidance = guidance is { HasRevision: true } ? guidance : null;
        var aiRunId = await repository.CreateAiRunAsync(
            new NewsAiRunCreateRequest(
                candidateId,
                kind,
                ModelProvider,
                modelId,
                request.PromptVersion,
                utcNow,
                recordedGuidance?.RevisionId,
                recordedGuidance?.RevisionNumber,
                recordedGuidance?.ContentHash),
            cancellationToken);

        NewsAiChatCompletion completion;
        try
        {
            completion = await aiClient.CompleteChatAsync(
                request,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await repository.CompleteAiRunAsync(
                aiRunId,
                new NewsAiRunCompletion(
                    NewsAiRunStatus.Failed,
                    null,
                    null,
                    null,
                    null,
                    ex.Message,
                    DateTime.UtcNow),
                cancellationToken);

            throw;
        }

        await repository.CompleteAiRunAsync(
            aiRunId,
            new NewsAiRunCompletion(
                NewsAiRunStatus.Succeeded,
                completion.InputTokens,
                completion.OutputTokens,
                completion.EstimatedCostUsd,
                completion.DryRun ? null : completion.Content,
                null,
                DateTime.UtcNow),
            cancellationToken);

        budgetGuard.RegisterSpend(completion.EstimatedCostUsd);

        logger.LogInformation(
            "AI run {AiRunId} completed for candidate {CandidateId} using model {ModelId}.",
            aiRunId,
            candidateId,
            completion.ModelId);

        return new NewsAiRunExecutionResult(aiRunId, completion);
    }
}
