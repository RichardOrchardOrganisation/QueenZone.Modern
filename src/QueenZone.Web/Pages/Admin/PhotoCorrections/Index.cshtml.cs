using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.PhotoCorrections;

public sealed class IndexModel(IPhotoCorrectionRepository photoCorrectionRepository)
    : AdminPhotoCorrectionsPageModel
{
    public IReadOnlyList<PhotoCorrectionListItem> Proposals { get; private set; } = [];

    public int PageNumber { get; private set; } = 1;

    public bool ShowMemories { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(int pageNumber = 1, string? queue = null, CancellationToken cancellationToken = default)
    {
        PageNumber = Math.Max(1, pageNumber);
        ShowMemories = string.Equals(queue, "memories", StringComparison.OrdinalIgnoreCase);
        Proposals = await photoCorrectionRepository.GetOpenAsync(PageNumber, 50, ShowMemories, cancellationToken);
        ViewData["Title"] = "Photo corrections";
        Breadcrumbs = AdminBreadcrumbs.Section("Photo corrections", "/admin/photo-corrections");
    }
}
