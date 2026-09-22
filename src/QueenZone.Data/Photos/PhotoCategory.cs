namespace QueenZone.Data;

public sealed record PhotoCategory(
    int CatId,
    string Name,
    string Slug,
    int ImageCount,
    string? CoverThumbnailUrl = null);

/// <summary>
/// Detail lightbox context without loading the whole category collection.
/// </summary>
public sealed record PhotoDetailNavigation(
    PhotoItem Photo,
    int Index,
    int Count,
    int? PreviousPicId,
    int? NextPicId,
    /// <summary>
    /// False when a requested size filter excluded this photo and navigation
    /// was resolved against the unfiltered category instead.
    /// </summary>
    bool MatchedRequestedFilter = true);
