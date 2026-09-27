namespace QueenZone.Data;

/// <summary>
/// Process-wide regex match timeout. Call <see cref="ApplyProcessDefault"/> as the first
/// statement of each executable — <see cref="System.Text.RegularExpressions.Regex"/> reads
/// the AppContext value once, in its static constructor.
/// </summary>
public static class RegexDefaults
{
    public const int MatchTimeoutMilliseconds = 2000;

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);

    public static void ApplyProcessDefault() =>
        AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", MatchTimeout);
}
