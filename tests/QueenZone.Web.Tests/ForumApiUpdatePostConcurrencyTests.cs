using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumApiUpdatePostConcurrencyTests
{
    [Fact]
    public async Task UpdatePostAsync_maps_each_write_status()
    {
        var memberId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, memberId.ToString("D"))],
            "test"));
        var ugc = new UgcHtml(Options.Create(new BlobUploadOptions()));
        var forumOptions = Options.Create(new ForumOptions { PostEditWindowMinutes = 60 });
        var existing = new ForumEditablePost(
            10,
            3,
            "Topic",
            "Body",
            memberId,
            "Fan",
            DateTimeOffset.UtcNow,
            null,
            0,
            1,
            DateTimeOffset.UtcNow);

        foreach (var (status, code) in new (ForumPostUpdateStatus Status, int Code)[]
        {
            (ForumPostUpdateStatus.Success, StatusCodes.Status200OK),
            (ForumPostUpdateStatus.NotFound, StatusCodes.Status404NotFound),
            (ForumPostUpdateStatus.ConcurrencyConflict, StatusCodes.Status409Conflict),
            (ForumPostUpdateStatus.EditWindowExpired, StatusCodes.Status403Forbidden),
            (ForumPostUpdateStatus.EditingDisabled, StatusCodes.Status403Forbidden),
            (ForumPostUpdateStatus.Forbidden, StatusCodes.Status403Forbidden),
        })
        {
            var result = await ForumApiEndpoints.UpdatePostAsync(
                user,
                new ForumPostRouteIds(existing.TopicId, existing.PostId),
                new ForumPostUpdateRequestDto { Body = "Updated body text." },
                new StatusForumWriteRepository(existing, status),
                ugc,
                forumOptions,
                CancellationToken.None);
            var http = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
            Assert.Equal(code, http.StatusCode);
        }

        var anonymous = await ForumApiEndpoints.UpdatePostAsync(
            new ClaimsPrincipal(),
            new ForumPostRouteIds(3, 10),
            new ForumPostUpdateRequestDto { Body = "Updated body text." },
            new StatusForumWriteRepository(existing, ForumPostUpdateStatus.Success),
            ugc,
            forumOptions,
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(anonymous).StatusCode);
    }

    private sealed class StatusForumWriteRepository(
        ForumEditablePost existing,
        ForumPostUpdateStatus status) : IForumWriteRepository
    {
        public Task<ForumThreadCreateResult> CreateThreadAsync(NewForumThread thread, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumEditablePost?>(postId == existing.PostId ? existing : null);

        public Task<ForumPostUpdateResult> UpdatePostAsync(
            int postId,
            Guid editorMemberId,
            string sanitisedBody,
            bool isAdmin,
            int editWindowMinutes,
            DateTimeOffset? expectedUpdatedAt = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumPostUpdateResult(status, existing.TopicId, existing.TopicSubject));

        public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountPostsByMemberSinceAsync(Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountApprovedPostsByMemberAsync(Guid memberId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task HideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnhideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> EnsureCategoryAsync(string slug, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
