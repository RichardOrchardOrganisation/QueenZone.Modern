using System.Reflection;

namespace QueenZone.Web;

public static class BuildMetadata
{
    private static readonly Lazy<BuildMetadataValues> Current = new(Read);

    public static string Version => Current.Value.VersionText;

    public static string BuiltAtUtc => Current.Value.TimestampText;

    public static bool IsAvailable =>
        !string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(BuiltAtUtc);

    private static BuildMetadataValues Read()
    {
        var assembly = typeof(BuildMetadata).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?
            .Trim();

        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString() ?? "unknown";
        }

        var builtAtUtc = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildTimestampUtc")
            ?.Value?
            .Trim() ?? string.Empty;

        return new BuildMetadataValues(version, builtAtUtc);
    }

    private sealed record BuildMetadataValues(string VersionText, string TimestampText);
}
