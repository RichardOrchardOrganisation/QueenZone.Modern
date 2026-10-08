using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>An album as a streaming catalogue reports it, before any matching.</summary>
internal sealed record CatalogAlbum(
    string ExternalId,
    string Name,
    int? ReleaseYear,
    int TrackCount,
    string Url,
    bool IsCompilation = false);

/// <summary>One track on a <see cref="CatalogAlbum"/>.</summary>
internal sealed record CatalogTrack(
    string ExternalId,
    string Name,
    int TrackNumber,
    int DiscNumber,
    string Url);

/// <summary>
/// Read-only catalogue search for one provider. Implementations call the provider's public API;
/// tests substitute recorded responses.
/// </summary>
internal interface IStreamingCatalogClient
{
    StreamingProvider Provider { get; }

    Task<IReadOnlyList<CatalogAlbum>> SearchAlbumsAsync(string albumName, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogTrack>> GetTracksAsync(CatalogAlbum album, CancellationToken cancellationToken);
}
