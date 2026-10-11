using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.PhotoCorrections;

public sealed class DetailModel(
    IPhotoCorrectionRepository photoCorrectionRepository,
    IAdminPhotoRepository photoRepository) : AdminPhotoCorrectionsPageModel
{
    public PhotoCorrectionProposal? Proposal { get; private set; }

    public AdminPhotoItem? Photo { get; private set; }

    /// <summary>Other proposals for the same photo, so competing claims are visible side by side.</summary>
    public IReadOnlyList<PhotoCorrectionProposal> OtherProposals { get; private set; } = [];

    public IReadOnlyList<PhotoCorrectionAuditEntry> Audit { get; private set; } = [];

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    /// <summary>True when the live caption no longer matches what the member saw.</summary>
    public bool CaptionIsStale =>
        Proposal is { Kind: PhotoCorrectionKind.Caption } proposal
        && Photo is not null
        && !string.Equals(Photo.Title, proposal.CurrentValue, StringComparison.Ordinal);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Proposal = await photoCorrectionRepository.GetByIdAsync(id, cancellationToken);
        if (Proposal is null)
        {
            return NotFound();
        }

        Photo = await photoRepository.GetByIdAsync(Proposal.PicId, cancellationToken);
        OtherProposals = (await photoCorrectionRepository.GetForPhotoAsync(Proposal.PicId, cancellationToken))
            .Where(other => other.Id != id)
            .ToList();
        Audit = await photoCorrectionRepository.GetAuditAsync(id, cancellationToken);

        StatusMessage = TempData["PhotoCorrectionMessage"] as string;
        StatusMessageKind = TempData["PhotoCorrectionMessageKind"] as string;
        ViewData["Title"] = "Review photo correction";
        Breadcrumbs = AdminBreadcrumbs.Page("Photo corrections", "/admin/photo-corrections", "Review photo correction");
        return Page();
    }
}
