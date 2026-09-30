using System.Globalization;

namespace QueenZone.Web.E2E;

/// <summary>
/// Failure dump for admin create/edit reloads that wait on DOMContentLoaded
/// instead of the full <c>load</c> event (#1934). Written beside Playwright screenshots/traces
/// under <see cref="E2EArtifactPaths"/> (<c>E2E_ARTIFACT_DIR</c> or <c>test-results/e2e</c>).
/// </summary>
internal static class AdminEditReloadDiagnostics
{
    public const string FileSuffix = "reload-diagnostics";

    public static string FormatDump(
        string url,
        int? status,
        string? titleValue,
        string? previewImageSrc,
        string expectedTitle,
        string exceptionMessage)
    {
        return string.Join(
            Environment.NewLine,
            [
                "Admin edit reload diagnostic",
                $"URL: {url}",
                $"Status: {FormatStatus(status)}",
                $"Expected Title: {expectedTitle}",
                $"Title input value: {titleValue ?? "(unavailable)"}",
                $"Preview img[src]: {previewImageSrc ?? "(none)"}",
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
}
