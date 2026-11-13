using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Pages.Forum;

namespace QueenZone.Web.Tests;

public sealed class EditPostModelTests(QueenZoneWebApplicationFactory factory) : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Fact]
    public async Task OnPost_maps_statuses_and_topic_mismatch()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = Guid.NewGuid();
        var post = new ForumEditablePost(10, 3, "Topic", "Current body", memberId, "Fan", DateTimeOffset.UtcNow, null, 0, 26);
        foreach (var status in Enum.GetValues<ForumPostUpdateStatus>())
        {
            var model = CreateModel(status, post.TopicId);
            var result = await model.OnPostAsync(post.PostId, CancellationToken.None);
            switch (status)
            {
                case ForumPostUpdateStatus.Success:
                    Assert.Equal(ForumRoutes.GetTopicCanonicalPath(3, "Topic", 2) + "#post-10", Assert.IsType<RedirectResult>(result).Url);
                    break;
                case ForumPostUpdateStatus.NotFound:
                    Assert.IsType<NotFoundResult>(result);
                    break;
                case ForumPostUpdateStatus.ValidationFailed:
                    Assert.IsType<PageResult>(result);
                    Assert.Contains(model.ModelState[nameof(model.Body)]!.Errors, error => error.ErrorMessage == "Body is required.");
                    break;
                default:
                    Assert.IsType<PageResult>(result);
                    Assert.Equal(403, model.Response.StatusCode);
                    Assert.Equal(status switch
                    {
                        ForumPostUpdateStatus.ConcurrencyConflict => OptimisticConcurrencyException.UserMessage,
                        ForumPostUpdateStatus.EditWindowExpired => "This post can no longer be edited.",
                        ForumPostUpdateStatus.EditingDisabled => "Post editing is currently disabled.",
                        _ => "You do not have permission to edit this post.",
                    }, model.ErrorMessage);
                    if (status == ForumPostUpdateStatus.ConcurrencyConflict) Assert.Equal(post.Body, model.Body);
                    break;
            }
        }

        Assert.IsType<NotFoundResult>(await CreateModel(ForumPostUpdateStatus.Success, 4).OnPostAsync(10, CancellationToken.None));

        EditPostModel CreateModel(ForumPostUpdateStatus status, int topicId)
        {
            var repository = new ForumApiUpdatePostConcurrencyTests.StatusForumWriteRepository(post, status);
            var options = Options.Create(new ForumOptions());
            var service = new ForumPostEditService(repository, new UgcHtml(Options.Create(new BlobUploadOptions())),
                scope.ServiceProvider.GetRequiredService<ForumPostWriteEffects>(), options);
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, memberId.ToString())], "test"));
            return new EditPostModel(repository, service, options, Options.Create(new AdminOptions()), TimeProvider.System)
            {
                PageContext = new PageContext
                {
                    HttpContext = context,
                    ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()),
                },
                TopicId = topicId,
                Body = status == ForumPostUpdateStatus.ValidationFailed ? " " : "Edited",
            };
        }
    }
}
