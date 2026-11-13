using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumPostEditServiceTests(QueenZoneWebApplicationFactory factory) : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Fact]
    public async Task Edit_preserves_arguments_and_invalidates_only_on_success()
    {
        using var scope = factory.Services.CreateScope();
        var effects = scope.ServiceProvider.GetRequiredService<ForumPostWriteEffects>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var post = new ForumEditablePost(10, 3, "Topic", "Original", Guid.NewGuid(), "Fan", DateTimeOffset.UtcNow, null, 0, 26);
        var actor = new ForumPostEditActor(Guid.NewGuid(), true);
        var expected = DateTimeOffset.UtcNow;
        using var cancellation = new CancellationTokenSource();
        string[] keys = [PublicQueryCacheKeys.ForumCategories, PublicQueryCacheKeys.ForumThreadCount,
            PublicQueryCacheKeys.ForumRecentThreads(ForumRoutes.RecentThreadsCount), PublicQueryCacheKeys.ForumLegacyDiscographyThreads];
        foreach (var status in Enum.GetValues<ForumPostUpdateStatus>())
        {
            var repository = new ForumApiUpdatePostConcurrencyTests.StatusForumWriteRepository(post, status);
            var service = new ForumPostEditService(repository, new UgcHtml(Options.Create(new BlobUploadOptions())),
                effects, Options.Create(new ForumOptions { PostEditWindowMinutes = 42 }));
            foreach (var key in keys) cache.Set(key, "cached");
            var result = await service.EditAsync(3, 10, actor,
                status == ForumPostUpdateStatus.ValidationFailed ? "<script>alert(1)</script>" : "Edited<script>alert(1)</script>",
                expected, cancellation.Token);

            Assert.Equal(status, result.Status);
            Assert.All(keys, key => Assert.Equal(status != ForumPostUpdateStatus.Success, cache.TryGetValue(key, out _)));
            if (status == ForumPostUpdateStatus.ValidationFailed)
            {
                Assert.Equal(0, repository.GetCalls);
                Assert.Equal(0, repository.UpdateCalls);
                Assert.Equal("", result.SanitizedBody);
            }
            else
            {
                Assert.Equal(1, repository.GetCalls);
                Assert.Equal(1, repository.UpdateCalls);
                Assert.Equal((actor.MemberId, "Edited", true, 42, (DateTimeOffset?)expected, cancellation.Token), repository.Update);
                Assert.Equal((3, "Topic", 26, "Edited"), (result.TopicId, result.TopicSubject, result.PositionInThread, result.SanitizedBody));
            }
        }
    }

    [Theory]
    [InlineData(4, 10)]
    [InlineData(3, 11)]
    public async Task Edit_rejects_missing_or_mismatched_post_without_writes_or_effects(int topicId, int postId)
    {
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        cache.Set(PublicQueryCacheKeys.ForumCategories, "cached");
        var post = new ForumEditablePost(10, 3, "Topic", "Original", Guid.NewGuid(), "Fan", DateTimeOffset.UtcNow, null, 0);
        var repository = new ForumApiUpdatePostConcurrencyTests.StatusForumWriteRepository(post, ForumPostUpdateStatus.Success);
        var service = new ForumPostEditService(repository, new UgcHtml(Options.Create(new BlobUploadOptions())),
            scope.ServiceProvider.GetRequiredService<ForumPostWriteEffects>(), Options.Create(new ForumOptions()));

        var result = await service.EditAsync(topicId, postId, new(post.AuthorMemberId!.Value, false), "Edited", null, CancellationToken.None);

        Assert.Equal(ForumPostUpdateStatus.NotFound, result.Status);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.True(cache.TryGetValue(PublicQueryCacheKeys.ForumCategories, out _));
    }
}
