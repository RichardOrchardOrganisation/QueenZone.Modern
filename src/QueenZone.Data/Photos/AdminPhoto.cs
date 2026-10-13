namespace QueenZone.Data;

public sealed record AdminPhotoItem(
    int PicId,
    int CatId,
    string CategoryName,
    string CategorySlug,
    string Title,
    string LegacyUrl,
    string LegacyThumbUrl,
    string ImageUrl,
    string ThumbnailUrl,
    int ThumbWidth,
    int ThumbHeight,
    int PictureWidth,
    int PictureHeight,
    int Year,
    DateTime DateTime,
    string? Keywords,
    bool IsVisible,
    string? SubmittedByDisplayName = null);

public sealed record AdminPhotoPage(
    IReadOnlyList<AdminPhotoItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record AdminPhotoCategory(int CatId, string Name, string Slug);

public sealed record AdminPhotoListFilter(
    int? CatId = null,
    bool? IsVisible = null,
    int? Year = null,
    string? Search = null,
    bool UsableForCardCrop = false)
{
    public const int CardCropMinWidth = 400;

    public const int CardCropMinHeight = 267;
}

public sealed record AdminPhotoCreateRequest(
    int CatId,
    string Title,
    string? Keywords,
    int Year,
    DateTime DateTime,
    bool IsVisible,
    string LegacyUrl,
    string LegacyThumbUrl,
    int ThumbWidth,
    int ThumbHeight,
    int PictureWidth,
    int PictureHeight);

public sealed record AdminPhotoUpdateRequest(
    string Title,
    string? Keywords,
    int Year,
    DateTime DateTime,
    int CatId);

public sealed record AdminPhotoConcurrencyToken(
    string Title,
    string? Keywords,
    int Year,
    DateTime DateTime,
    int CatId,
    bool IsVisible);

public sealed record AdminPhotoAssetUpdate(
    string LegacyUrl,
    string LegacyThumbUrl,
    int ThumbWidth,
    int ThumbHeight,
    int PictureWidth,
    int PictureHeight);

public sealed record AdminPhotoAuditEntry(
    long Id,
    int PicId,
    string Action,
    string ActorEmail,
    DateTimeOffset OccurredAt,
    string? Details);
