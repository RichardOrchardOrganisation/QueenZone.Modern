using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumEditRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumEditRoutesTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task EditPostGet_RedirectsUnauthenticatedUsers()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var memberId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(memberId, "Owner body");

        var response = await client.GetAsync($"/forum/post/{postId}/edit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task EditPostGet_Returns403ForAuthenticatedNonOwners()
    {
        var ownerId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(ownerId, "Owner body");
        var client = CreateMemberClient(Guid.NewGuid());

        var response = await client.GetAsync($"/forum/post/{postId}/edit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("You do not have permission to edit this post.", html);
    }

    [Fact]
    public async Task ValidEditPost_UpdatesBodyAndSetsEditedAt()
    {
        var memberId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(memberId, "Original body");
        var client = CreateMemberClient(memberId);
        var form = await client.GetStringAsync($"/forum/post/{postId}/edit");
        var token = ExtractAntiforgeryToken(form);

        var response = await client.PostAsync($"/forum/post/{postId}/edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>Updated body<script>alert(1)</script></p>",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("#post-" + postId, response.Headers.Location!.OriginalString);

        using var scope = factory.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var updated = await write.GetPostAsync(postId);
        Assert.NotNull(updated);
        Assert.Contains("Updated body", updated.Body);
        Assert.DoesNotContain("script", updated.Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(updated.EditedAt);
        Assert.Equal(1, updated.EditCount);

        var topic = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Updated body", topic);
        Assert.Contains($"href=\"/forum/post/{postId}/edit\"", topic);
    }

    [Fact]
    public async Task AdminCanEditAnyPost()
    {
        var ownerId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(ownerId, "Admin target");
        var adminClient = CreateAdminClient();
        var form = await adminClient.GetStringAsync($"/forum/post/{postId}/edit");
        Assert.Contains("Save changes", form);
        var token = ExtractAntiforgeryToken(form);

        var response = await adminClient.PostAsync($"/forum/post/{postId}/edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "Admin rewrite",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var updated = await write.GetPostAsync(postId);
        Assert.Equal("Admin rewrite", updated!.Body);

        var topicPath = ForumRoutes.GetTopicCanonicalPath(updated.TopicId, updated.TopicSubject);
        var topicHtml = await adminClient.GetStringAsync(topicPath);
        Assert.DoesNotContain($"href=\"/forum/post/{postId}/edit\"", topicHtml);
    }

    [Fact]
    public async Task AllowlistedMemberEmail_CannotEditAnotherMembersPost()
    {
        var ownerId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(ownerId, "Owner body");
        var client = CreateMemberClient(Guid.NewGuid(), email: "admin@test.local");

        var response = await client.GetAsync($"/forum/post/{postId}/edit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("You do not have permission to edit this post.", html);
    }

    [Fact]
    public async Task AllowlistedMemberEmail_CanStillEditOwnPostInsideTheWindow()
    {
        var memberId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(memberId, "Own body");
        var client = CreateMemberClient(memberId, email: "admin@test.local");
        var form = await client.GetStringAsync($"/forum/post/{postId}/edit");
        var token = ExtractAntiforgeryToken(form);

        var response = await client.PostAsync($"/forum/post/{postId}/edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "Own rewrite",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task ForumPost_LinksAuthorProfileAndMessageCompose_ForAnotherMember()
    {
        var ownerId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(ownerId, "Contact the author");
        using var scope = factory.Services.CreateScope();
        var post = await scope.ServiceProvider.GetRequiredService<IForumWriteRepository>().GetPostAsync(postId);
        var viewerClient = CreateMemberClient(Guid.NewGuid(), displayName: "Forum Viewer");

        var html = await viewerClient.GetStringAsync(
            ForumRoutes.GetTopicCanonicalPath(post!.TopicId, post.TopicSubject));

        Assert.Contains($"href=\"/members/{ownerId}\"", html);
        Assert.Contains($"href=\"/messages/compose?to={ownerId}\"", html);
        Assert.Contains("aria-label=\"Message Forum Fan\"", html);
        Assert.DoesNotContain($"href=\"/forum/post/{postId}/edit\"", html);
    }

    [Fact]
    public async Task EditPost_stale_updated_at_reloads_current_body()
    {
        var memberId = Guid.NewGuid();
        var postId = await CreateOwnedPostAsync(memberId, "Original body");
        var client = CreateMemberClient(memberId);
        var form = await client.GetStringAsync($"/forum/post/{postId}/edit");
        var token = ExtractAntiforgeryToken(form);
        var expectedUpdatedAt = Regex.Match(form, "name=\"expectedUpdatedAt\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(expectedUpdatedAt));

        var first = await client.PostAsync($"/forum/post/{postId}/edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>First writer</p>",
            ["ExpectedUpdatedAt"] = expectedUpdatedAt,
        }));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var formAgain = await client.GetStringAsync($"/forum/post/{postId}/edit");
        var tokenAgain = ExtractAntiforgeryToken(formAgain);
        var response = await client.PostAsync($"/forum/post/{postId}/edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenAgain,
            ["Body"] = "<p>Stale overwrite</p>",
            ["ExpectedUpdatedAt"] = expectedUpdatedAt,
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(OptimisticConcurrencyException.UserMessage, html);
    }

    private async Task<int> CreateOwnedPostAsync(Guid memberId, string body)
    {
        var client = CreateMemberClient(memberId);
        var form = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var token = ExtractAntiforgeryToken(form);
        var response = await client.PostAsync("/forum/c/the-music/new-thread", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Subject"] = $"Edit route topic {memberId:N}",
            ["Body"] = body,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var created = write as InMemoryForumWriteRepository
            ?? throw new InvalidOperationException("Expected in-memory forum write repository in Testing.");
        var topicId = int.Parse(Regex.Match(response.Headers.Location!.OriginalString, @"/forum/topic/(\d+)/").Groups[1].Value);
        return created.GetPostsForTopic(topicId).Single().PostId;
    }

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, "admin@test.local");
        return client;
    }

    private HttpClient CreateMemberClient(Guid memberId, string displayName = "Forum Fan", string? email = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, displayName);
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, email);
        }

        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var input = Regex.Match(
            html,
            """<input[^>]*name="__RequestVerificationToken"[^>]*>""",
            RegexOptions.IgnoreCase);
        Assert.True(input.Success, "Antiforgery token input was not found in the form.");

        var value = Regex.Match(input.Value, "value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, "Antiforgery token value was not found in the form.");
        return value.Groups["token"].Value;
    }
}
