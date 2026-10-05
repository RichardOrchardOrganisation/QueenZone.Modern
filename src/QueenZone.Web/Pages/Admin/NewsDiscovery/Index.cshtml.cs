using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.Web.Pages.Admin.NewsDiscovery;

public sealed class IndexModel(
    INewsDiscoveryRepository discoveryRepository,
    INewsAgentRunRequestRepository runRequestRepository) : AdminNewsDiscoveryPageModel
{
    [BindProperty(SupportsGet = true)]
    public NewsCandidateStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SourceId { get; set; }

    [BindProperty(SupportsGet = true)]
    public NewsDiscoveryTrustTier? TrustTier { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MinConfidence { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Entity { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? DiscoveredFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? DiscoveredTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? HasDraft { get; set; }

    [FromQuery(Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = AdminNewsDiscoveryRoutes.ListPageSize;

    public IReadOnlyList<NewsCandidateReviewListItem> Candidates { get; private set; } = [];

    public IReadOnlyList<NewsDiscoverySource> Sources { get; private set; } = [];

    public IReadOnlyList<NewsAgentRunRequest> RecentRuns { get; private set; } = [];

    public NewsAgentRunnerHeartbeat? RunnerHeartbeat { get; private set; }

    public bool RunnerRecentlySeen =>
        RunnerHeartbeat is not null && RunnerHeartbeat.LastSeenAtUtc >= DateTime.UtcNow.AddMinutes(-5);

    public int CurrentPage { get; private set; }

    public int TotalPages { get; private set; }

    public int TotalCount { get; private set; }

    public int RangeStart { get; private set; }

    public int RangeEnd { get; private set; }

    public ArchivePaginationViewModel? Pagination { get; private set; }

    [BindProperty]
    public string? ArticleUrl { get; set; }

    [BindProperty]
    public string UrlIngestionAction { get; set; } = "triage";

    public string? StatusMessage => TempData["DiscoveryMessage"] as string;

    public string? StatusMessageKind => TempData["DiscoveryMessageKind"] as string;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (PageNumber < 1)
        {
            return Redirect(BuildReturnUrl(1));
        }

        await LoadAsync(cancellationToken);

        if (TotalPages > 0 && PageNumber > TotalPages)
        {
            return Redirect(BuildReturnUrl(TotalPages));
        }

        return Page();
    }

    public async Task<IActionResult> OnPostQueueRunAsync(CancellationToken cancellationToken)
    {
        var result = await runRequestRepository.QueueAsync(
            new NewsAgentRunRequestCreate(EditorEmail, NewsAgentRunRequestKind.ScheduledGathering),
            cancellationToken);
        TempData["DiscoveryMessage"] = result.WasCreated
            ? "News gathering queued. The local Windows runner will fetch and triage it when next online."
            : $"A news gathering run is already {result.Request.Status.ToString().ToLowerInvariant()}.";
        TempData["DiscoveryMessageKind"] = result.WasCreated ? "success" : "info";
        return Redirect(BuildReturnUrl());
    }

    public async Task<IActionResult> OnPostQueueUrlIngestionAsync(CancellationToken cancellationToken)
    {
        if (!OutboundUrlSafety.TryValidatePublicHttpUrl(ArticleUrl, out var error, out var normalizedUrl)
            || string.IsNullOrWhiteSpace(normalizedUrl))
        {
            TempData["DiscoveryMessage"] = error;
            TempData["DiscoveryMessageKind"] = "error";
            return Redirect(BuildReturnUrl());
        }

        var generateDraft = string.Equals(UrlIngestionAction, "triage-and-draft", StringComparison.OrdinalIgnoreCase);
        var result = await runRequestRepository.QueueAsync(
            new NewsAgentRunRequestCreate(
                EditorEmail,
                NewsAgentRunRequestKind.UrlIngestion,
                normalizedUrl,
                generateDraft),
            cancellationToken);

        var queuedMessage = generateDraft
            ? "URL queued for forced triage and AI draft generation on the local Windows runner."
            : "URL queued for forced triage on the local Windows runner (no draft generation).";
        TempData["DiscoveryMessage"] = result.WasCreated
            ? queuedMessage
            : $"URL request #{result.Request.Id} was already queued.";
        TempData["DiscoveryMessageKind"] = "success";
        return Redirect(BuildReturnUrl());
    }

    public async Task<IActionResult> OnPostIgnoreAsync(int id, CancellationToken cancellationToken)
    {
        var candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (candidate is null)
        {
            return NotFound();
        }

        if (!NewsCandidateWorkflow.CanReject(candidate.Status))
        {
            TempData["DiscoveryMessage"] = NewsCandidateWorkflow.GetRejectError(candidate.Status);
            TempData["DiscoveryMessageKind"] = "error";
            return Redirect(BuildReturnUrl());
        }

        var ignored = await discoveryRepository.TryUpdateCandidateStatusAsync(
            id,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.Rejected,
                ReviewNotes: $"Ignored from discovery listing by {EditorEmail}."),
            cancellationToken);

        TempData["DiscoveryMessage"] = ignored
            ? $"Ignored candidate #{id}."
            : $"Candidate #{id} could not be ignored.";
        TempData["DiscoveryMessageKind"] = ignored ? "success" : "error";
        return Redirect(BuildReturnUrl());
    }

    public async Task<IActionResult> OnPostIgnoreListedAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        var ignoreable = Candidates
            .Where(candidate => NewsCandidateWorkflow.CanReject(candidate.Status))
            .ToList();

        var ignored = 0;
        foreach (var candidate in ignoreable)
        {
            var updated = await discoveryRepository.TryUpdateCandidateStatusAsync(
                candidate.Id,
                new NewsCandidateStatusUpdate(
                    NewsCandidateStatus.Rejected,
                    ReviewNotes: $"Bulk ignored from discovery listing by {EditorEmail}."),
                cancellationToken);

            if (updated)
            {
                ignored++;
            }
        }

        TempData["DiscoveryMessage"] = ignored == 1
            ? "Ignored 1 listed candidate."
            : $"Ignored {ignored} listed candidates.";
        TempData["DiscoveryMessageKind"] = ignored > 0 ? "success" : "info";
        return Redirect(BuildReturnUrl());
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = NewsCandidateListQueryDefaults.Normalize(
            GetEffectivePageNumber(),
            GetEffectivePageSize());
        CurrentPage = normalizedPage;
        PageSize = normalizedPageSize;

        ViewData["Title"] = CurrentPage <= 1 ? "News discovery review" : $"News discovery review – Page {CurrentPage}";
        Breadcrumbs = AdminBreadcrumbs.Section("AI discovery", "/admin/news-discovery");
        RecentRuns = await runRequestRepository.ListRecentAsync(5, cancellationToken);
        RunnerHeartbeat = await runRequestRepository.GetLatestHeartbeatAsync(cancellationToken);
        Sources = await discoveryRepository.GetSourcesAsync(cancellationToken: cancellationToken);

        var result = await discoveryRepository.ListCandidatesForReviewAsync(
            new NewsCandidateListQuery(
                Status: Status,
                SourceId: SourceId,
                TrustTier: TrustTier,
                MinConfidence: MinConfidence,
                Entity: string.IsNullOrWhiteSpace(Entity) ? null : Entity.Trim(),
                DiscoveredFromUtc: DiscoveredFrom?.ToUniversalTime(),
                DiscoveredToUtc: DiscoveredTo?.AddDays(1).ToUniversalTime(),
                HasDraft: HasDraft,
                Page: normalizedPage,
                PageSize: normalizedPageSize),
            cancellationToken);

        Candidates = result.Items;
        TotalCount = result.TotalCount;
        CurrentPage = result.Page;
        PageSize = result.PageSize;
        TotalPages = AdminNewsDiscoveryRoutes.GetListTotalPages(TotalCount, PageSize);
        RangeStart = TotalCount == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;
        RangeEnd = TotalCount == 0 ? 0 : RangeStart + Candidates.Count - 1;
        Pagination = AdminNewsDiscoveryRoutes.GetListPaginationViewModel(
            CurrentPage,
            TotalPages,
            page => BuildReturnUrl(page));
    }

    private string BuildReturnUrl(int? page = null) =>
        AdminNewsDiscoveryRoutes.BuildIndexPath(
            new NewsDiscoveryIndexQuery(
                Status,
                SourceId,
                TrustTier,
                MinConfidence,
                Entity,
                DiscoveredFrom,
                DiscoveredTo,
                HasDraft,
                GetEffectivePageSize()),
            page ?? GetEffectivePageNumber());

    private int GetEffectivePageNumber()
    {
        if (Request.HasFormContentType
            && Request.Form.TryGetValue("page", out var pageValue)
            && int.TryParse(pageValue, out var formPage))
        {
            return formPage;
        }

        return PageNumber;
    }

    private int GetEffectivePageSize()
    {
        if (Request.HasFormContentType
            && Request.Form.TryGetValue("pageSize", out var pageSizeValue)
            && int.TryParse(pageSizeValue, out var formPageSize))
        {
            return formPageSize;
        }

        return PageSize;
    }
}
