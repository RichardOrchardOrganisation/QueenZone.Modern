namespace QueenZone.Data;

public sealed class InMemoryAdminPhotoRepository(SharedPhotoStore store) : IAdminPhotoRepository
{
    public Task<AdminPhotoPage> GetPageAsync(
        AdminPhotoListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = store.GetPhotos(filter);
        if (filter.UsableForCardCrop)
        {
            all = all.Where(photo => photo.PictureWidth >= AdminPhotoListFilter.CardCropMinWidth
                && photo.PictureHeight >= AdminPhotoListFilter.CardCropMinHeight).ToList();
        }

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var items = all
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return Task.FromResult(new AdminPhotoPage(items, all.Count, safePage, safePageSize));
    }

    public Task<AdminPhotoItem?> GetByIdAsync(int picId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetPhoto(picId));

    public Task<IReadOnlyList<AdminPhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetCategories());

    public Task<AdminPhotoCategory?> GetCategoryByIdAsync(int catId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetCategory(catId));

    public Task<IReadOnlyList<string>> GetReferencedBlobNamesAsync(int catId, CancellationToken cancellationToken = default)
    {
        var photos = store.GetPhotos(new AdminPhotoListFilter(CatId: catId));
        IReadOnlyList<string> names = photos
            .SelectMany(photo => new[] { photo.LegacyUrl, photo.LegacyThumbUrl })
            .Select(url => url.Split('/').Last())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(names);
    }

    public Task<int> CreateAsync(
        AdminPhotoCreateRequest request,
        string editorEmail,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Create(request, editorEmail));

    public Task UpdateAsync(
        int picId,
        AdminPhotoUpdateRequest request,
        string editorEmail,
        AdminPhotoConcurrencyToken? expected = null,
        CancellationToken cancellationToken = default)
    {
        if (!store.Update(picId, request, editorEmail, expected))
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task SetVisibilityAsync(
        int picId,
        bool isVisible,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default)
    {
        if (!store.SetVisibility(picId, isVisible, editorEmail, expectedIsVisible))
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAssetsAsync(
        int picId,
        AdminPhotoAssetUpdate assets,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        if (!store.UpdateAssets(picId, assets, editorEmail))
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateThumbnailAsync(
        int picId,
        string legacyThumbUrl,
        int thumbWidth,
        int thumbHeight,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        if (!store.UpdateThumbnail(picId, legacyThumbUrl, thumbWidth, thumbHeight, editorEmail))
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(int picId, string editorEmail, CancellationToken cancellationToken = default)
    {
        if (!store.Delete(picId, editorEmail))
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        return Task.CompletedTask;
    }

    public Task AppendAuditAsync(
        int picId,
        string action,
        string actorEmail,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        store.AppendAudit(picId, action, actorEmail, details);
        return Task.CompletedTask;
    }
}
