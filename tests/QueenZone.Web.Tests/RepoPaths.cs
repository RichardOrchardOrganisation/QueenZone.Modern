namespace QueenZone.Web.Tests;

internal static class RepoPaths
{
    private static readonly Lazy<string> root = new(FindRoot);

    public static string Root => root.Value;

    public static string Combine(params string[] segments) => Path.Combine([Root, .. segments]);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QueenZone.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find QueenZone.sln above the test output directory.");
    }
}
