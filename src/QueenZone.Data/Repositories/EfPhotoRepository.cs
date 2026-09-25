using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Reads the legacy picture library with targeted SQL (counts, server-side paging,
/// neighbor navigation) instead of loading whole categories via
/// <c>Q_PIC_CAT_PAGE4_SP</c> (which materializes every visible row into a temp table).
/// </summary>
public sealed class EfPhotoRepository : IPhotoRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly PhotoSqlQueries sql;

    [ExcludeFromCodeCoverage]
    public EfPhotoRepository(QueenZoneDbContext dbContext)
        : this(dbContext, PhotoSqlQueries.CreateProduction())
    {
    }

    internal EfPhotoRepository(QueenZoneDbContext dbContext, PhotoSqlQueries sql)
    {
        this.dbContext = dbContext;
        this.sql = sql;
    }

    public async Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<CategoryWithCountRow>(sql.CategoriesWithCountsSql)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PhotoCategory> categories = rows
            .Select(row => new PhotoCategory(
                row.cat_id,
                row.name,
                NewsSlug.Slugify(row.name),
                row.ImageCount,
                string.IsNullOrWhiteSpace(row.CoverThumbUrl) ? null : PhotoImageUrl.Build(row.CoverThumbUrl)))
            .ToList();
        return categories;
    }

    public async Task<PhotoCategory?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var categories = await GetCategoriesAsync(cancellationToken);
        return categories.FirstOrDefault(category =>
            string.Equals(category.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PhotoCategoryPage> GetCategoryPageAsync(
        int catId,
        int page,
        int pageSize,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (safePage - 1) * safePageSize;
        var activeFilter = filter ?? PhotoListFilter.None;

        var total = await dbContext.Database
            .SqlQueryRaw<IntValueRow>(sql.ApplyFilter(sql.CategoryCountSql, activeFilter), catId)
            .SingleAsync(cancellationToken);

        var nameRows = await dbContext.Database
            .SqlQueryRaw<NameRow>(sql.CategoryNameSql, catId)
            .ToListAsync(cancellationToken);
        var categoryName = nameRows.FirstOrDefault()?.name ?? string.Empty;
        var categorySlug = NewsSlug.Slugify(categoryName);

        var rows = await dbContext.Database
            .SqlQueryRaw<CategoryPageRow>(
                sql.ApplyFilter(sql.CategoryPageSql, activeFilter),
                offset,
                safePageSize,
                catId)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => MapItem(row, catId, categoryName, categorySlug)).ToList();
        return new PhotoCategoryPage(categoryName, items, total.Value);
    }

    public async Task<PhotoDetailNavigation?> GetDetailNavigationAsync(
        int catId,
        int picId,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var requested = filter ?? PhotoListFilter.None;
        var effective = requested;
        if (requested.IsActive)
        {
            // Point lookup decides the filter before the category count runs, so a miss
            // issues the unfiltered navigation query only.
            var dimensions = await LoadDimensionsAsync(catId, picId, cancellationToken);
            if (dimensions is null)
            {
                return null;
            }

            if (!requested.Matches(dimensions.PIC_WIDTH, dimensions.PIC_HEIGHT))
            {
                effective = PhotoListFilter.None;
            }
        }

        var row = await LoadNavigationRowAsync(catId, picId, effective, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var categoryName = row.category_name ?? string.Empty;
        var categorySlug = NewsSlug.Slugify(categoryName);
        var photo = MapDetailItem(row, catId, categoryName, categorySlug);
        var matched = !requested.IsActive || effective.IsActive;
        return new PhotoDetailNavigation(
            photo,
            row.IndexBefore,
            row.TotalCount,
            row.PreviousPicId,
            row.NextPicId,
            matched,
            NeighborMedia(row.PreviousPicId, row.PreviousUrl, row.PreviousWidth, row.PreviousHeight),
            NeighborMedia(row.NextPicId, row.NextUrl, row.NextWidth, row.NextHeight));
    }

    public async Task<IReadOnlyList<PhotoItem>> GetCategoryAllAsync(
        int catId,
        CancellationToken cancellationToken = default)
    {
        var nameRows = await dbContext.Database
            .SqlQueryRaw<NameRow>(sql.CategoryNameSql, catId)
            .ToListAsync(cancellationToken);
        var categoryName = nameRows.FirstOrDefault()?.name ?? string.Empty;
        var categorySlug = NewsSlug.Slugify(categoryName);

        var rows = await dbContext.Database
            .SqlQueryRaw<CategoryPageRow>(sql.ApplyFilter(sql.CategoryAllSql, PhotoListFilter.None), catId)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PhotoItem> items = rows
            .Select(row => MapItem(row, catId, categoryName, categorySlug))
            .ToList();
        return items;
    }

    public async Task<IReadOnlyList<PhotoItem>> GetRandomPublishedInCategoryAsync(
        int catId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var ids = await PickRandomPublishedPhotoIdsAsync(catId, take, cancellationToken);
        return await GetPublishedByIdsAsync(catId, ids, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> PickRandomPublishedPhotoIdsAsync(
        int catId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var safeTake = Math.Clamp(take, 1, 8);
        var bounds = await dbContext.Database
            .SqlQueryRaw<IdBoundsRow>(sql.RandomIdBoundsSql, catId)
            .ToListAsync(cancellationToken);
        var range = bounds.FirstOrDefault();
        if (range?.MinId is not int minId || range.MaxId is not int maxId)
        {
            return [];
        }

        var ids = new List<int>(safeTake);
        var seen = new HashSet<int>();
        var attempts = safeTake * 8;
        for (var attempt = 0; attempt < attempts && ids.Count < safeTake; attempt++)
        {
            var target = IndexedIdRange.NextTarget(minId, maxId);
            var picked = await SeekPublishedPicIdAsync(sql.RandomIdSeekAtOrAfterSql, catId, target, cancellationToken)
                ?? await SeekPublishedPicIdAsync(sql.RandomIdSeekBeforeSql, catId, target, cancellationToken);
            if (picked is int picId && seen.Add(picId))
            {
                ids.Add(picId);
            }
        }

        return ids;
    }

    public async Task<IReadOnlyList<PhotoItem>> GetPublishedByIdsAsync(
        int catId,
        IReadOnlyList<int> picIds,
        CancellationToken cancellationToken = default)
    {
        if (picIds.Count == 0)
        {
            return [];
        }

        var nameRows = await dbContext.Database
            .SqlQueryRaw<NameRow>(sql.CategoryNameSql, catId)
            .ToListAsync(cancellationToken);
        var categoryName = nameRows.FirstOrDefault()?.name ?? string.Empty;
        var categorySlug = NewsSlug.Slugify(categoryName);

        var rows = await dbContext.Database
            .SqlQueryRaw<CategoryPageRow>(PhotoSqlQueries.ApplyIdList(sql.PublishedByIdsSql, picIds), catId)
            .ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(row => row.pic_id);
        var items = new List<PhotoItem>(picIds.Count);
        foreach (var picId in picIds.Distinct())
        {
            if (byId.TryGetValue(picId, out var row))
            {
                items.Add(MapItem(row, catId, categoryName, categorySlug));
            }
        }

        return items;
    }

    private async Task<DimensionRow?> LoadDimensionsAsync(
        int catId,
        int picId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<DimensionRow>(sql.PhotoDimensionsSql, catId, picId)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    private async Task<DetailNavigationRow?> LoadNavigationRowAsync(
        int catId,
        int picId,
        PhotoListFilter filter,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<DetailNavigationRow>(
                sql.ApplyFilter(sql.DetailNavigationSql, filter),
                catId,
                picId)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    private async Task<int?> SeekPublishedPicIdAsync(
        string seekSql,
        int catId,
        int targetPicId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<IntValueRow>(seekSql, catId, targetPicId)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault() is { } row && row.Value > 0 ? row.Value : null;
    }

    public async Task<IReadOnlyList<PhotoSitemapCategory>> GetPublishedSitemapCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<SitemapRow>(sql.SitemapSql)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PhotoSitemapCategory> categories = rows
            .GroupBy(row => row.cat_id)
            .Select(group =>
            {
                var first = group.First();
                var name = first.category_name;
                IReadOnlyList<PhotoSitemapPhoto> photos = group
                    .Select(row => new PhotoSitemapPhoto(row.pic_id, row.date_time))
                    .ToList();
                return new PhotoSitemapCategory(group.Key, name, NewsSlug.Slugify(name), photos);
            })
            .ToList();

        return categories;
    }

    private static PhotoItem MapItem(IPhotoRow row, int catId, string categoryName, string categorySlug) =>
        new(
            PicId: row.pic_id,
            CatId: catId,
            CategoryName: categoryName,
            CategorySlug: categorySlug,
            Title: row.NAME,
            ImageUrl: PhotoImageUrl.Build(row.URL),
            ThumbnailUrl: PhotoImageUrl.Build(row.THUMB_URL),
            ThumbWidth: row.T_WIDTH,
            ThumbHeight: row.T_HEIGHT,
            PictureWidth: row.PIC_WIDTH,
            PictureHeight: row.PIC_HEIGHT,
            Year: row.DATE_TIME.Year,
            DateTime: row.DATE_TIME);

    private static PhotoNeighborMedia? NeighborMedia(
        int? picId,
        string? filePath,
        int? width,
        int? height) =>
        picId is null
            ? null
            : new PhotoNeighborMedia(filePath, width ?? 0, height ?? 0);

    private static PhotoItem MapDetailItem(DetailNavigationRow row, int catId, string categoryName, string categorySlug) =>
        MapItem(row, catId, categoryName, categorySlug) with
        {
            SubmittedByDisplayName = string.IsNullOrWhiteSpace(row.submitted_by_display_name)
                ? null
                : row.submitted_by_display_name.Trim(),
        };

    private interface IPhotoRow
    {
        string NAME { get; }

        DateTime DATE_TIME { get; }

        string URL { get; }

        string THUMB_URL { get; }

        int T_HEIGHT { get; }

        int T_WIDTH { get; }

        int PIC_WIDTH { get; }

        int PIC_HEIGHT { get; }

        int pic_id { get; }

        string? category_name { get; }
    }

    private sealed class CategoryWithCountRow
    {
        public int cat_id { get; set; }

        public string name { get; set; } = string.Empty;

        public int ImageCount { get; set; }

        public string? CoverThumbUrl { get; set; }
    }

    private sealed class CategoryPageRow : IPhotoRow
    {
        public string NAME { get; set; } = string.Empty;

        public DateTime DATE_TIME { get; set; }

        public string URL { get; set; } = string.Empty;

        public string THUMB_URL { get; set; } = string.Empty;

        public int T_HEIGHT { get; set; }

        public int T_WIDTH { get; set; }

        public int PIC_WIDTH { get; set; }

        public int PIC_HEIGHT { get; set; }

        public int pic_id { get; set; }

        public string? category_name { get; set; }
    }

    private sealed class DetailNavigationRow : IPhotoRow
    {
        public string NAME { get; set; } = string.Empty;

        public DateTime DATE_TIME { get; set; }

        public string URL { get; set; } = string.Empty;

        public string THUMB_URL { get; set; } = string.Empty;

        public int T_HEIGHT { get; set; }

        public int T_WIDTH { get; set; }

        public int PIC_WIDTH { get; set; }

        public int PIC_HEIGHT { get; set; }

        public int pic_id { get; set; }

        public string? category_name { get; set; }

        public string? submitted_by_display_name { get; set; }

        public int TotalCount { get; set; }

        public int IndexBefore { get; set; }

        public int? PreviousPicId { get; set; }

        public int? NextPicId { get; set; }

        public string? PreviousUrl { get; set; }

        public int? PreviousWidth { get; set; }

        public int? PreviousHeight { get; set; }

        public string? NextUrl { get; set; }

        public int? NextWidth { get; set; }

        public int? NextHeight { get; set; }
    }

    private sealed class NameRow
    {
        public string name { get; set; } = string.Empty;
    }

    private sealed class IntValueRow
    {
        public int Value { get; set; }
    }

    private sealed class IdBoundsRow
    {
        public int? MinId { get; set; }

        public int? MaxId { get; set; }
    }

    private sealed class DimensionRow
    {
        public int PIC_WIDTH { get; set; }

        public int PIC_HEIGHT { get; set; }
    }

    private sealed class SitemapRow
    {
        public int cat_id { get; set; }

        public string category_name { get; set; } = string.Empty;

        public int pic_id { get; set; }

        public DateTime date_time { get; set; }
    }
}
