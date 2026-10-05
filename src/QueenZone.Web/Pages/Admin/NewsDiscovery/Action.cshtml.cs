using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.Web.Pages.Admin.NewsDiscovery;

public sealed class ActionModel(
    INewsDiscoveryRepository discoveryRepository,
    IAdminNewsRepository adminNewsRepository,
    AdminNewsWriteService adminNewsWriteService,
    NewsDraftGenerationService draftGenerationService,
    ILogger<ActionModel> logger) : AdminNewsDiscoveryPageModel
{
    public async Task<IActionResult> OnPostRejectAsync(int id, CancellationToken cancellationToken)
    {
        var candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (candidate is null)
        {
            return NotFound();
        }

        if (!NewsCandidateWorkflow.TryValidateStatusChange(
                candidate.Status,
                NewsCandidateStatus.Rejected,
                out var transitionError))
        {
            return RedirectToReview(id, transitionError);
        }

        var updated = await discoveryRepository.TryUpdateCandidateStatusAsync(
            id,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.Rejected,
                ReviewNotes: $"Marked not relevant by {EditorEmail}."),
            cancellationToken);
        if (!updated)
        {
            return RedirectToReview(id, "The candidate could not be marked as rejected.");
        }

        return Redirect("/admin/news-discovery");
    }

    public async Task<IActionResult> OnPostIgnoreDuplicateAsync(int id, CancellationToken cancellationToken)
    {
        var candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (candidate is null)
        {
            return NotFound();
        }

        if (!NewsCandidateWorkflow.TryValidateStatusChange(
                candidate.Status,
                NewsCandidateStatus.IgnoredDuplicate,
                out var transitionError))
        {
            return RedirectToReview(id, transitionError);
        }

        var duplicateOf = candidate.DuplicateOfCandidateId
            ?? (await discoveryRepository.FindEarlierDuplicateCandidateAsync(
                candidate.Id,
                candidate.SourceTitle,
                candidate.ContentHash,
                cancellationToken))?.Id;

        var updated = await discoveryRepository.TryUpdateCandidateStatusAsync(
            id,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.IgnoredDuplicate,
                ReviewNotes: $"Ignored as duplicate by {EditorEmail}.",
                DuplicateOfCandidateId: duplicateOf),
            cancellationToken);
        if (!updated)
        {
            return RedirectToReview(id, "The candidate could not be marked as a duplicate.");
        }

        return Redirect("/admin/news-discovery");
    }

    public async Task<IActionResult> OnPostPromoteAsync(int id, CancellationToken cancellationToken)
    {
        var candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (candidate is null)
        {
            return NotFound();
        }

        var agentDraft = await discoveryRepository.GetDraftByCandidateIdAsync(id, cancellationToken);
        if (agentDraft is null)
        {
            return RedirectToReview(id, "Generate or save a draft before promoting this candidate.");
        }

        var promoteReadinessError = NewsCandidateWorkflow.GetPromoteReadinessError(candidate.Status);
        if (!string.IsNullOrEmpty(promoteReadinessError))
        {
            return RedirectToReview(id, promoteReadinessError);
        }

        if (candidate.Status == NewsCandidateStatus.NeedsReview)
        {
            if (!NewsCandidateWorkflow.TryValidateStatusChange(
                    candidate.Status,
                    NewsCandidateStatus.Drafted,
                    out var draftedError))
            {
                return RedirectToReview(id, draftedError);
            }

            var drafted = await discoveryRepository.TryUpdateCandidateStatusAsync(
                id,
                new NewsCandidateStatusUpdate(
                    NewsCandidateStatus.Drafted,
                    ReviewNotes: $"Draft acknowledged by {EditorEmail} before promotion."),
                cancellationToken);
            if (!drafted)
            {
                return RedirectToReview(id, "The candidate could not be marked as drafted before promotion.");
            }

            candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        }

        if (candidate is null || !NewsCandidateWorkflow.CanPromoteToArticle(candidate.Status))
        {
            return RedirectToReview(
                id,
                candidate is null
                    ? "The candidate is no longer available."
                    : NewsCandidateWorkflow.GetPromoteReadinessError(candidate.Status));
        }

        var adminDraft = NewsDiscoveryPromoteDraft.Build(agentDraft, candidate);
        var slugInUse = await adminNewsRepository.IsSlugInUseAsync(
            NewsSlug.Resolve(adminDraft.Title, adminDraft.Slug),
            cancellationToken: cancellationToken);
        var validationErrors = NewsValidation.ValidateDraft(adminDraft, slugInUse);
        if (validationErrors.Count > 0)
        {
            return RedirectToReview(id, string.Join(" ", validationErrors));
        }

        int newsId;
        try
        {
            newsId = await adminNewsWriteService.PromoteDiscoveryCandidateAsync(
                candidate,
                agentDraft,
                adminDraft,
                EditorEmail,
                cancellationToken);
        }
        catch (AdminNewsPromotionException ex)
        {
            return RedirectToReview(id, ex.Message);
        }
        catch (OptimisticConcurrencyException)
        {
            return RedirectToReview(id, OptimisticConcurrencyException.UserMessage);
        }

        return Redirect($"/admin/news/{newsId}/edit");
    }

    public async Task<IActionResult> OnPostRegenerateDraftAsync(int id, CancellationToken cancellationToken)
    {
        var candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (candidate is null)
        {
            return NotFound();
        }

        if (!draftGenerationService.IsAiEnabled)
        {
            TempData["DiscoveryMessage"] = "Draft generation requires OpenRouter configuration on the web app.";
            TempData["DiscoveryMessageKind"] = "error";
            return Redirect($"/admin/news-discovery/{id}");
        }

        if (candidate.Status is NewsCandidateStatus.Discovered or NewsCandidateStatus.Rejected)
        {
            var prepared = await discoveryRepository.TryUpdateCandidateStatusAsync(
                id,
                new NewsCandidateStatusUpdate(
                    NewsCandidateStatus.NeedsReview,
                    ReviewNotes: $"Queued for AI draft generation by {EditorEmail}."),
                cancellationToken);

            if (!prepared)
            {
                TempData["DiscoveryMessage"] = "The candidate could not be moved into review before draft generation.";
                TempData["DiscoveryMessageKind"] = "error";
                return Redirect($"/admin/news-discovery/{id}");
            }

            candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
            if (candidate is null)
            {
                return NotFound();
            }
        }

        var draftGenerationError = NewsCandidateWorkflow.GetDraftGenerationError(candidate.Status);
        if (!string.IsNullOrEmpty(draftGenerationError))
        {
            TempData["DiscoveryMessage"] = draftGenerationError;
            TempData["DiscoveryMessageKind"] = "error";
            return Redirect($"/admin/news-discovery/{id}");
        }

        await GenerateCandidateDraftAsync(candidate, cancellationToken);

        return Redirect($"/admin/news-discovery/{id}");
    }

    private async Task GenerateCandidateDraftAsync(NewsCandidate candidate, CancellationToken cancellationToken)
    {
        var id = candidate.Id;
        try
        {
            var hadDraft = await discoveryRepository.GetDraftByCandidateIdAsync(id, cancellationToken) is not null;
            var operationName = hadDraft ? "regeneration" : "generation";
            var result = await draftGenerationService.GenerateDraftAsync(
                candidate,
                new NewsDraftRunOptions(ForceRegenerate: true, BypassConfidenceThreshold: true),
                cancellationToken);

            if (result.Succeeded && result.DraftId is not null)
            {
                TempData["DiscoveryMessage"] = hadDraft
                    ? "Draft regenerated successfully."
                    : "Draft generated successfully.";
                TempData["DiscoveryMessageKind"] = "success";
            }
            else
            {
                TempData["DiscoveryMessage"] = $"Draft {operationName} did not produce a new draft.";
                TempData["DiscoveryMessageKind"] = "error";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Draft generation failed for discovery candidate {CandidateId}", id);
            var hadDraft = await discoveryRepository.GetDraftByCandidateIdAsync(id, cancellationToken) is not null;
            var operationName = hadDraft ? "regeneration" : "generation";
            TempData["DiscoveryMessage"] = $"Draft {operationName} failed: {ex.Message}";
            TempData["DiscoveryMessageKind"] = "error";
        }

    }

    private IActionResult RedirectToReview(int id, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            TempData["DiscoveryMessage"] = message;
            TempData["DiscoveryMessageKind"] = "error";
        }

        return Redirect($"/admin/news-discovery/{id}");
    }
}
