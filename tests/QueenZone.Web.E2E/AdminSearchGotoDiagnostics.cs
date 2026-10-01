using System.Globalization;

namespace QueenZone.Web.E2E;

/// <summary>
/// Failure dump for admin search navigation that waits on DOMContentLoaded
/// instead of the full <c>load</c> event (#1968). Written beside Playwright screenshots/traces
/// under <see cref="E2EArtifactPaths"/> (<c>E2E_ARTIFACT_DIR</c> or <c>test-results/e2e</c>).
/// </summary>
internal static class AdminSearchGotoDiagnostics
{
    public const string FileSuffix = "search-goto-diagnostics";

    public static string FormatDump(
        string url,
        int? status,
        bool? searchIndexAdminPresent,
        string exceptionMessage)
    {
        return string.Join(
            Environment.NewLine,
            [
                "Admin search goto diagnostic",
                $"URL: {url}",
                $"Status: {FormatStatus(status)}",
                $"#search-index-admin: {FormatPresent(searchIndexAdminPresent)}",
                $"Exception: {exceptionMessage}",
            ]);
    }

    public static string WriteDump(
        string contents,
        string? directory = null,
        string? testName = null,
        DateTime? utcNow = null)
    {
        var dir = directory ?? E2EArtifactPaths.EnsureDirectory();
        Directory.CreateDirectory(dir);
        var name = E2EArtifactPaths.SanitizeFileName(testName ?? TestContext.CurrentContext.Test.Name);
        var stamp = (utcNow ?? DateTime.UtcNow).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(dir, $"{name}-{FileSuffix}-{stamp}.txt");
        File.WriteAllText(path, contents);
        return path;
    }

    private static string FormatStatus(int? status) =>
        status is int code ? code.ToString(CultureInfo.InvariantCulture) : "(none)";

    private static string FormatPresent(bool? present) =>
        present switch
        {
            true => "present",
            false => "missing",
            null => "(unavailable)",
        };
}
