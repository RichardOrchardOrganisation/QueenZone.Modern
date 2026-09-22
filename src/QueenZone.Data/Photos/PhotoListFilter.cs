namespace QueenZone.Data;

/// <summary>
/// Category-scoped size/orientation filter using original <c>PIC_WIDTH</c>/<c>PIC_HEIGHT</c>
/// (issue #437). Zeros never match active presets.
/// </summary>
/// <remarks>
/// Thresholds after live inventory (#435): desktop/phone/large remain 1920-based wallpaper
/// intent; <see cref="PhotoSizePreset.Hd"/> is a softer longest-side ≥ 1280 tier because most
/// archive images are sub-800px on the longest side.
/// </remarks>
public enum PhotoSizePreset
{
    None = 0,
    Desktop = 1,
    Phone = 2,
    Large = 3,
    Hd = 4,
    Landscape = 5,
    Portrait = 6,
}

public sealed class PhotoListFilter : IEquatable<PhotoListFilter>
{
    public const int DesktopMinWidth = 1920;

    public const int PhoneMinHeight = 1920;

    public const int LargeMinLongestSide = 1920;

    public const int HdMinLongestSide = 1280;

    public static PhotoListFilter None { get; } = new(PhotoSizePreset.None);

    public PhotoListFilter(PhotoSizePreset size) => Size = size;

    public PhotoSizePreset Size { get; }

    public bool IsActive => Size != PhotoSizePreset.None;

    /// <summary>Query-string value for <c>?size=</c>, or null when inactive.</summary>
    public string? QueryValue => Size switch
    {
        PhotoSizePreset.None => null,
        PhotoSizePreset.Desktop => "desktop",
        PhotoSizePreset.Phone => "phone",
        PhotoSizePreset.Large => "large",
        PhotoSizePreset.Hd => "hd",
        PhotoSizePreset.Landscape => "landscape",
        PhotoSizePreset.Portrait => "portrait",
        _ => null,
    };

    public string Label => Size switch
    {
        PhotoSizePreset.None => "All sizes",
        PhotoSizePreset.Desktop => "Desktop wallpaper",
        PhotoSizePreset.Phone => "Phone wallpaper",
        PhotoSizePreset.Large => "Large (1920+)",
        PhotoSizePreset.Hd => "HD (1280+)",
        PhotoSizePreset.Landscape => "Landscape",
        PhotoSizePreset.Portrait => "Portrait",
        _ => "All sizes",
    };

    public static IReadOnlyList<(PhotoSizePreset Size, string Query, string Label)> AllPresets { get; } =
    [
        (PhotoSizePreset.None, "", "All sizes"),
        (PhotoSizePreset.Desktop, "desktop", "Desktop wallpaper"),
        (PhotoSizePreset.Phone, "phone", "Phone wallpaper"),
        (PhotoSizePreset.Large, "large", "Large (1920+)"),
        (PhotoSizePreset.Hd, "hd", "HD (1280+)"),
        (PhotoSizePreset.Landscape, "landscape", "Landscape"),
        (PhotoSizePreset.Portrait, "portrait", "Portrait"),
    ];

    public static PhotoListFilter Parse(string? sizeQuery)
    {
        if (string.IsNullOrWhiteSpace(sizeQuery))
        {
            return None;
        }

        return sizeQuery.Trim().ToLowerInvariant() switch
        {
            "desktop" => new PhotoListFilter(PhotoSizePreset.Desktop),
            "phone" => new PhotoListFilter(PhotoSizePreset.Phone),
            "large" => new PhotoListFilter(PhotoSizePreset.Large),
            "hd" or "1280" => new PhotoListFilter(PhotoSizePreset.Hd),
            "landscape" => new PhotoListFilter(PhotoSizePreset.Landscape),
            "portrait" => new PhotoListFilter(PhotoSizePreset.Portrait),
            _ => None,
        };
    }

    public bool Matches(int pictureWidth, int pictureHeight)
    {
        if (Size == PhotoSizePreset.None)
        {
            return true;
        }

        if (pictureWidth <= 0 || pictureHeight <= 0)
        {
            return false;
        }

        var longest = Math.Max(pictureWidth, pictureHeight);
        return Size switch
        {
            PhotoSizePreset.Desktop => pictureWidth >= DesktopMinWidth && pictureWidth >= pictureHeight,
            PhotoSizePreset.Phone => pictureHeight >= PhoneMinHeight && pictureHeight > pictureWidth,
            PhotoSizePreset.Large => longest >= LargeMinLongestSide,
            PhotoSizePreset.Hd => longest >= HdMinLongestSide,
            PhotoSizePreset.Landscape => pictureWidth > pictureHeight,
            PhotoSizePreset.Portrait => pictureHeight > pictureWidth,
            _ => true,
        };
    }

    public bool Matches(PhotoItem photo) => Matches(photo.PictureWidth, photo.PictureHeight);

    /// <summary>
    /// SQL AND-clause fragment starting with a leading space + AND.
    /// <paramref name="widthExpr"/> and <paramref name="heightExpr"/> are bare integer columns
    /// (or SQLite <c>IFNULL</c> expressions). <paramref name="longestSideExpr"/> is the persisted
    /// <c>PIC_LONGEST_SIDE</c> column on SQL Server; SQLite omits it and uses a <c>CASE</c>.
    /// </summary>
    public string ToSqlAndClause(string widthExpr, string heightExpr, string? longestSideExpr = null)
    {
        if (!IsActive)
        {
            return string.Empty;
        }

        var usable = $"({widthExpr} > 0 AND {heightExpr} > 0)";
        var body = Size switch
        {
            PhotoSizePreset.Desktop =>
                $"{usable} AND {widthExpr} >= {DesktopMinWidth} AND {widthExpr} >= {heightExpr}",
            PhotoSizePreset.Phone =>
                $"{usable} AND {heightExpr} >= {PhoneMinHeight} AND {heightExpr} > {widthExpr}",
            PhotoSizePreset.Large => longestSideExpr is null
                ? $"{usable} AND (CASE WHEN {widthExpr} > {heightExpr} THEN {widthExpr} ELSE {heightExpr} END) >= {LargeMinLongestSide}"
                : $"{longestSideExpr} >= {LargeMinLongestSide}",
            PhotoSizePreset.Hd => longestSideExpr is null
                ? $"{usable} AND (CASE WHEN {widthExpr} > {heightExpr} THEN {widthExpr} ELSE {heightExpr} END) >= {HdMinLongestSide}"
                : $"{longestSideExpr} >= {HdMinLongestSide}",
            PhotoSizePreset.Landscape =>
                $"{usable} AND {widthExpr} > {heightExpr}",
            PhotoSizePreset.Portrait =>
                $"{usable} AND {heightExpr} > {widthExpr}",
            _ => usable,
        };

        return " AND " + body;
    }

    /// <summary>
    /// Production SQL Server fragment. Compares bare <c>PIC_WIDTH</c>/<c>PIC_HEIGHT</c>
    /// or the persisted <c>PIC_LONGEST_SIDE</c> column — never <c>CAST</c> around the column.
    /// </summary>
    public string ToSqlServerAndClause(string tableAlias)
    {
        var prefix = string.IsNullOrEmpty(tableAlias) ? string.Empty : tableAlias + ".";
        return ToSqlAndClause($"{prefix}PIC_WIDTH", $"{prefix}PIC_HEIGHT", $"{prefix}PIC_LONGEST_SIDE");
    }

    /// <summary>SQLite fixture expressions (integer columns).</summary>
    public string ToSqliteAndClause(string tableAlias)
    {
        var prefix = string.IsNullOrEmpty(tableAlias) ? string.Empty : tableAlias + ".";
        var w = $"IFNULL({prefix}PIC_WIDTH, 0)";
        var h = $"IFNULL({prefix}PIC_HEIGHT, 0)";
        return ToSqlAndClause(w, h);
    }

    public bool Equals(PhotoListFilter? other) => other is not null && Size == other.Size;

    public override bool Equals(object? obj) => obj is PhotoListFilter other && Equals(other);

    public override int GetHashCode() => Size.GetHashCode();

    public override string ToString() => QueryValue ?? string.Empty;
}
