namespace QueenZone.Data;

public sealed class SharedPhotoStore
{
    private readonly object sync = new();
    private readonly List<AdminPhotoCategory> categories = [];
    private readonly List<MutablePhoto> photos = [];
    private readonly List<AdminPhotoAuditEntry> auditEntries = [];
    private int nextPicId = 10_000;
    private long nextAuditId = 1;

    public SharedPhotoStore()
    {
    }

    public SharedPhotoStore(IEnumerable<PhotoCategorySeed> seedCategories)
    {
        lock (sync)
        {
            foreach (var category in seedCategories)
            {
                categories.Add(new AdminPhotoCategory(category.CatId, category.Name, NewsSlug.Slugify(category.Name)));
                foreach (var item in category.Items)
                {
                    photos.Add(ToMutable(category, item));
                }
            }

            if (photos.Count > 0)
            {
                nextPicId = photos.Max(photo => photo.PicId) + 1;
            }
        }
    }

    public IReadOnlyList<AdminPhotoCategory> GetCategories()
    {
        lock (sync)
        {
            return categories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public AdminPhotoCategory? GetCategory(int catId)
    {
        lock (sync)
        {
            return categories.SingleOrDefault(category => category.CatId == catId);
        }
    }

    public IReadOnlyList<AdminPhotoItem> GetPhotos(AdminPhotoListFilter filter)
    {
        lock (sync)
        {
            return photos
                .Select(ToAdminItem)
                .Where(item => Matches(item, filter))
                .OrderByDescending(item => item.DateTime)
                .ThenByDescending(item => item.PicId)
                .ToList();
        }
    }

    public IReadOnlyList<AdminPhotoItem> GetVisiblePhotosByCategory(int catId)
    {
        lock (sync)
        {
            return photos
                .Where(photo => photo.CatId == catId && photo.IsVisible)
                .OrderByDescending(photo => photo.DateTime)
                .ThenByDescending(photo => photo.PicId)
                .Select(ToAdminItem)
                .ToList();
        }
    }

    public IReadOnlyList<AdminPhotoItem> GetLatestVisiblePhotos(int take)
    {
        lock (sync)
        {
            return photos
                .Where(photo => photo.IsVisible)
                .OrderByDescending(photo => photo.PicId)
                .Take(take)
                .Select(ToAdminItem)
                .ToList();
        }
    }

    public AdminPhotoItem? GetPhoto(int picId)
    {
        lock (sync)
        {
            var photo = photos.SingleOrDefault(item => item.PicId == picId);
            return photo is null ? null : ToAdminItem(photo);
        }
    }

    public int Create(AdminPhotoCreateRequest request, string editorEmail)
    {
        lock (sync)
        {
            var category = categories.SingleOrDefault(item => item.CatId == request.CatId)
                ?? throw new InvalidOperationException($"Category {request.CatId} was not found.");

            var picId = nextPicId++;
            photos.Add(new MutablePhoto(
                picId,
                request.CatId,
                category.Name,
                category.Slug,
                request.Title.Trim(),
                request.LegacyUrl,
                request.LegacyThumbUrl,
                request.ThumbWidth,
                request.ThumbHeight,
                request.PictureWidth,
                request.PictureHeight,
                request.Year,
                request.DateTime,
                NormalizeKeywords(request.Keywords),
                request.IsVisible));

            AppendAuditUnlocked(picId, "create", editorEmail, $"Created \"{request.Title.Trim()}\"");
            return picId;
        }
    }

    public bool Update(
        int picId,
        AdminPhotoUpdateRequest request,
        string editorEmail,
        AdminPhotoConcurrencyToken? expected = null)
    {
        lock (sync)
        {
            var index = photos.FindIndex(photo => photo.PicId == picId);
            if (index < 0)
            {
                return false;
            }

            var category = categories.SingleOrDefault(item => item.CatId == request.CatId)
                ?? throw new InvalidOperationException($"Category {request.CatId} was not found.");

            var existing = photos[index];
            if (expected is not null && !Matches(existing, expected))
            {
                throw new OptimisticConcurrencyException();
            }

            photos[index] = existing with
            {
                CatId = request.CatId,
                CategoryName = category.Name,
                CategorySlug = category.Slug,
                Title = request.Title.Trim(),
                Keywords = NormalizeKeywords(request.Keywords),
                Year = request.Year,
                DateTime = request.DateTime,
            };

            AppendAuditUnlocked(picId, "edit", editorEmail, $"Updated \"{request.Title.Trim()}\"");
            return true;
        }
    }

    public bool SetVisibility(int picId, bool isVisible, string editorEmail, bool? expectedIsVisible = null)
    {
        lock (sync)
        {
            var index = photos.FindIndex(photo => photo.PicId == picId);
            if (index < 0)
            {
                return false;
            }

            if (expectedIsVisible is bool expected && photos[index].IsVisible != expected)
            {
                throw new OptimisticConcurrencyException();
            }

            photos[index] = photos[index] with { IsVisible = isVisible };
            AppendAuditUnlocked(picId, isVisible ? "show" : "hide", editorEmail, null);
            return true;
        }
    }

    public bool UpdateAssets(int picId, AdminPhotoAssetUpdate assets, string editorEmail)
    {
        lock (sync)
        {
            var index = photos.FindIndex(photo => photo.PicId == picId);
            if (index < 0)
            {
                return false;
            }

            photos[index] = photos[index] with
            {
                LegacyUrl = assets.LegacyUrl,
                LegacyThumbUrl = assets.LegacyThumbUrl,
                ThumbWidth = assets.ThumbWidth,
                ThumbHeight = assets.ThumbHeight,
                PictureWidth = assets.PictureWidth,
                PictureHeight = assets.PictureHeight,
            };

            AppendAuditUnlocked(picId, "replace", editorEmail, "Replaced image assets");
            return true;
        }
    }

    public bool UpdateThumbnail(
        int picId,
        string legacyThumbUrl,
        int thumbWidth,
        int thumbHeight,
        string editorEmail)
    {
        lock (sync)
        {
            var index = photos.FindIndex(photo => photo.PicId == picId);
            if (index < 0)
            {
                return false;
            }

            photos[index] = photos[index] with
            {
                LegacyThumbUrl = legacyThumbUrl,
                ThumbWidth = thumbWidth,
                ThumbHeight = thumbHeight,
            };

            AppendAuditUnlocked(picId, "regenerate-thumb", editorEmail, legacyThumbUrl);
            return true;
        }
    }

    public bool Delete(int picId, string editorEmail)
    {
        lock (sync)
        {
            var index = photos.FindIndex(photo => photo.PicId == picId);
            if (index < 0)
            {
                return false;
            }

            var title = photos[index].Title;
            photos.RemoveAt(index);
            AppendAuditUnlocked(picId, "delete", editorEmail, $"Deleted \"{title}\"");
            return true;
        }
    }

    public void AppendAudit(int picId, string action, string actorEmail, string? details)
    {
        lock (sync)
        {
            AppendAuditUnlocked(picId, action, actorEmail, details);
        }
    }

    public IReadOnlyList<AdminPhotoAuditEntry> GetAuditEntries(int picId)
    {
        lock (sync)
        {
            return auditEntries
                .Where(entry => entry.PicId == picId)
                .OrderByDescending(entry => entry.OccurredAt)
                .ToList();
        }
    }

    private void AppendAuditUnlocked(int picId, string action, string actorEmail, string? details)
    {
        auditEntries.Add(new AdminPhotoAuditEntry(
            nextAuditId++,
            picId,
            action,
            actorEmail,
            DateTimeOffset.UtcNow,
            details));
    }

    private static bool Matches(MutablePhoto existing, AdminPhotoConcurrencyToken expected) =>
        string.Equals(existing.Title, expected.Title.Trim(), StringComparison.Ordinal)
        && existing.CatId == expected.CatId
        && existing.DateTime == expected.DateTime
        && string.Equals(existing.Keywords ?? string.Empty, expected.Keywords?.Trim() ?? string.Empty, StringComparison.Ordinal)
        && existing.Year == expected.Year
        && existing.IsVisible == expected.IsVisible;

    private AdminPhotoItem ToAdminItem(MutablePhoto photo) =>
        new(
            photo.PicId,
            photo.CatId,
            photo.CategoryName,
            photo.CategorySlug,
            photo.Title,
            photo.LegacyUrl,
            photo.LegacyThumbUrl,
            PhotoImageUrl.Build(photo.LegacyUrl),
            PhotoImageUrl.Build(photo.LegacyThumbUrl),
            photo.ThumbWidth,
            photo.ThumbHeight,
            photo.PictureWidth,
            photo.PictureHeight,
            photo.Year,
            photo.DateTime,
            photo.Keywords,
            photo.IsVisible,
            photo.SubmittedByDisplayName);

    private static MutablePhoto ToMutable(PhotoCategorySeed category, PhotoItemSeed item) =>
        new(
            item.PicId,
            category.CatId,
            category.Name,
            NewsSlug.Slugify(category.Name),
            item.Title,
            item.Url,
            item.ThumbUrl,
            150,
            150,
            item.PictureWidth,
            item.PictureHeight,
            item.DateTime.Year,
            item.DateTime,
            null,
            true,
            string.IsNullOrWhiteSpace(item.SubmittedByDisplayName) ? null : item.SubmittedByDisplayName.Trim());

    private static bool Matches(AdminPhotoItem item, AdminPhotoListFilter filter)
    {
        if (filter.CatId is int catId && item.CatId != catId)
        {
            return false;
        }

        if (filter.IsVisible is bool isVisible && item.IsVisible != isVisible)
        {
            return false;
        }

        if (filter.Year is int year && item.Year != year)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var inTitle = item.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var inKeywords = item.Keywords?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
            if (!inTitle && !inKeywords)
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeKeywords(string? keywords) =>
        string.IsNullOrWhiteSpace(keywords) ? null : keywords.Trim();

    private sealed record MutablePhoto(
        int PicId,
        int CatId,
        string CategoryName,
        string CategorySlug,
        string Title,
        string LegacyUrl,
        string LegacyThumbUrl,
        int ThumbWidth,
        int ThumbHeight,
        int PictureWidth,
        int PictureHeight,
        int Year,
        DateTime DateTime,
        string? Keywords,
        bool IsVisible,
        string? SubmittedByDisplayName = null);
}
