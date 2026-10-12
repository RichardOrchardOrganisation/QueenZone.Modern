using Microsoft.AspNetCore.OutputCaching;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin.Photos;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web;

/// <summary>Outcome of an editor decision; <see cref="Message"/> is safe to show to the editor.</summary>
public sealed record PhotoCorrectionReviewResult(bool Succeeded, string Message);

/// <summary>
/// Applies editor decisions on photo correction proposals. Only caption approvals change the
/// photograph today: the caption is written with a compare-and-swap on the values the editor
/// saw, so a stale proposal or a concurrent edit is refused instead of overwriting newer work.
/// Event-date and photographer/source approvals are recorded as accepted for editorial
/// follow-up because the archive has no field to hold them yet.
/// </summary>
public sealed class PhotoCorrectionReviewService(
    IPhotoCorrectionRepository corrections,
    IAdminPhotoRepository photos,
    PublicQueryCacheService publicQueryCache,
    CoreSitemapService coreSitemapService,
    IOutputCacheStore outputCacheStore)
{
    public async Task<PhotoCorrectionReviewResult> ApproveAsync(
        Guid id,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken)
    {
        var proposal = await corrections.GetByIdAsync(id, cancellationToken);
        if (proposal is null)
        {
            return new PhotoCorrectionReviewResult(false, "Proposal not found.");
        }

        if (!PhotoCorrectionStatus.IsOpen(proposal.Status))
        {
            return new PhotoCorrectionReviewResult(false, "Only open proposals can be approved.");
        }

        if (proposal.Kind != PhotoCorrectionKind.Caption)
        {
            return await RecordAsync(
                () => corrections.ApproveAsync(id, editorEmail, reviewNotes, null, cancellationToken),
                "Accepted and recorded. This field is not shown on the site yet, so no photo was changed.");
        }

        var photo = await photos.GetByIdAsync(proposal.PicId, cancellationToken);
        if (photo is null || !photo.IsVisible)
        {
            return new PhotoCorrectionReviewResult(false, "The photo is hidden or deleted, so the caption was not changed.");
        }

        if (!string.Equals(photo.Title, proposal.CurrentValue, StringComparison.Ordinal))
        {
            return new PhotoCorrectionReviewResult(
                false,
                "The caption has changed since this was proposed. Review the current caption, then reject this proposal or ask the member to resubmit.");
        }

        var previousTitle = photo.Title;
        var applied = await TryUpdateTitleAsync(photo, proposal.ProposedValue, editorEmail, cancellationToken);
        if (!applied.Succeeded)
        {
            return applied;
        }

        try
        {
            await corrections.ApproveAsync(id, editorEmail, reviewNotes, previousTitle, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Someone decided the proposal first. Put the caption back so the photo and the
            // trail agree.
            var current = await photos.GetByIdAsync(proposal.PicId, cancellationToken);
            if (current is not null)
            {
                await TryUpdateTitleAsync(current, previousTitle, editorEmail, cancellationToken);
            }

            return new PhotoCorrectionReviewResult(false, ex.Message);
        }

        return new PhotoCorrectionReviewResult(true, "Approved. The caption is updated on the site.");
    }

    public async Task<PhotoCorrectionReviewResult> RevertAsync(
        Guid id,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken)
    {
        var proposal = await corrections.GetByIdAsync(id, cancellationToken);
        if (proposal is null)
        {
            return new PhotoCorrectionReviewResult(false, "Proposal not found.");
        }

        if (proposal.Status != PhotoCorrectionStatus.Approved)
        {
            return new PhotoCorrectionReviewResult(false, "Only approved proposals can be reverted.");
        }

        if (proposal.AppliedFromValue is null)
        {
            return await RecordAsync(
                () => corrections.RevertAsync(id, editorEmail, reviewNotes, cancellationToken),
                "Reverted. Nothing had been applied to the photo.");
        }

        var photo = await photos.GetByIdAsync(proposal.PicId, cancellationToken);
        if (photo is null)
        {
            return new PhotoCorrectionReviewResult(false, "The photo no longer exists.");
        }

        if (!string.Equals(photo.Title, proposal.ProposedValue, StringComparison.Ordinal))
        {
            return new PhotoCorrectionReviewResult(
                false,
                "The caption has been edited since this was applied, so it was not reverted automatically. Edit the photo directly.");
        }

        var restored = await TryUpdateTitleAsync(photo, proposal.AppliedFromValue, editorEmail, cancellationToken);
        if (!restored.Succeeded)
        {
            return restored;
        }

        return await RecordAsync(
            () => corrections.RevertAsync(id, editorEmail, reviewNotes, cancellationToken),
            "Reverted. The previous caption is back on the site.");
    }

    private async Task<PhotoCorrectionReviewResult> TryUpdateTitleAsync(
        AdminPhotoItem photo,
        string newTitle,
        string editorEmail,
        CancellationToken cancellationToken)
    {
        try
        {
            await photos.UpdateAsync(
                photo.PicId,
                new AdminPhotoUpdateRequest(newTitle, photo.Keywords, photo.Year, photo.DateTime, photo.CatId),
                editorEmail,
                new AdminPhotoConcurrencyToken(
                    photo.Title,
                    photo.Keywords,
                    photo.Year,
                    photo.DateTime,
                    photo.CatId,
                    photo.IsVisible),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new PhotoCorrectionReviewResult(false, ex.Message);
        }

        await ActionModel.InvalidatePublicPhotoCachesAsync(
            publicQueryCache,
            coreSitemapService,
            outputCacheStore,
            cancellationToken);
        return new PhotoCorrectionReviewResult(true, string.Empty);
    }

    private static async Task<PhotoCorrectionReviewResult> RecordAsync(
        Func<Task<PhotoCorrectionProposal?>> decide,
        string successMessage)
    {
        try
        {
            return await decide() is null
                ? new PhotoCorrectionReviewResult(false, "Proposal not found.")
                : new PhotoCorrectionReviewResult(true, successMessage);
        }
        catch (InvalidOperationException ex)
        {
            return new PhotoCorrectionReviewResult(false, ex.Message);
        }
    }
}
