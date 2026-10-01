using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.Web.Search;

namespace QueenZone.Web.Pages.Admin.Search;

/// <summary>
/// Lets an admin manually trigger a full <c>SearchDocument</c> reindex. The rebuild runs in a
/// single-flight background job so the HTTP request returns immediately — reverse-proxy timeouts
/// previously aborted mid-sequence (often after News only). Standing in for a durable scheduled
/// worker tracked separately.
/// </summary>
public sealed class IndexModel(
    ISearchIndexService searchIndexService,
    SearchReindexJobService reindexJobService) : PageModel
{
    private const string MessageKey = "SearchIndexMessage";
    private const string MessageKindKey = "SearchIndexMessageKind";

    public IReadOnlyDictionary<string, int> ContentTypeCounts { get; } =
        new Dictionary<string, int>();

    public int TotalCount { get; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public SearchReindexJobSnapshot Job { get; private set; } = new(
        SearchReindexJobPhase.Idle,
        CurrentContentType: null,
        Message: string.Empty,
        StartedAt: null,
        FinishedAt: null);

    public bool IsRunning => Job.Phase == SearchReindexJobPhase.Running;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public void OnGet()
    {
        StatusMessage = TempData[MessageKey] as string;
        StatusMessageKind = TempData[MessageKindKey] as string;
        Job = reindexJobService.GetSnapshot();
        Breadcrumbs = AdminBreadcrumbs.Section("Search index", "/admin/search");
        // Counts load via ?handler=Status so a slow index query cannot hold first paint (#1968).
    }

    public IActionResult OnPostReindex()
    {
        if (reindexJobService.TryStart())
        {
            TempData[MessageKey] = "Reindex started in the background. This page will update as it progresses.";
            TempData[MessageKindKey] = "success";
        }
        else
        {
            TempData[MessageKey] = "A reindex is already in progress.";
            TempData[MessageKindKey] = "info";
        }

        return RedirectToPage();
    }

    /// <summary>JSON status + live document counts for the admin page poller.</summary>
    public async Task<IActionResult> OnGetStatusAsync(CancellationToken cancellationToken)
    {
        var job = reindexJobService.GetSnapshot();
        var counts = await searchIndexService.GetContentTypeCountsAsync(cancellationToken);
        var total = counts.Values.Sum();

        return new JsonResult(new
        {
            phase = job.Phase.ToString(),
            currentContentType = job.CurrentContentType,
            currentContentTypeLabel = string.IsNullOrEmpty(job.CurrentContentType)
                ? null
                : SiteSearchContentType.DisplayLabel(job.CurrentContentType),
            message = job.Message,
            startedAt = job.StartedAt,
            finishedAt = job.FinishedAt,
            isRunning = job.Phase == SearchReindexJobPhase.Running,
            totalCount = total,
            contentTypeCounts = counts
                .OrderBy(pair => pair.Key)
                .Select(pair => new
                {
                    contentType = pair.Key,
                    label = SiteSearchContentType.DisplayLabel(pair.Key),
                    count = pair.Value,
                })
                .ToList(),
        });
    }
}
