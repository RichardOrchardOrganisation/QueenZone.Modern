using QueenZone.Data;

namespace QueenZone.Web;

public sealed class NewsForumCategoryResolver(
    IForumWriteRepository forumWriteRepository,
    IForumRepository forumRepository)
{
    public async Task<int> ResolveAsync(CancellationToken cancellationToken)
    {
        var categoryId = await forumWriteRepository.EnsureCategoryAsync(
            NewsForumDiscussion.CategorySlug,
            NewsForumDiscussion.CategoryName,
            cancellationToken);
        var category = await forumRepository.GetCategoryByIdAsync(categoryId, cancellationToken);
        if (category is null || NewsForumDiscussion.IsTheMusic(category.Name))
        {
            throw new InvalidOperationException("News forum topic must not use The Music category.");
        }

        return categoryId;
    }
}
