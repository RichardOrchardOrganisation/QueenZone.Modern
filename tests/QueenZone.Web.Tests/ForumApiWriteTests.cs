using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumApiWriteTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;

    public ForumApiWriteTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Create_topic_and_reply_require_bearer_token()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var topic = await client.PostAsJsonAsync(
                $"{ForumApiEndpoints.RootPath}/categories/1/topics",
                new { title = "Needs a token here", body = "Anonymous cannot write." });
            Assert.Equal(HttpStatusCode.Unauthorized, topic.StatusCode);
            Assert.Equal("application/problem+json", topic.Content.Headers.ContentType?.MediaType);

            using var reply = await client.PostAsJsonAsync(
                $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
                new { body = "Anonymous cannot reply." });
            Assert.Equal(HttpStatusCode.Unauthorized, reply.StatusCode);
            Assert.Equal("application/problem+json", reply.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Member_can_create_topic_and_reply_with_same_storage_as_website()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "App Poster");
        using var client = CreateBearerClient(memberId, "App Poster");

        using var createdResponse = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            new { title = "App started this thread", body = "Hello <strong>fans</strong><script>alert(1)</script>" });

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<ForumTopicCreatedDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.True(created.StarterPostId > 0);
        Assert.Equal("App started this thread", created.Title);
        Assert.Equal(ForumRoutes.GetTopicCanonicalPath(created.Id, created.Title), created.DetailPath);
        Assert.Equal(
            $"{ForumApiEndpoints.RootPath}/topics/{created.Id}",
            createdResponse.Headers.Location?.OriginalString);

        using var replyResponse = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{created.Id}/posts",
            new { body = "Plain reply from the app" });
        Assert.Equal(HttpStatusCode.Created, replyResponse.StatusCode);
        var reply = await replyResponse.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        Assert.NotNull(reply);
        Assert.Equal(created.Id, reply!.TopicId);
        Assert.Contains($"#post-{reply.Id}", reply.DetailPath, StringComparison.Ordinal);
        Assert.Equal(reply.DetailPath, replyResponse.Headers.Location?.OriginalString);

        using var postsResponse = await client.GetAsync($"{ForumApiEndpoints.RootPath}/topics/{created.Id}/posts");
        var posts = await postsResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ForumPostDto>>(JsonOptions);
        Assert.NotNull(posts);
        Assert.Equal(2, posts!.TotalCount);
        Assert.Contains("Hello", posts.Items[0].Body, StringComparison.Ordinal);
        Assert.Contains("<strong>", posts.Items[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", posts.Items[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plain reply from the app", posts.Items[1].Body, StringComparison.Ordinal);
        Assert.All(posts.Items, item => Assert.Equal("App Poster", item.AuthorUsername));

        var html = await factory.CreateAnonymousClient().GetStringAsync(created.DetailPath);
        Assert.Contains("App started this thread", html, StringComparison.Ordinal);
        Assert.Contains("Plain reply from the app", html, StringComparison.Ordinal);
        Assert.Contains("App Poster", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_post_returns_409_when_the_row_changed()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Conflict Poster");
        using var client = CreateBearerClient(memberId, "Conflict Poster");

        using var createdResponse = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            new { title = "Concurrency topic title", body = "Original body for the starter post." });
        var created = await createdResponse.Content.ReadFromJsonAsync<ForumTopicCreatedDto>(JsonOptions);
        Assert.NotNull(created);

        using var scope = factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var post = await posts.GetPostAsync(created!.StarterPostId);
        Assert.NotNull(post);

        var first = await posts.UpdatePostAsync(
            created.StarterPostId,
            memberId,
            "First writer saved this body.",
            isAdmin: false,
            editWindowMinutes: 60,
            post!.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.Success, first.Status);

        using var conflict = await client.PatchAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{created.Id}/posts/{created.StarterPostId}",
            new { body = "Stale overwrite from the app.", updatedAt = post.UpdatedAt });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(OptimisticConcurrencyException.UserMessage, problem.GetProperty("detail").GetString());

        var current = await posts.GetPostAsync(created.StarterPostId);
        Assert.Equal("First writer saved this body.", current!.Body);
    }

    [Fact]
    public async Task Patch_post_validates_auth_body_and_topic()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Patch Poster");
        using var client = CreateBearerClient(memberId, "Patch Poster");
        using var createdResponse = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            new { title = "Patch validation topic", body = "Starter body for patch checks." });
        var created = await createdResponse.Content.ReadFromJsonAsync<ForumTopicCreatedDto>(JsonOptions);
        Assert.NotNull(created);

        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var unauthorized = await anonymous.PatchAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{created!.Id}/posts/{created.StarterPostId}",
            new { body = "No token" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var emptyBody = await client.PatchAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{created.Id}/posts/{created.StarterPostId}",
            new { body = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, emptyBody.StatusCode);

        using var missing = await client.PatchAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{created.Id}/posts/999999",
            new { body = "Missing post body." });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var wrongTopic = await client.PatchAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1/posts/{created.StarterPostId}",
            new { body = "Wrong topic body." });
        Assert.Equal(HttpStatusCode.NotFound, wrongTopic.StatusCode);

        using var scope = factory.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var original = await posts.GetPostAsync(created.StarterPostId);
        using var ok = await client.PatchAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{created.Id}/posts/{created.StarterPostId}",
            new { body = "Patched from the app.", updatedAt = original!.UpdatedAt });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var payload = await ok.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        Assert.Equal(created.StarterPostId, payload!.Id);
        Assert.Contains($"#post-{created.StarterPostId}", payload.DetailPath, StringComparison.Ordinal);
        Assert.Equal("Patched from the app.", (await posts.GetPostAsync(created.StarterPostId))!.Body);
    }

    [Fact]
    public async Task Create_topic_rejects_short_title_and_empty_sanitized_body()
    {
        using var client = CreateBearerClient(Guid.NewGuid());

        using var shortTitle = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            new { title = "Hey", body = "This body is long enough." });
        Assert.Equal(HttpStatusCode.BadRequest, shortTitle.StatusCode);
        var shortProblem = await shortTitle.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            ForumPostWriteService.SubjectLengthMessage,
            shortProblem.GetProperty("detail").GetString(),
            StringComparison.Ordinal);

        using var emptyBody = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            new { title = "A valid title here", body = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyBody.StatusCode);
        var emptyProblem = await emptyBody.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            ForumPostWriteService.BodyRequiredMessage,
            emptyProblem.GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_topic_returns_not_found_for_missing_board()
    {
        using var client = CreateBearerClient(Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/9999/topics",
            new { title = "Missing board thread", body = "Should not persist." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Reply_returns_not_found_for_missing_topic()
    {
        using var client = CreateBearerClient(Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/9999/posts",
            new { body = "Missing topic reply." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("9999", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topic_detail_includes_isLocked_when_thread_is_locked()
    {
        using var lockedFactory = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<IForumWriteRepository>();
            services.AddSingleton<IForumWriteRepository>(new LockedForumWriteRepository());
        });
        using var client = lockedFactory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ForumApiEndpoints.RootPath}/topics/1002");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var topic = await response.Content.ReadFromJsonAsync<ForumTopicDetailDto>(JsonOptions);
        Assert.NotNull(topic);
        Assert.True(topic!.IsLocked);
    }

    [Fact]
    public async Task Reply_returns_forbidden_when_topic_is_locked()
    {
        using var lockedFactory = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<IForumWriteRepository>();
            services.AddSingleton<IForumWriteRepository>(new LockedForumWriteRepository());
        });
        using var client = CreateBearerClient(lockedFactory, Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Should be blocked on a locked topic." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ForumPostWriteService.TopicLockedMessage,
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_returns_unauthorized_when_member_is_suspended()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Suspended Fan", isSuspended: true);
        using var client = CreateBearerClient(memberId, "Suspended Fan");

        using var response = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Suspended members cannot post." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Writes_return_too_many_requests_after_shared_rate_limit()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(memberId);

        HttpResponseMessage? response = null;
        for (var i = 0; i < ForumPostRateLimiter.MaxPostsPerMinute + 1; i++)
        {
            response = await client.PostAsJsonAsync(
                $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
                new { body = $"Rate-limit reply {i}" });
        }

        Assert.NotNull(response);
        Assert.Equal((HttpStatusCode)429, response!.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.GetProperty("status").GetInt32());
        Assert.Equal(
            ForumPostWriteService.RateLimitedMessage,
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Multipart_write_reuses_attachment_validator()
    {
        using var client = CreateBearerClient(Guid.NewGuid());
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Attachment rejection thread", Encoding.UTF8), "title");
        content.Add(new StringContent("Body with a bad attachment", Encoding.UTF8), "body");
        var file = new ByteArrayContent("not-an-image"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-msdownload");
        content.Add(file, "attachments", "malware.exe");

        using var response = await client.PostAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not allowed", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reply_WithoutIdempotencyKey_KeepsOneShotBehavior()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(memberId);

        using var first = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "First one-shot reply." });
        using var second = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "First one-shot reply." });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        Assert.NotEqual(firstDto!.Id, secondDto!.Id);
    }

    [Fact]
    public async Task Reply_ReplaysOriginalSuccess_ForSameKeyAndPayload()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(memberId);
        var key = Guid.NewGuid();
        SetIdempotencyKey(client, key);

        using var first = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Idempotent forum reply." });
        using var second = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Idempotent forum reply." });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        Assert.Equal(firstDto!.Id, secondDto!.Id);
        Assert.Equal(firstDto.DetailPath, secondDto.DetailPath);
        Assert.Equal(first.Headers.Location?.OriginalString, second.Headers.Location?.OriginalString);
        Assert.Equal(firstDto.DetailPath, first.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Reply_SameKeyDifferentPayload_ReturnsConflict()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(memberId);
        var key = Guid.NewGuid();
        SetIdempotencyKey(client, key);

        using var first = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Original idempotent body." });
        using var conflict = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "A different idempotent body." });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(IdempotentApiWrites.ConflictDetail, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_ConcurrentDuplicateKey_SerializesToOnePost()
    {
        var memberId = Guid.NewGuid();
        var key = Guid.NewGuid();
        using var firstClient = CreateBearerClient(memberId);
        using var secondClient = CreateBearerClient(memberId);
        SetIdempotencyKey(firstClient, key);
        SetIdempotencyKey(secondClient, key);
        var body = new { body = "Concurrent idempotent reply." };

        var firstTask = firstClient.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            body);
        var secondTask = secondClient.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            body);
        using var first = await firstTask;
        using var second = await secondTask;

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        Assert.Equal(firstDto!.Id, secondDto!.Id);
    }

    [Fact]
    public async Task Reply_InvalidIdempotencyKey_IsBadRequest()
    {
        using var client = CreateBearerClient(Guid.NewGuid());
        client.DefaultRequestHeaders.TryAddWithoutValidation(IdempotentApiWrites.HeaderName, "not-a-uuid");

        using var response = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Should not persist." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(IdempotentApiWrites.InvalidKeyDetail, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_ExpiredKey_IsTreatedAsNewWrite()
    {
        var memberId = Guid.NewGuid();
        var key = Guid.NewGuid();
        var store = Assert.IsType<InMemoryIdempotencyStore>(
            factory.Services.GetRequiredService<IIdempotencyStore>());
        store.SeedExpired(
            memberId,
            IdempotencyOperationKinds.ForumCreateReply,
            key,
            new IdempotencyReceipt(201, "/old", """{"id":1}""", "stale"),
            DateTimeOffset.UtcNow.AddMinutes(-5));

        using var client = CreateBearerClient(memberId);
        SetIdempotencyKey(client, key);
        using var response = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/1002/posts",
            new { body = "Expired key should create a new post." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ForumPostCreatedDto>(JsonOptions);
        Assert.True(created!.Id > 1);
        Assert.NotEqual("/old", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateTopic_ReplaysOriginalSuccess_ForSameKeyAndPayload()
    {
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(memberId);
        var key = Guid.NewGuid();
        SetIdempotencyKey(client, key);
        var payload = new { title = "Idempotent topic title", body = "Starter post body here." };

        using var first = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            payload);
        using var second = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/categories/1/topics",
            payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ForumTopicCreatedDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ForumTopicCreatedDto>(JsonOptions);
        Assert.Equal(firstDto!.Id, secondDto!.Id);
        Assert.Equal(first.Headers.Location?.OriginalString, second.Headers.Location?.OriginalString);
    }

    private static void SetIdempotencyKey(HttpClient client, Guid key) =>
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            IdempotentApiWrites.HeaderName,
            key.ToString("D"));

    private HttpClient CreateBearerClient(Guid memberId, string displayName = "Forum Fan") =>
        CreateBearerClient(factory, memberId, displayName);

    private HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory source,
        Guid memberId,
        string displayName = "Forum Fan")
    {
        MemberBearerAccounts.Ensure(source.Services, memberId, $"{memberId:N}@example.test", displayName);
        using var scope = source.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = source.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedMemberAsync(Guid memberId, string displayName, bool isSuspended = false)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        await repository.CreateAsync(new MemberAccount
        {
            Id = memberId,
            Email = $"{memberId:N}@example.test",
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
            IsSuspended = isSuspended,
        });
    }

    private sealed class LockedForumWriteRepository : IForumWriteRepository
    {
        public Task<ForumThreadCreateResult> CreateThreadAsync(
            NewForumThread thread,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumThreadCreateResult(200_001, 2_000_001));

        public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Locked.");

        public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumEditablePost?>(null);

        public Task<ForumPostUpdateResult> UpdatePostAsync(
            int postId,
            Guid editorMemberId,
            string sanitisedBody,
            bool isAdmin,
            int editWindowMinutes,
            DateTimeOffset? expectedUpdatedAt = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.Forbidden));

        public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumWriteThread?>(new ForumWriteThread(
                topicId,
                1,
                "Ranking every studio album",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1,
                IsLocked: true));

        public Task<int> CountPostsByMemberSinceAsync(
            Guid memberId,
            DateTimeOffset since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CountApprovedPostsByMemberAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task HideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnhideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> EnsureCategoryAsync(
            string slug,
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
