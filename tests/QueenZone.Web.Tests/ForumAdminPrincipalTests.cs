using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumAdminPrincipalTests(PrincipalPinnedWebApplicationFactory factory)
    : IClassFixture<PrincipalPinnedWebApplicationFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Topic_moderation_controls_require_matching_pin(bool pinned)
    {
        var created = await CreateThreadAsync();
        using var client = CreateClient(pinned);
        var html = await client.GetStringAsync(created.Path);

        Assert.Equal(pinned, html.Contains("Hide all posts by", StringComparison.Ordinal));
        Assert.Equal(pinned, html.Contains("Close poll", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Poll_close_requires_matching_pin(bool pinned)
    {
        var created = await CreateThreadAsync();
        using var client = CreateClient(pinned);
        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();
        var poll = await polls.GetPollWithResultsAsync(created.TopicId, null);

        using var response = await PostAsync(client, $"/forum/poll/{poll!.PollId}/close");

        Assert.Equal(pinned ? HttpStatusCode.Redirect : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(pinned, (await polls.GetPollWithResultsAsync(created.TopicId, null))!.IsClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editing_another_authors_post_requires_matching_pin(bool pinned)
    {
        var created = await CreateThreadAsync();
        using var client = CreateClient(pinned);
        var path = $"/forum/post/{created.PostId}/edit";
        using var get = await client.GetAsync(path);
        Assert.Equal(pinned ? HttpStatusCode.OK : HttpStatusCode.Redirect, get.StatusCode);
        using var response = await PostAsync(client, path, new() { ["Body"] = "Rewritten" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        if (!pinned) Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var post = await scope.ServiceProvider.GetRequiredService<IForumWriteRepository>().GetPostAsync(created.PostId);
        Assert.Equal(pinned ? "Rewritten" : "<p>Original</p>", post!.Body);
        Assert.Equal(pinned ? 1 : 0, post.EditCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hiding_author_requires_matching_pin(bool pinned)
    {
        var created = await CreateThreadAsync();
        using var client = CreateClient(pinned);
        var path = $"/forum/post/{created.PostId}/hide-author";
        using var get = await client.GetAsync(path);
        Assert.Equal(pinned ? HttpStatusCode.OK : HttpStatusCode.Forbidden, get.StatusCode);
        using var response = await PostAsync(client, path);

        Assert.Equal(pinned ? HttpStatusCode.Redirect : HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var summary = await scope.ServiceProvider.GetRequiredService<IForumWriteRepository>()
            .GetAuthorForumContentSummaryAsync(created.AuthorId, "Pin author");
        Assert.Equal(pinned, summary.IsHidden);
    }

    private HttpClient CreateClient(bool pinned)
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserTenantHeader, AdminPrincipalRequirementTests.WorkTenant);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserOidHeader,
            pinned ? AdminPrincipalRequirementTests.ObjectId : Guid.Empty.ToString());
        return client;
    }

    private async Task<(int TopicId, int PostId, Guid AuthorId, string Path)> CreateThreadAsync()
    {
        using var scope = factory.Services.CreateScope();
        var authorId = Guid.NewGuid();
        var subject = $"Principal pin {Guid.NewGuid():N}";
        var created = await scope.ServiceProvider.GetRequiredService<IForumWriteRepository>().CreateThreadAsync(
            new NewForumThread(1, authorId, "Pin author", subject, "<p>Original</p>", DateTimeOffset.UtcNow,
                new NewForumPoll("Best album?", false, null, null, ["A", "B"], authorId)));
        return (created.TopicId, created.StarterPostId, authorId, ForumRoutes.GetTopicCanonicalPath(created.TopicId, subject));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path,
        Dictionary<string, string>? fields = null)
    {
        fields ??= [];
        fields["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/contact"));
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync(path, form);
    }
}
