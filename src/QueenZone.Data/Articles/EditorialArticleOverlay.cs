namespace QueenZone.Data;

internal static class EditorialArticleOverlay
{
    public static IReadOnlyList<ArticleItem> Apply(
        IEnumerable<ArticleItem> items,
        IReadOnlyDictionary<int, EditorialArticle> overlays)
    {
        var result = new List<ArticleItem>();
        foreach (var item in items)
        {
            if (!overlays.TryGetValue(item.Id, out var edit))
            {
                result.Add(item);
                continue;
            }

            if (edit.Status == EditorialArticleStatus.Unpublished)
            {
                continue;
            }

            result.Add(item with
            {
                Title = edit.Title,
                Excerpt = edit.Excerpt,
                Body = string.IsNullOrEmpty(item.Body) ? string.Empty : edit.Body,
                PublishedAt = edit.PublishedAt.UtcDateTime,
                Source = edit.Source,
                CategoryName = edit.Category,
                ImageBlobKey = edit.ImageBlobKey,
                AuthorName = edit.AuthorName,
                Tags = edit.Tags,
            });
        }

        return result;
    }
}
