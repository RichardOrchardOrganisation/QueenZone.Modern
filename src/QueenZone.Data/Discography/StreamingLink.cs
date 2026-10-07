namespace QueenZone.Data;

/// <summary>A public "Listen on" link for an album or track.</summary>
public sealed record StreamingLink(StreamingProvider Provider, string Url);

/// <summary>
/// A validated, normalised link from <see cref="StreamingLinkUrl.TryParse"/>.
/// <paramref name="ExternalId"/> is the Spotify base62 id or the Apple numeric album/track id.
/// </summary>
public sealed record ParsedStreamingLink(StreamingProvider Provider, StreamingLinkKind Kind, string ExternalId, string Url);

/// <summary>Admin view of a stored link, with provenance.</summary>
public sealed record AdminStreamingLink(
    StreamingProvider Provider,
    string ExternalId,
    string Url,
    StreamingLinkSource Source,
    DateTime UpdatedAtUtc,
    string? UpdatedBy)
{
    public StreamingLink ToPublic() => new(Provider, Url);
}

/// <summary>A link to store for one album or track and provider.</summary>
public sealed record StreamingLinkWrite(ParsedStreamingLink Link, StreamingLinkSource Source, string? UpdatedBy)
{
    public const int MaxUpdatedByLength = 100;

    /// <summary>Throws when this write cannot be stored in the given provider and kind slot.</summary>
    public void EnsureFits(StreamingProvider provider, StreamingLinkKind kind)
    {
        if (Link.Provider != provider)
        {
            throw new ArgumentException($"A {Link.Provider.DisplayName()} link cannot be stored as {provider.DisplayName()}.", nameof(provider));
        }

        if (Link.Kind != kind)
        {
            throw new ArgumentException($"Expected a {kind.ToString().ToLowerInvariant()} link but got a {Link.Kind.ToString().ToLowerInvariant()} link.", nameof(kind));
        }
    }

    public string? TrimmedUpdatedBy()
    {
        var value = UpdatedBy?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= MaxUpdatedByLength ? value : value[..MaxUpdatedByLength];
    }
}
