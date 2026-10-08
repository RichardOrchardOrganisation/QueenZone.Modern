using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>One provider slot to write: a new link, or null to remove the stored one.</summary>
public sealed record StreamingLinkChange(StreamingProvider Provider, StreamingLinkWrite? Link);

/// <summary>The "Listen on" fields on the admin album and song pages, one per provider.</summary>
public sealed class StreamingLinksFormInput
{
    public string? Spotify { get; set; }

    public string? AppleMusic { get; set; }

    public static StreamingLinksFormInput From(IReadOnlyList<AdminStreamingLink> links) =>
        new()
        {
            Spotify = links.FirstOrDefault(link => link.Provider == StreamingProvider.Spotify)?.Url,
            AppleMusic = links.FirstOrDefault(link => link.Provider == StreamingProvider.AppleMusic)?.Url,
        };

    public string? Value(StreamingProvider provider) => provider switch
    {
        StreamingProvider.Spotify => Spotify,
        StreamingProvider.AppleMusic => AppleMusic,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static string FieldName(StreamingProvider provider) => provider switch
    {
        StreamingProvider.Spotify => nameof(Spotify),
        StreamingProvider.AppleMusic => nameof(AppleMusic),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };
}

/// <summary>
/// Turns submitted "Listen on" fields into validated changes. Blank removes a link; a value equal
/// to the stored canonical URL is left alone so re-saving the form keeps an imported link's source.
/// </summary>
public static class DiscographyStreamingLinkForm
{
    public static (IReadOnlyList<StreamingLinkChange> Changes, IReadOnlyList<string> Errors) Parse(
        StreamingLinksFormInput input,
        StreamingLinkKind kind,
        IReadOnlyList<AdminStreamingLink> existing,
        string? updatedBy)
    {
        var changes = new List<StreamingLinkChange>();
        var errors = new List<string>();
        foreach (var provider in StreamingProviders.All)
        {
            var stored = existing.FirstOrDefault(link => link.Provider == provider);
            var value = input.Value(provider)?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                if (stored is not null)
                {
                    changes.Add(new StreamingLinkChange(provider, null));
                }

                continue;
            }

            if (!StreamingLinkUrl.TryParse(value, kind, out var parsed, out var error))
            {
                errors.Add($"{provider.DisplayName()} link: {error}");
                continue;
            }

            if (parsed.Provider != provider)
            {
                errors.Add($"{provider.DisplayName()} link: that link is from {parsed.Provider.DisplayName()}. Paste it in the {parsed.Provider.DisplayName()} field instead.");
                continue;
            }

            if (stored is not null && string.Equals(stored.Url, parsed.Url, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(new StreamingLinkChange(provider, new StreamingLinkWrite(parsed, StreamingLinkSource.Manual, updatedBy)));
        }

        return errors.Count > 0 ? ([], errors) : (changes, []);
    }
}
