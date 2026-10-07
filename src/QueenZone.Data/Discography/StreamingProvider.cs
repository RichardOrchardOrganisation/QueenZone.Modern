namespace QueenZone.Data;

/// <summary>
/// A music service a discography album or track can link to. Stored in
/// <c>DiscographyStreamingLinks.Provider</c> as the <see cref="StreamingProviders.Key"/> string.
/// </summary>
public enum StreamingProvider
{
    Spotify,
    AppleMusic,
}

/// <summary>Whether a streaming link points at a whole album or a single track.</summary>
public enum StreamingLinkKind
{
    Album,
    Track,
}

/// <summary>How a stored streaming link was entered.</summary>
public enum StreamingLinkSource
{
    Manual,
    Imported,
}

public static class StreamingProviders
{
    public static IReadOnlyList<StreamingProvider> All { get; } = [StreamingProvider.Spotify, StreamingProvider.AppleMusic];

    /// <summary>Stable lowercase key used in storage and the JSON API.</summary>
    public static string Key(this StreamingProvider provider) => provider switch
    {
        StreamingProvider.Spotify => "spotify",
        StreamingProvider.AppleMusic => "apple-music",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static string DisplayName(this StreamingProvider provider) => provider switch
    {
        StreamingProvider.Spotify => "Spotify",
        StreamingProvider.AppleMusic => "Apple Music",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static StreamingProvider FromKey(string key) => key switch
    {
        "spotify" => StreamingProvider.Spotify,
        "apple-music" => StreamingProvider.AppleMusic,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown streaming provider key."),
    };

    public static string Key(this StreamingLinkSource source) => source switch
    {
        StreamingLinkSource.Manual => "manual",
        StreamingLinkSource.Imported => "imported",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static StreamingLinkSource SourceFromKey(string key) => key switch
    {
        "manual" => StreamingLinkSource.Manual,
        "imported" => StreamingLinkSource.Imported,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown streaming link source key."),
    };
}
