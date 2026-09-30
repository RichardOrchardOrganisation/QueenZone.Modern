namespace QueenZone.Data;

public sealed class InMemoryPhotoRepository(SharedPhotoStore store) : IPhotoRepository
{
    public Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PhotoCategory> categories = store.GetCategories()
            .Select(category =>
            {
                var photos = store.GetVisiblePhotosByCategory(category.CatId);
                var cover = photos.FirstOrDefault()?.ThumbnailUrl;
                return new { category, count = photos.Count, cover };
            })
            .Where(item => item.count > 0)
            .Select(item => new PhotoCategory(
                item.category.CatId,
                item.category.Name,
                item.category.Slug,
                item.count,
                item.cover))
            .ToList();

        return Task.FromResult(categories);
    }

    public async Task<PhotoCategory?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var categories = await GetCategoriesAsync(cancellationToken);
        return categories.FirstOrDefault(category =>
            string.Equals(category.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public Task<PhotoCategoryPage> GetCategoryPageAsync(
        int catId,
        int page,
        int pageSize,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var category = store.GetCategory(catId);
        if (category is null)
        {
            return Task.FromResult(new PhotoCategoryPage(string.Empty, [], 0));
        }

        var activeFilter = filter ?? PhotoListFilter.None;
        var items = store.GetVisiblePhotosByCategory(catId)
            .Select(ToPhotoItem)
            .Where(activeFilter.Matches)
            .ToList();
        var paged = items
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PhotoCategoryPage(category.Name, paged, items.Count));
    }

    public Task<PhotoDetailNavigation?> GetDetailNavigationAsync(
        int catId,
        int picId,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var requested = filter ?? PhotoListFilter.None;
        var raw = store.GetVisiblePhotosByCategory(catId);
        var all = raw.Select(ToPhotoItem).ToList();
        var items = requested.IsActive ? all.Where(requested.Matches).ToList() : all;
        var index = items.FindIndex(item => item.PicId == picId);
        if (index >= 0)
        {
            return Task.FromResult<PhotoDetailNavigation?>(ToNavigation(items, raw, index, matchedRequestedFilter: true));
        }

        if (!requested.IsActive)
        {
            return Task.FromResult<PhotoDetailNavigation?>(null);
        }

        var fallback = all.FindIndex(item => item.PicId == picId);
        if (fallback < 0)
        {
            return Task.FromResult<PhotoDetailNavigation?>(null);
        }

        return Task.FromResult<PhotoDetailNavigation?>(ToNavigation(all, raw, fallback, matchedRequestedFilter: false));
    }

    public Task<IReadOnlyList<PhotoItem>> GetCategoryAllAsync(int catId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PhotoItem> items = store.GetVisiblePhotosByCategory(catId).Select(ToPhotoItem).ToList();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<PhotoItem>> GetRandomPublishedInCategoryAsync(
        int catId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var ids = PickIds(catId, take);
        IReadOnlyList<PhotoItem> items = LoadByIds(catId, ids);
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<int>> PickRandomPublishedPhotoIdsAsync(
        int catId,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PickIds(catId, take));

    public Task<IReadOnlyList<PhotoItem>> GetPublishedByIdsAsync(
        int catId,
        IReadOnlyList<int> picIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LoadByIds(catId, picIds));

    private IReadOnlyList<int> PickIds(int catId, int take)
    {
        var safeTake = Math.Clamp(take, 1, 8);
        var ids = store.GetVisiblePhotosByCategory(catId).Select(item => item.PicId).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var picked = new List<int>(Math.Min(safeTake, ids.Count));
        var pool = ids.ToList();
        while (picked.Count < safeTake && pool.Count > 0)
        {
            var index = Random.Shared.Next(pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return picked;
    }

    private IReadOnlyList<PhotoItem> LoadByIds(int catId, IReadOnlyList<int> picIds)
    {
        if (picIds.Count == 0)
        {
            return [];
        }

        var byId = store.GetVisiblePhotosByCategory(catId)
            .Select(ToPhotoItem)
            .ToDictionary(item => item.PicId);
        var items = new List<PhotoItem>(picIds.Count);
        foreach (var picId in picIds.Distinct())
        {
            if (byId.TryGetValue(picId, out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static PhotoDetailNavigation ToNavigation(
        IReadOnlyList<PhotoItem> items,
        IReadOnlyList<AdminPhotoItem> raw,
        int index,
        bool matchedRequestedFilter)
    {
        var previousId = index > 0 ? items[index - 1].PicId : (int?)null;
        var nextId = index < items.Count - 1 ? items[index + 1].PicId : (int?)null;
        return new(
            items[index],
            index,
            items.Count,
            previousId,
            nextId,
            matchedRequestedFilter,
            NeighborMedia(raw, previousId),
            NeighborMedia(raw, nextId));
    }

    private static PhotoNeighborMedia? NeighborMedia(IReadOnlyList<AdminPhotoItem> raw, int? picId)
    {
        if (picId is not int id)
        {
            return null;
        }

        var item = raw.FirstOrDefault(photo => photo.PicId == id);
        return item is null
            ? null
            : new PhotoNeighborMedia(item.LegacyUrl, item.PictureWidth, item.PictureHeight);
    }

    public Task<IReadOnlyList<PhotoSitemapCategory>> GetPublishedSitemapCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PhotoSitemapCategory> categories = store.GetCategories()
            .Select(category =>
            {
                IReadOnlyList<PhotoSitemapPhoto> photos = store.GetVisiblePhotosByCategory(category.CatId)
                    .Select(item => new PhotoSitemapPhoto(item.PicId, item.DateTime))
                    .ToList();
                return new { category, photos };
            })
            .Where(item => item.photos.Count > 0)
            .Select(item => new PhotoSitemapCategory(
                item.category.CatId,
                item.category.Name,
                item.category.Slug,
                item.photos))
            .ToList();

        return Task.FromResult(categories);
    }

    private static PhotoItem ToPhotoItem(AdminPhotoItem item) =>
        new(
            item.PicId,
            item.CatId,
            item.CategoryName,
            item.CategorySlug,
            item.Title,
            item.ImageUrl,
            item.ThumbnailUrl,
            item.ThumbWidth,
            item.ThumbHeight,
            item.PictureWidth,
            item.PictureHeight,
            item.Year,
            item.DateTime,
            item.SubmittedByDisplayName);
}
