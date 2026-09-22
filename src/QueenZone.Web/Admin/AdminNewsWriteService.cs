using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Shared admin news publish path. Dispatch and News-forum topic create fire only on
/// unpublished → published. Topic create is fail-open, like push.
/// Also owns discovery-to-admin-draft promotion (SQL transaction when a DbContext is registered).
/// </summary>
public sealed class AdminNewsWriteService(
    IAdminNewsRepository adminNewsRepository,
    INotificationDispatcher notificationDispatcher,
    INewsForumTopicService newsForumTopicService,
    ILogger<AdminNewsWriteService> logger,
    INewsDiscoveryRepository? discoveryRepository = null,
    INewsAuditRepository? auditRepository = null,
    IServiceProvider? serviceProvider = null)
{
    public async Task PublishAsync(
        AdminNewsArticle article,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var firstPublish = !article.IsPublished;
        await adminNewsRepository.PublishAsync(article.Id, editorEmail, article.UpdatedAt, cancellationToken);
        if (!firstPublish)
        {
            return;
        }

        try
        {
            await newsForumTopicService.EnsureTopicOnFirstPublishAsync(article, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "News forum topic create failed after news publish {NewsId} for category {Category}: {Error}",
                article.Id,
                NewsForumDiscussion.CategoryName,
                ex.Message);
        }

        try
        {
            await notificationDispatcher.NotifyNewsPublishedAsync(
                article.Id,
                article.Title,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Push dispatch failed after news publish {NewsId} for category {Category}: {Error}",
                article.Id,
                NotificationCategoryNames.News,
                ex.Message);
        }
    }

    public async Task<int> PromoteDiscoveryCandidateAsync(
        NewsCandidate candidate,
        NewsAgentDraft agentDraft,
        AdminNewsDraft adminDraft,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(agentDraft);
        ArgumentNullException.ThrowIfNull(adminDraft);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);
        ArgumentNullException.ThrowIfNull(discoveryRepository);
        ArgumentNullException.ThrowIfNull(auditRepository);

        var promotionStage = "creating the admin draft";
        try
        {
            return await QueenZoneDbTransactions.ExecuteAsync(
                serviceProvider,
                ct => PromoteDiscoveryCoreAsync(
                    candidate,
                    agentDraft,
                    adminDraft,
                    editorEmail,
                    stage => promotionStage = stage,
                    ct),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AdminNewsPromotionException
            and not OptimisticConcurrencyException)
        {
            logger.LogError(
                ex,
                "Failed while {PromotionStage} for discovery candidate {CandidateId}",
                promotionStage,
                candidate.Id);
            throw new AdminNewsPromotionException(
                $"Promotion failed while {promotionStage}. Check the app logs for the exact validation or database error.");
        }
    }

    private async Task<int> PromoteDiscoveryCoreAsync(
        NewsCandidate candidate,
        NewsAgentDraft agentDraft,
        AdminNewsDraft adminDraft,
        string editorEmail,
        Action<string> setStage,
        CancellationToken ct)
    {
        var promotedNewsId = await adminNewsRepository.CreateDraftAsync(adminDraft, editorEmail, ct);

        if (!NewsCandidateWorkflow.TryValidateStatusChange(
                candidate.Status,
                NewsCandidateStatus.PromotedToArticle,
                out var promoteError))
        {
            throw new AdminNewsPromotionException(
                promoteError ?? "The candidate cannot be promoted from its current status.");
        }

        setStage("updating the discovery candidate");
        var promoted = await discoveryRepository!.TryUpdateCandidateStatusAsync(
            candidate.Id,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.PromotedToArticle,
                ReviewNotes: $"Promoted to admin news draft #{promotedNewsId} by {editorEmail} at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.",
                PromotedNewsId: promotedNewsId),
            ct);
        if (!promoted)
        {
            throw new AdminNewsPromotionException("Promotion failed while updating the discovery candidate.");
        }

        setStage("loading the discovery audit provenance");
        var aiRuns = await discoveryRepository.GetAiRunsForCandidateAsync(candidate.Id, ct);
        var provenance = NewsDiscoveryProvenanceBuilder.Build(candidate, agentDraft, aiRuns);

        setStage("recording the promotion audit");
        await auditRepository!.AppendAsync(
            promotedNewsId,
            "promote-from-discovery",
            editorEmail,
            NewsDiscoveryPromoteAudit.Format(provenance),
            ct);

        setStage("committing the promotion");
        return promotedNewsId;
    }
}
