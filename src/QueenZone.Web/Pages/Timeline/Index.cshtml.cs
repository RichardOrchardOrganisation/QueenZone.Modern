using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Timeline;

public sealed class IndexModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public IReadOnlyList<TimelineDecadeGroup> Decades { get; private set; } = [];
    public IReadOnlyList<TimelineDecadeGroup> VisibleDecades { get; private set; } = [];
    public string? SelectedDecade { get; private set; }
    public int VisibleEventCount { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Timeline", "/timeline"),
    ];

    public async Task OnGetAsync(string? decade, CancellationToken cancellationToken)
    {
        var events = await publicQueryCache.GetAllPublishedHistoryEventsAsync(cancellationToken);

        var rows = events
            .OrderBy(e => e.EventDate)
            .ThenByDescending(e => e.Importance)
            .Select(e => new TimelineEventRow(e))
            .ToList();

        Decades = rows
            .GroupBy(r => r.Decade)
            .OrderBy(g => g.Key)
            .Select(g => new TimelineDecadeGroup(
                g.Key,
                g.Select(r => r.Year).Distinct().Order().ToList(),
                g.ToList()))
            .ToList();

        var currentDecade = (DateTime.UtcNow.Year / 10 * 10).ToString() + "s";
        SelectedDecade = Decades.Any(group => group.Decade == decade) ? decade
            : Decades.Any(group => group.Decade == currentDecade) ? currentDecade
            : Decades.LastOrDefault()?.Decade;
        VisibleDecades = Decades.Where(group => group.Decade == SelectedDecade).ToList();
        VisibleEventCount = VisibleDecades.Sum(group => group.Events.Count);

        ViewData["Title"] = "Queen History Timeline · Queenzone";
        ViewData["CanonicalPath"] = decade == SelectedDecade
            ? $"/timeline?decade={SelectedDecade}"
            : "/timeline";
        ViewData["Description"] = "Five decades of Queen history — concerts, releases, milestones and more, from the Queenzone archive.";
    }
}

public sealed class TimelineEventRow(QueenHistoryEvent e)
{
    public QueenHistoryEvent Event { get; } = e;
    public string Year { get; } = e.EventDate.Year.ToString();
    public string Decade { get; } = e.EventDate.Year.ToString()[..3] + "0s";
    public string DisplayCategory { get; } = e.Category.ToTimelineCategory();
    public string DisplayLabel { get; } = e.Category.ToTimelineCategoryLabel();
}

public sealed record TimelineDecadeGroup(
    string Decade,
    IReadOnlyList<string> Years,
    IReadOnlyList<TimelineEventRow> Events);

internal static class QueenHistoryEventCategoryTimelineExtensions
{
    internal static string ToTimelineCategory(this QueenHistoryEventCategory cat) => cat switch
    {
        QueenHistoryEventCategory.Concert => "live",
        QueenHistoryEventCategory.Release or QueenHistoryEventCategory.Recording => "music",
        QueenHistoryEventCategory.Award or QueenHistoryEventCategory.Birthday or QueenHistoryEventCategory.SiteHistory => "milestone",
        _ => "other",
    };

    internal static string ToTimelineCategoryLabel(this QueenHistoryEventCategory cat) => cat switch
    {
        QueenHistoryEventCategory.Concert => "Live",
        QueenHistoryEventCategory.Release => "Release",
        QueenHistoryEventCategory.Recording => "Recording",
        QueenHistoryEventCategory.Award => "Award",
        QueenHistoryEventCategory.Birthday => "Birthday",
        QueenHistoryEventCategory.TVRadio => "TV / Radio",
        QueenHistoryEventCategory.SiteHistory => "Archive",
        _ => "Other",
    };
}
