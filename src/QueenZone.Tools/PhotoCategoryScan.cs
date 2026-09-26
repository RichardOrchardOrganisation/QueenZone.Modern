using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>Loads public photos across the categories selected by an id or slug filter, capped at an optional limit.</summary>
internal static class PhotoCategoryScan
{
    public static async Task<IReadOnlyList<PhotoItem>> LoadPhotosAsync(
        IPhotoRepository repository,
        int? categoryId,
        string? categorySlug,
        int? limit,
        CancellationToken cancellationToken)
    {
        var categories = await repository.GetCategoriesAsync(cancellationToken);

        if (categoryId is int id)
        {
            categories = categories.Where(category => category.CatId == id).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            categories = categories
                .Where(category => string.Equals(category.Slug, categorySlug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var photos = new List<PhotoItem>();
        foreach (var category in categories)
        {
            var items = await repository.GetCategoryAllAsync(category.CatId, cancellationToken);
            photos.AddRange(items);
            if (limit is int cap && photos.Count >= cap)
            {
                return photos.Take(cap).ToList();
            }
        }

        return photos;
    }
}
