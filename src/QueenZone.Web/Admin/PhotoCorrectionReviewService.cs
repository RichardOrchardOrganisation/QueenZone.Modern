using Microsoft.AspNetCore.OutputCaching;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin.Photos;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web;

/// <summary>Outcome of an editor decision; <see cref="Message"/> is safe to show to the editor.</summary>
public sealed record PhotoCorrectionReviewResult(bool Succeeded, string Message);

/// <summary>
/// Applies editor decisions on photo correction proposals. Captions, event dates and photograph
/// dates change the photograph itself: each is written with a compare-and-swap on the value the
/// editor saw, so a stale proposal or a concurrent edit is refused instead of overwriting newer
/// work, and the replaced value is kept so the change can be reverted. Photographer/source and
/// memory approvals are recorded only and shown as labelled notes.
/// </summary>
public sealed class PhotoCorrectionReviewService(
    IPhotoCorrectionRepository corrections,
    IAdminPhotoRepository photos,
    IPhotoArchiveDatesRepository archiveDates,
    PublicQueryCacheService publicQueryCache,
    CoreSitemapService coreSitemapService,
    IOutputCacheStore outputCacheStore)
{
    /// <summary>
    /// The live value of the field a proposal of this kind changes, in the form stored on the
    /// proposal (<see cref="PhotoCorrectionValidation.UnsetValue"/> when empty). Null when the kind
    /// does not change a photo field or the photo is gone.
    /// </summary>
    public async Task<string?> GetCurrentValueAsync(string kind, int picId, CancellationToken cancellationToken)
    {
        if (!ChangesPhoto(kind))
        {
            return null;
        }

        var photo = await photos.GetByIdAsync(picId, cancellationToken);
        return photo is null ? null : await ReadAsync(kind, photo, cancellationToken);
    }

    public static string FieldLabel(string kind) => kind switch
    {
        PhotoCorrectionKind.Caption => "caption",
        PhotoCorrectionKind.EventDate => "event date",
        PhotoCorrectionKind.PhotoDate => "photo date",
        _ => "value",
    };

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

        if (!ChangesPhoto(proposal.Kind))
        {
            return await RecordAsync(
                () => corrections.ApproveAsync(id, editorEmail, reviewNotes, null, cancellationToken),
                "Accepted. It now appears on the photo.",
                cancellationToken);
        }

        return await ApplyAndApproveAsync(proposal, editorEmail, reviewNotes, cancellationToken);
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
                "Reverted. It is removed from the photo.",
                cancellationToken);
        }

        var photo = await photos.GetByIdAsync(proposal.PicId, cancellationToken);
        if (photo is null)
        {
            return new PhotoCorrectionReviewResult(false, "The photo no longer exists.");
        }

        var label = FieldLabel(proposal.Kind);
        if (!string.Equals(await ReadAsync(proposal.Kind, photo, cancellationToken), Canonical(proposal.Kind, proposal.ProposedValue), StringComparison.Ordinal))
        {
            return new PhotoCorrectionReviewResult(
                false,
                $"The {label} has been edited since this was applied, so it was not reverted automatically. Edit the photo directly.");
        }

        var restored = await TryWriteAsync(proposal.Kind, photo, proposal.AppliedFromValue, editorEmail, cancellationToken);
        if (!restored.Succeeded)
        {
            return restored;
        }

        return await RecordAsync(
            () => corrections.RevertAsync(id, editorEmail, reviewNotes, cancellationToken),
            $"Reverted. The previous {label} is back on the site.",
            cancellationToken);
    }

    private async Task<PhotoCorrectionReviewResult> ApplyAndApproveAsync(
        PhotoCorrectionProposal proposal,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken)
    {
        var label = FieldLabel(proposal.Kind);
        var photo = await photos.GetByIdAsync(proposal.PicId, cancellationToken);
        if (photo is null || !photo.IsVisible)
        {
            return new PhotoCorrectionReviewResult(false, $"The photo is hidden or deleted, so the {label} was not changed.");
        }

        var previous = await ReadAsync(proposal.Kind, photo, cancellationToken);
        if (!string.Equals(previous, proposal.CurrentValue ?? PhotoCorrectionValidation.UnsetValue, StringComparison.Ordinal))
        {
            return new PhotoCorrectionReviewResult(
                false,
                $"The {label} has changed since this was proposed. Review the current {label}, then reject this proposal or ask the member to resubmit.");
        }

        var applied = await TryWriteAsync(proposal.Kind, photo, Canonical(proposal.Kind, proposal.ProposedValue), editorEmail, cancellationToken);
        if (!applied.Succeeded)
        {
            return applied;
        }

        try
        {
            await corrections.ApproveAsync(proposal.Id, editorEmail, reviewNotes, previous, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Someone decided the proposal first. Put the value back so the photo and the trail agree.
            var current = await photos.GetByIdAsync(proposal.PicId, cancellationToken);
            if (current is not null)
            {
                await TryWriteAsync(proposal.Kind, current, previous!, editorEmail, cancellationToken);
            }

            return new PhotoCorrectionReviewResult(false, ex.Message);
        }

        return new PhotoCorrectionReviewResult(true, $"Approved. The {label} is updated on the site.");
    }

    private static bool ChangesPhoto(string kind) =>
        kind == PhotoCorrectionKind.Caption || PhotoCorrectionKind.IsDate(kind);

    /// <summary>The proposed value in the same canonical form <see cref="ReadAsync"/> returns.</summary>
    private static string Canonical(string kind, string proposedValue) =>
        PhotoCorrectionKind.IsDate(kind) && PartialDate.TryParse(proposedValue, out var date)
            ? date.ToIso()
            : proposedValue;

    private async Task<string> ReadAsync(string kind, AdminPhotoItem photo, CancellationToken cancellationToken)
    {
        if (kind == PhotoCorrectionKind.Caption)
        {
            return photo.Title;
        }

        var dates = await archiveDates.GetAsync(photo.PicId, cancellationToken);
        var date = kind == PhotoCorrectionKind.EventDate ? dates.EventDate : dates.PhotoDate;
        return date?.ToIso() ?? PhotoCorrectionValidation.UnsetValue;
    }

    private async Task<PhotoCorrectionReviewResult> TryWriteAsync(
        string kind,
        AdminPhotoItem photo,
        string newValue,
        string editorEmail,
        CancellationToken cancellationToken)
    {
        try
        {
            if (kind == PhotoCorrectionKind.Caption)
            {
                await WriteCaptionAsync(photo, newValue, editorEmail, cancellationToken);
            }
            else
            {
                await WriteDateAsync(kind, photo.PicId, newValue, editorEmail, cancellationToken);
            }
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

    private Task WriteCaptionAsync(AdminPhotoItem photo, string newTitle, string editorEmail, CancellationToken cancellationToken) =>
        photos.UpdateAsync(
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

    private async Task WriteDateAsync(
        string kind,
        int picId,
        string newValue,
        string editorEmail,
        CancellationToken cancellationToken)
    {
        PartialDate? replacement = PartialDate.TryParse(newValue, out var parsed) ? parsed : null;
        var current = await archiveDates.GetAsync(picId, cancellationToken);
        var updated = kind == PhotoCorrectionKind.EventDate
            ? current with { EventDate = replacement }
            : current with { PhotoDate = replacement };

        await archiveDates.SetAsync(picId, updated, current, cancellationToken);
        await photos.AppendAuditAsync(
            picId,
            "archive-dates",
            editorEmail,
            $"{FieldLabel(kind)} set to {replacement?.ToIso() ?? "(none)"}",
            cancellationToken);
    }

    private async Task<PhotoCorrectionReviewResult> RecordAsync(
        Func<Task<PhotoCorrectionProposal?>> decide,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await decide() is null)
            {
                return new PhotoCorrectionReviewResult(false, "Proposal not found.");
            }
        }
        catch (InvalidOperationException ex)
        {
            return new PhotoCorrectionReviewResult(false, ex.Message);
        }

        // Archive notes are rendered on the cached public photo page.
        await outputCacheStore.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, cancellationToken);
        return new PhotoCorrectionReviewResult(true, successMessage);
    }
}
