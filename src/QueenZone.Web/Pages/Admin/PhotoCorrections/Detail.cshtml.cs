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

    public bool IsOpen => Proposal is not null && PhotoCorrectionStatus.IsOpen(Proposal.Status);

    public bool CanRevert => Proposal?.Status == PhotoCorrectionStatus.Approved;

    public string StatusNoticeClass =>
        StatusMessageKind == "error" ? "qz-account-notice--error" : "qz-account-notice--success";

    public string KindLabel => PhotoCorrectionKind.Label(Proposal!.Kind);

    public string CreditLabel =>
        string.IsNullOrWhiteSpace(Proposal!.PublicCreditName) ? "None" : Proposal.PublicCreditName;

    public string StaleWarning =>
        $"The photo's caption is now \"{Photo?.Title}\", which is not what the member saw. Approving is blocked until you reject this or the member resubmits.";

    public string PhotoSummary => Photo is null
        ? "photo no longer exists"
        : $"current caption: {Photo.Title} ({(Photo.IsVisible ? "visible" : "hidden")})";

    public IReadOnlyList<string> AuditLines =>
        Audit.Select(entry =>
        {
            var actor = string.IsNullOrEmpty(entry.ActorEmail) ? string.Empty : $" by {entry.ActorEmail}";
            var details = string.IsNullOrWhiteSpace(entry.Details) ? string.Empty : $": {entry.Details}";
            return $"{entry.OccurredAt:u} — {entry.Action}{actor}{details}";
        }).ToList();

    public string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

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
