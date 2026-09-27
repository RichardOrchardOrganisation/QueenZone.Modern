using System.Text.RegularExpressions;
using QueenZone.Data;

namespace QueenZone.Routing;

public static class FanPerformanceRoutes
{
    public const int PageSize = 20;

    public static string GetIndexPath() => "/fan-performances";

    public static string GetPublicItemAnchorId(int id) => $"fan-performance-{id}";

    public static string GetPublicPath(int id) => $"{GetIndexPath()}#{GetPublicItemAnchorId(id)}";

    public static string GetPagePath(int page) =>
        page <= 1 ? GetIndexPath() : $"/fan-performances/page/{page}";

    public static string GetAudioPath(int id) => $"/fan-performances/{id}/audio";

    public static string GetApiListPath() => "/api/v1/content/fan-performances";

    public static string GetApiDetailPath(int id) => $"{GetApiListPath()}/{id}";

    public static string GetApiAudioPath(int id) => $"{GetApiDetailPath(id)}/audio";

    public static string GetAudioPath(int id, string title) =>
        $"/fan-performances/{id}/audio/{GetDownloadFileName(title)}";

    public static string GetDownloadFileName(string title) =>
        $"{ToUrlSafeFilename(title)}.mp3";

    private static readonly Regex NonAlphanumericRun = new(@"[^a-zA-Z0-9]+", RegexOptions.Compiled, RegexDefaults.MatchTimeout);

    private static string ToUrlSafeFilename(string title)
    {
        var safe = NonAlphanumericRun.Replace(title.Trim(), "-").Trim('-');
        if (safe.Length == 0) return "download";
        return safe.Length > 100 ? safe[..100] : safe;
    }

    public static string GetReportPath(int id) => $"{GetIndexPath()}/{id}/report";

    public static string GetApiReportPath(int id) => $"/api/v1/me/fan-performances/{id}/report";

    public static string GetLoginPath(string returnPath) =>
        $"/account/login?returnUrl={Uri.EscapeDataString(returnPath)}";

    public static int GetTotalPages(int visibleCount, int pageSize = PageSize) =>
        ArchivePagination.GetTotalPages(visibleCount, pageSize);

    public static int ResolveTotalPages(int currentPage, int itemCount, int visibleCount, int totalPages) =>
        ArchivePagination.ResolveTotalPages(currentPage, itemCount, visibleCount, totalPages, PageSize);

    public static ArchivePaginationViewModel? GetPaginationViewModel(int currentPage, int totalPages) =>
        ArchivePagination.BuildViewModel("Fan performances pagination", currentPage, totalPages, GetPagePath);
}
