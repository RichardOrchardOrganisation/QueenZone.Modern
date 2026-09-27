using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MessagesApiTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory limitedFactory;

    public MessagesApiTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        limitedFactory = variants.Get(WebHostVariants.TestingPrivateMessageRateLimit1);
    }

    [Fact]
    public async Task Inbox_RequiresMobileBearer_NotCookie()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var inbox = await client.GetAsync(MessagesApiEndpoints.Path);
            Assert.Equal(HttpStatusCode.Unauthorized, inbox.StatusCode);
            Assert.Equal("application/problem+json", inbox.Content.Headers.ContentType?.MediaType);

            using var unread = await client.GetAsync(MessagesApiEndpoints.UnreadCountPath);
            Assert.Equal(HttpStatusCode.Unauthorized, unread.StatusCode);
        }
    }

    [Fact]
    public async Task Inbox_Empty_UsesWebsitePageSize()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Empty Inbox", "api-empty-inbox@example.com");
        using var client = CreateBearerClient(memberId, "Empty Inbox", "api-empty-inbox@example.com");

        using var response = await client.GetAsync(MessagesApiEndpoints.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var page = await response.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        Assert.NotNull(page);
        Assert.Empty(page!.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(PrivateMessageLimits.InboxPageSize, page.PageSize);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task Inbox_UnreadCounts_MatchWebsiteAndHeaderBadge()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-unread-alice@example.com",
            "API Unread Alice",
            "api-unread-bob@example.com",
            "API Unread Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Hello from Alice");
        Assert.True(sent.Succeeded);

        using var bobApi = CreateBearerClient(bobId, "API Unread Bob", "api-unread-bob@example.com");
        using var bobWeb = CreateCookieClient(bobId, "API Unread Bob", "api-unread-bob@example.com");

        using var inboxResponse = await bobApi.GetAsync(MessagesApiEndpoints.Path);
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        var row = Assert.Single(inbox!.Items);
        Assert.Equal(sent.ConversationId, row.ConversationId);
        Assert.Equal(aliceId, row.OtherParticipantId);
        Assert.Equal("API Unread Alice", row.OtherParticipantDisplayName);
        Assert.Equal("Hello from Alice", row.LastMessagePreview);
        Assert.True(row.HasUnread);
        Assert.Equal(1, row.UnreadCount);
        Assert.Equal($"/messages/{sent.ConversationId:D}", row.DetailPath);

        using var unreadResponse = await bobApi.GetAsync(MessagesApiEndpoints.UnreadCountPath);
        Assert.Contains("no-store", unreadResponse.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var unread = await unreadResponse.Content.ReadFromJsonAsync<UnreadConversationsDto>(JsonOptions);
        Assert.Equal(1, unread!.UnreadConversationCount);
        Assert.Equal(1, await service.CountUnreadConversationsAsync(bobId));

        var html = await bobWeb.GetStringAsync("/messages");
        Assert.Contains("API Unread Alice", html, StringComparison.Ordinal);
        Assert.Contains("1 unread", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Messages, 1 unread conversations\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversation_MarksRead_MatchingWebsiteOpen()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-read-alice@example.com",
            "API Read Alice",
            "api-read-bob@example.com",
            "API Read Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Please read this");
        var conversationId = sent.ConversationId!.Value;

        using var bobApi = CreateBearerClient(bobId, "API Read Bob", "api-read-bob@example.com");
        using var conversationResponse = await bobApi.GetAsync(MessagesApiEndpoints.ConversationPath(conversationId));

        Assert.Equal(HttpStatusCode.OK, conversationResponse.StatusCode);
        Assert.Contains("no-store", conversationResponse.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var detail = await conversationResponse.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(conversationId, detail!.ConversationId);
        Assert.Equal(aliceId, detail.OtherParticipantId);
        Assert.Equal("API Read Alice", detail.OtherParticipantDisplayName);
        Assert.Equal($"/messages/{conversationId:D}", detail.DetailPath);
        var message = Assert.Single(detail.Messages);
        Assert.Equal("Please read this", message.Body);
        Assert.False(message.IsMine);
        Assert.Equal(aliceId, message.SenderMemberId);
        Assert.True(detail.CanSendReply);

        Assert.Equal(0, await service.CountUnreadConversationsAsync(bobId));

        using var inboxResponse = await bobApi.GetAsync(MessagesApiEndpoints.Path);
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        var row = Assert.Single(inbox!.Items);
        Assert.False(row.HasUnread);
        Assert.Equal(0, row.UnreadCount);

        using var unreadResponse = await bobApi.GetAsync(MessagesApiEndpoints.UnreadCountPath);
        var unread = await unreadResponse.Content.ReadFromJsonAsync<UnreadConversationsDto>(JsonOptions);
        Assert.Equal(0, unread!.UnreadConversationCount);

        using var bobWeb = CreateCookieClient(bobId, "API Read Bob", "api-read-bob@example.com");
        var html = await bobWeb.GetStringAsync("/messages");
        Assert.DoesNotContain("1 unread", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Messages\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conversation_ReturnsMarkupBodiesAsPlainText()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-xss-alice@example.com",
            "API Xss Alice",
            "api-xss-bob@example.com",
            "API Xss Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        const string markup = "<script>alert(1)</script>";
        var sent = await service.ComposeAsync(aliceId, bobId, markup);
        Assert.True(sent.Succeeded);

        using var bob = CreateBearerClient(bobId, "API Xss Bob", "api-xss-bob@example.com");
        using var conversationResponse = await bob.GetAsync(
            MessagesApiEndpoints.ConversationPath(sent.ConversationId!.Value));
        var detail = await conversationResponse.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(markup, Assert.Single(detail!.Messages).Body);

        using var inboxResponse = await bob.GetAsync(MessagesApiEndpoints.Path);
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        Assert.Equal(markup, Assert.Single(inbox!.Items).LastMessagePreview);
    }

    [Fact]
    public async Task GetConversation_ReturnsNotFound_ForNonParticipantAndUnknownId()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-404-alice@example.com",
            "API 404 Alice",
            "api-404-bob@example.com",
            "API 404 Bob");
        var outsiderId = Guid.NewGuid();
        await SeedMemberAsync(outsiderId, "API 404 Outsider", "api-404-outsider@example.com");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Private");

        using var outsider = CreateBearerClient(outsiderId, "API 404 Outsider", "api-404-outsider@example.com");
        using var hidden = await outsider.GetAsync(MessagesApiEndpoints.ConversationPath(sent.ConversationId!.Value));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("application/problem+json", hidden.Content.Headers.ContentType?.MediaType);

        using var bob = CreateBearerClient(bobId, "API 404 Bob", "api-404-bob@example.com");
        using var missing = await bob.GetAsync(MessagesApiEndpoints.ConversationPath(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status404NotFound, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Inbox_ClampsPaging_AndConversationDefaultsToLatestPage()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-page-alice@example.com",
            "API Page Alice",
            "api-page-bob@example.com",
            "API Page Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(aliceId, bobId, "Msg 1");
        var conversationId = created.ConversationId!.Value;
        Assert.True((await service.ReplyAsync(conversationId, aliceId, "Msg 2")).Succeeded);
        Assert.True((await service.ReplyAsync(conversationId, aliceId, "Msg 3")).Succeeded);

        using var bob = CreateBearerClient(bobId, "API Page Bob", "api-page-bob@example.com");
        using var clamped = await bob.GetAsync($"{MessagesApiEndpoints.Path}?page=0&pageSize=1000");
        var inbox = await clamped.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        Assert.Equal(1, inbox!.Page);
        Assert.Equal(PrivateMessageLimits.MaxInboxPageSize, inbox.PageSize);

        using var latest = await bob.GetAsync(
            $"{MessagesApiEndpoints.ConversationPath(conversationId)}?pageSize=2");
        var latestPage = await latest.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(2, latestPage!.Page);
        Assert.Equal(2, latestPage.PageSize);
        Assert.Equal(3, latestPage.TotalCount);
        Assert.Equal(["Msg 2", "Msg 3"], latestPage.Messages.Select(item => item.Body).ToArray());

        using var first = await bob.GetAsync(
            $"{MessagesApiEndpoints.ConversationPath(conversationId)}?page=1&pageSize=2");
        var firstPage = await first.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(1, firstPage!.Page);
        Assert.Equal(["Msg 1", "Msg 2"], firstPage.Messages.Select(item => item.Body).ToArray());
    }

    [Fact]
    public async Task GetConversation_ReturnsHasBlockedOtherParticipant_WhenViewerBlockedSender()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-viewer-block-alice@example.com",
            "API Viewer Block Alice",
            "api-viewer-block-bob@example.com",
            "API Viewer Block Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Hello");
        var conversationId = sent.ConversationId!.Value;
        Assert.True((await service.BlockAsync(bobId, aliceId)).Succeeded);

        using var bob = CreateBearerClient(bobId, "API Viewer Block Bob", "api-viewer-block-bob@example.com");
        using var get = await bob.GetAsync(MessagesApiEndpoints.ConversationPath(conversationId));
        var detail = await get.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.True(detail!.HasBlockedOtherParticipant);
        Assert.False(detail.CanSendReply);
    }

    [Fact]
    public async Task Archived_ArchiveThenUnarchive_MatchesWebsiteBehavior()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-archive-alice@example.com",
            "API Archive Alice",
            "api-archive-bob@example.com",
            "API Archive Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Archive me");
        var conversationId = sent.ConversationId!.Value;

        using var bob = CreateBearerClient(bobId, "API Archive Bob", "api-archive-bob@example.com");

        using var emptyArchived = await bob.GetAsync(MessagesApiEndpoints.ArchivedPath);
        var emptyArchivedPage = await emptyArchived.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        Assert.Empty(emptyArchivedPage!.Items);

        using var archive = await bob.PostAsync(MessagesApiEndpoints.ArchivePath(conversationId), null);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.Contains("no-store", archive.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        using var inboxAfterArchive = await bob.GetAsync(MessagesApiEndpoints.Path);
        var inboxAfterArchivePage = await inboxAfterArchive.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        Assert.Empty(inboxAfterArchivePage!.Items);

        using var archivedAfterArchive = await bob.GetAsync(MessagesApiEndpoints.ArchivedPath);
        var archivedAfterArchivePage = await archivedAfterArchive.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        var archivedRow = Assert.Single(archivedAfterArchivePage!.Items);
        Assert.Equal(conversationId, archivedRow.ConversationId);

        using var bobWeb = CreateCookieClient(bobId, "API Archive Bob", "api-archive-bob@example.com");
        var inboxHtml = await bobWeb.GetStringAsync("/messages");
        Assert.DoesNotContain("API Archive Alice", inboxHtml, StringComparison.Ordinal);
        var archivedHtml = await bobWeb.GetStringAsync("/messages/archived");
        Assert.Contains("API Archive Alice", archivedHtml, StringComparison.Ordinal);

        using var unarchive = await bob.PostAsync(MessagesApiEndpoints.UnarchivePath(conversationId), null);
        Assert.Equal(HttpStatusCode.NoContent, unarchive.StatusCode);

        using var inboxAfterUnarchive = await bob.GetAsync(MessagesApiEndpoints.Path);
        var inboxAfterUnarchivePage = await inboxAfterUnarchive.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        var inboxRow = Assert.Single(inboxAfterUnarchivePage!.Items);
        Assert.Equal(conversationId, inboxRow.ConversationId);

        using var archivedAfterUnarchive = await bob.GetAsync(MessagesApiEndpoints.ArchivedPath);
        var archivedAfterUnarchivePage = await archivedAfterUnarchive.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        Assert.Empty(archivedAfterUnarchivePage!.Items);
    }

    [Fact]
    public async Task Archive_And_Unarchive_ReturnNotFound_ForNonParticipant()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-archive-404-alice@example.com",
            "API Archive 404 Alice",
            "api-archive-404-bob@example.com",
            "API Archive 404 Bob");
        var outsiderId = Guid.NewGuid();
        await SeedMemberAsync(outsiderId, "API Archive 404 Outsider", "api-archive-404-outsider@example.com");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Private");

        using var outsider = CreateBearerClient(outsiderId, "API Archive 404 Outsider", "api-archive-404-outsider@example.com");
        using var archiveHidden = await outsider.PostAsync(
            MessagesApiEndpoints.ArchivePath(sent.ConversationId!.Value),
            null);
        Assert.Equal(HttpStatusCode.NotFound, archiveHidden.StatusCode);

        using var bob = CreateBearerClient(bobId, "API Archive 404 Bob", "api-archive-404-bob@example.com");
        using var archiveMissing = await bob.PostAsync(MessagesApiEndpoints.ArchivePath(Guid.NewGuid()), null);
        Assert.Equal(HttpStatusCode.NotFound, archiveMissing.StatusCode);

        using var unarchiveMissing = await bob.PostAsync(MessagesApiEndpoints.UnarchivePath(Guid.NewGuid()), null);
        Assert.Equal(HttpStatusCode.NotFound, unarchiveMissing.StatusCode);
    }

    [Fact]
    public async Task Block_Then_Unblock_MatchesWebsiteBehavior()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-block-alice@example.com",
            "API Block Alice",
            "api-block-bob@example.com",
            "API Block Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Hello");
        var conversationId = sent.ConversationId!.Value;

        using var bob = CreateBearerClient(bobId, "API Block Bob", "api-block-bob@example.com");

        using var block = await bob.PostAsync(MessagesApiEndpoints.BlockPath(conversationId), null);
        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        Assert.Contains("no-store", block.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(await service.HasBlockedAsync(bobId, aliceId));

        using var get = await bob.GetAsync(MessagesApiEndpoints.ConversationPath(conversationId));
        var detail = await get.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.True(detail!.HasBlockedOtherParticipant);

        using var unblock = await bob.PostAsync(MessagesApiEndpoints.UnblockPath(conversationId), null);
        Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        Assert.False(await service.HasBlockedAsync(bobId, aliceId));
    }

    [Fact]
    public async Task Block_And_Unblock_ReturnNotFound_ForNonParticipant()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-block-404-alice@example.com",
            "API Block 404 Alice",
            "api-block-404-bob@example.com",
            "API Block 404 Bob");
        var outsiderId = Guid.NewGuid();
        await SeedMemberAsync(outsiderId, "API Block 404 Outsider", "api-block-404-outsider@example.com");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Private");

        using var outsider = CreateBearerClient(outsiderId, "API Block 404 Outsider", "api-block-404-outsider@example.com");
        using var blockHidden = await outsider.PostAsync(
            MessagesApiEndpoints.BlockPath(sent.ConversationId!.Value),
            null);
        Assert.Equal(HttpStatusCode.NotFound, blockHidden.StatusCode);

        using var bob = CreateBearerClient(bobId, "API Block 404 Bob", "api-block-404-bob@example.com");
        using var blockMissing = await bob.PostAsync(MessagesApiEndpoints.BlockPath(Guid.NewGuid()), null);
        Assert.Equal(HttpStatusCode.NotFound, blockMissing.StatusCode);

        using var unblockMissing = await bob.PostAsync(MessagesApiEndpoints.UnblockPath(Guid.NewGuid()), null);
        Assert.Equal(HttpStatusCode.NotFound, unblockMissing.StatusCode);
    }

    [Fact]
    public async Task Archived_RequiresMobileBearer_NotCookie()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var archived = await client.GetAsync(MessagesApiEndpoints.ArchivedPath);
            Assert.Equal(HttpStatusCode.Unauthorized, archived.StatusCode);

            using var archive = await client.PostAsync(MessagesApiEndpoints.ArchivePath(Guid.NewGuid()), null);
            Assert.Equal(HttpStatusCode.Unauthorized, archive.StatusCode);

            using var unarchive = await client.PostAsync(MessagesApiEndpoints.UnarchivePath(Guid.NewGuid()), null);
            Assert.Equal(HttpStatusCode.Unauthorized, unarchive.StatusCode);
        }
    }

    [Fact]
    public async Task Reply_RequiresMobileBearer_NotCookie()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var response = await client.PostAsJsonAsync(
                MessagesApiEndpoints.ConversationPath(Guid.NewGuid()),
                new { body = "Should not send." });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Reply_UsesSameSortKeyOrderAsWebsitePost()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-reply-alice@example.com",
            "API Reply Alice",
            "api-reply-bob@example.com",
            "API Reply Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Msg 1");
        var conversationId = sent.ConversationId!.Value;
        var path = MessagesApiEndpoints.ConversationPath(conversationId);

        using var bobApi = CreateBearerClient(bobId, "API Reply Bob", "api-reply-bob@example.com");
        using var created = await bobApi.PostAsJsonAsync(path, new { body = "Msg 2" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/messages/{conversationId:D}", created.Headers.Location?.OriginalString);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var afterAppReply = await created.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.True(afterAppReply!.CanSendReply);
        Assert.Equal(["Msg 1", "Msg 2"], afterAppReply.Messages.Select(item => item.Body).ToArray());
        Assert.True(afterAppReply.Messages[0].SortKey < afterAppReply.Messages[1].SortKey);
        Assert.True(afterAppReply.Messages[1].IsMine);

        using var aliceWeb = CreateCookieClient(aliceId, "API Reply Alice", "api-reply-alice@example.com");
        var conversationHtml = await aliceWeb.GetStringAsync($"/messages/{conversationId:D}");
        using var webReply = await aliceWeb.PostAsync(
            $"/messages/{conversationId:D}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(conversationHtml),
                ["Input.Body"] = "Msg 3",
            }));
        Assert.Equal(HttpStatusCode.Redirect, webReply.StatusCode);

        using var latest = await bobApi.GetAsync(path);
        var detail = await latest.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(["Msg 1", "Msg 2", "Msg 3"], detail!.Messages.Select(item => item.Body).ToArray());
        Assert.True(detail.Messages[0].SortKey < detail.Messages[1].SortKey);
        Assert.True(detail.Messages[1].SortKey < detail.Messages[2].SortKey);
        Assert.False(detail.Messages[2].IsMine);

        var html = await aliceWeb.GetStringAsync($"/messages/{conversationId:D}");
        Assert.Contains("Msg 1", html, StringComparison.Ordinal);
        Assert.Contains("Msg 2", html, StringComparison.Ordinal);
        Assert.Contains("Msg 3", html, StringComparison.Ordinal);
        var first = html.IndexOf("Msg 1", StringComparison.Ordinal);
        var second = html.IndexOf("Msg 2", StringComparison.Ordinal);
        var third = html.IndexOf("Msg 3", StringComparison.Ordinal);
        Assert.True(first < second && second < third);
    }

    [Fact]
    public async Task Reply_WithoutIdempotencyKey_KeepsOneShotBehavior()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-idemp-once-alice@example.com",
            "API Idem Once Alice",
            "api-idemp-once-bob@example.com",
            "API Idem Once Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Start");
        var conversationId = sent.ConversationId!.Value;
        var path = MessagesApiEndpoints.ConversationPath(conversationId);
        using var bob = CreateBearerClient(bobId, "API Idem Once Bob", "api-idemp-once-bob@example.com");

        using var first = await bob.PostAsJsonAsync(path, new { body = "Same reply twice." });
        using var second = await bob.PostAsJsonAsync(path, new { body = "Same reply twice." });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(2, firstDto!.TotalCount);
        Assert.Equal(3, secondDto!.TotalCount);
    }

    [Fact]
    public async Task Reply_ReplaysOriginalSuccess_ForSameKeyAndPayload()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-idemp-reply-alice@example.com",
            "API Idem Reply Alice",
            "api-idemp-reply-bob@example.com",
            "API Idem Reply Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Start");
        var conversationId = sent.ConversationId!.Value;
        var path = MessagesApiEndpoints.ConversationPath(conversationId);
        using var bob = CreateBearerClient(bobId, "API Idem Reply Bob", "api-idemp-reply-bob@example.com");
        SetIdempotencyKey(bob, Guid.NewGuid());

        using var first = await bob.PostAsJsonAsync(path, new { body = "Idempotent PM reply." });
        using var second = await bob.PostAsJsonAsync(path, new { body = "Idempotent PM reply." });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(firstDto!.TotalCount, secondDto!.TotalCount);
        Assert.Equal(2, firstDto.TotalCount);
        Assert.Equal(first.Headers.Location?.OriginalString, second.Headers.Location?.OriginalString);
        Assert.Equal($"/messages/{conversationId:D}", first.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Reply_SameKeyDifferentPayload_ReturnsConflict()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-idemp-conflict-alice@example.com",
            "API Idem Conflict Alice",
            "api-idemp-conflict-bob@example.com",
            "API Idem Conflict Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Start");
        var conversationId = sent.ConversationId!.Value;
        var path = MessagesApiEndpoints.ConversationPath(conversationId);
        using var bob = CreateBearerClient(bobId, "API Idem Conflict Bob", "api-idemp-conflict-bob@example.com");
        SetIdempotencyKey(bob, Guid.NewGuid());

        using var first = await bob.PostAsJsonAsync(path, new { body = "Original PM." });
        using var conflict = await bob.PostAsJsonAsync(path, new { body = "Different PM." });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(IdempotentApiWrites.ConflictDetail, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_ConcurrentDuplicateKey_SerializesToOneMessage()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-idemp-race-alice@example.com",
            "API Idem Race Alice",
            "api-idemp-race-bob@example.com",
            "API Idem Race Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Start");
        var conversationId = sent.ConversationId!.Value;
        var path = MessagesApiEndpoints.ConversationPath(conversationId);
        var key = Guid.NewGuid();
        using var firstClient = CreateBearerClient(bobId, "API Idem Race Bob", "api-idemp-race-bob@example.com");
        using var secondClient = CreateBearerClient(bobId, "API Idem Race Bob", "api-idemp-race-bob@example.com");
        SetIdempotencyKey(firstClient, key);
        SetIdempotencyKey(secondClient, key);
        var body = new { body = "Concurrent PM reply." };

        var firstTask = firstClient.PostAsJsonAsync(path, body);
        var secondTask = secondClient.PostAsJsonAsync(path, body);
        using var first = await firstTask;
        using var second = await secondTask;

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(firstDto!.TotalCount, secondDto!.TotalCount);
        Assert.Equal(2, firstDto.TotalCount);
    }

    [Fact]
    public async Task Reply_ExpiredKey_IsTreatedAsNewWrite()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-idemp-expired-alice@example.com",
            "API Idem Expired Alice",
            "api-idemp-expired-bob@example.com",
            "API Idem Expired Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Start");
        var conversationId = sent.ConversationId!.Value;
        var key = Guid.NewGuid();
        var store = Assert.IsType<InMemoryIdempotencyStore>(
            factory.Services.GetRequiredService<IIdempotencyStore>());
        store.SeedExpired(
            bobId,
            IdempotencyOperationKinds.MessageReply,
            key,
            new IdempotencyReceipt(201, "/messages/old", "{}", "stale"),
            DateTimeOffset.UtcNow.AddMinutes(-5));

        using var bob = CreateBearerClient(bobId, "API Idem Expired Bob", "api-idemp-expired-bob@example.com");
        SetIdempotencyKey(bob, key);
        using var response = await bob.PostAsJsonAsync(
            MessagesApiEndpoints.ConversationPath(conversationId),
            new { body = "Expired key should send again." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(2, detail!.TotalCount);
        Assert.NotEqual("/messages/old", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Compose_ReplaysOriginalSuccess_ForSameKeyAndPayload()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-idemp-compose-alice@example.com",
            "API Idem Compose Alice",
            "api-idemp-compose-bob@example.com",
            "API Idem Compose Bob");
        using var alice = CreateBearerClient(aliceId, "API Idem Compose Alice", "api-idemp-compose-alice@example.com");
        SetIdempotencyKey(alice, Guid.NewGuid());
        var payload = new { recipientMemberId = bobId, body = "Idempotent first message." };

        using var first = await alice.PostAsJsonAsync(MessagesApiEndpoints.Path, payload);
        using var second = await alice.PostAsJsonAsync(MessagesApiEndpoints.Path, payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstDto = await first.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        var secondDto = await second.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.Equal(firstDto!.ConversationId, secondDto!.ConversationId);
        Assert.Equal(firstDto.TotalCount, secondDto.TotalCount);
        Assert.Equal(1, firstDto.TotalCount);
        Assert.Equal(first.Headers.Location?.OriginalString, second.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Reply_ReturnsNotFound_ForNonParticipant()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-reply-404-alice@example.com",
            "API Reply 404 Alice",
            "api-reply-404-bob@example.com",
            "API Reply 404 Bob");
        var outsiderId = Guid.NewGuid();
        await SeedMemberAsync(outsiderId, "API Reply 404 Outsider", "api-reply-404-outsider@example.com");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Private");

        using var outsider = CreateBearerClient(outsiderId, "API Reply 404 Outsider", "api-reply-404-outsider@example.com");
        using var hidden = await outsider.PostAsJsonAsync(
            MessagesApiEndpoints.ConversationPath(sent.ConversationId!.Value),
            new { body = "Intruder" });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("application/problem+json", hidden.Content.Headers.ContentType?.MediaType);

        using var bob = CreateBearerClient(bobId, "API Reply 404 Bob", "api-reply-404-bob@example.com");
        using var missing = await bob.PostAsJsonAsync(
            MessagesApiEndpoints.ConversationPath(Guid.NewGuid()),
            new { body = "Gone" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status404NotFound, problem.GetProperty("status").GetInt32());
        Assert.Equal("Conversation was not found.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_RejectsEmptyAndOversizedBody()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-reply-body-alice@example.com",
            "API Reply Body Alice",
            "api-reply-body-bob@example.com",
            "API Reply Body Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Hello");
        var path = MessagesApiEndpoints.ConversationPath(sent.ConversationId!.Value);
        using var bob = CreateBearerClient(bobId, "API Reply Body Bob", "api-reply-body-bob@example.com");

        using var empty = await bob.PostAsJsonAsync(path, new { body = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var emptyProblem = await empty.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Message body is required.", emptyProblem.GetProperty("detail").GetString());

        using var missingBody = await bob.PostAsync(path, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, missingBody.StatusCode);

        using var tooLong = await bob.PostAsJsonAsync(
            path,
            new { body = new string('a', PrivateMessageLimits.MaxBodyLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var longProblem = await tooLong.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("4000", longProblem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var noJson = await bob.PostAsync(path, new StringContent(string.Empty, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, noJson.StatusCode);
        var noJsonProblem = await noJson.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("A JSON body is required.", noJsonProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_ReturnsForbidden_WhenMessagingIsBlocked()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-reply-block-alice@example.com",
            "API Reply Block Alice",
            "api-reply-block-bob@example.com",
            "API Reply Block Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Hello");
        var conversationId = sent.ConversationId!.Value;
        Assert.True((await service.BlockAsync(aliceId, bobId)).Succeeded);

        using var bob = CreateBearerClient(bobId, "API Reply Block Bob", "api-reply-block-bob@example.com");
        using var get = await bob.GetAsync(MessagesApiEndpoints.ConversationPath(conversationId));
        var detail = await get.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.False(detail!.CanSendReply);

        using var reply = await bob.PostAsJsonAsync(
            MessagesApiEndpoints.ConversationPath(conversationId),
            new { body = "Should fail" });
        Assert.Equal(HttpStatusCode.Forbidden, reply.StatusCode);
        var problem = await reply.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PrivateMessageService.UnableToSendMessage, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reply_ReturnsTooManyRequests_AfterSharedRateLimit()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        await SeedMemberAsync(limitedFactory, aliceId, "API Reply Limit Alice", "api-reply-limit-alice@example.com");
        await SeedMemberAsync(limitedFactory, bobId, "API Reply Limit Bob", "api-reply-limit-bob@example.com");
        var service = limitedFactory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Msg 1");
        var path = MessagesApiEndpoints.ConversationPath(sent.ConversationId!.Value);
        using var alice = CreateBearerClient(
            limitedFactory,
            aliceId,
            "API Reply Limit Alice",
            "api-reply-limit-alice@example.com");

        using var response = await alice.PostAsJsonAsync(path, new { body = "Msg 2" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.GetProperty("status").GetInt32());
        Assert.Equal(PrivateMessageService.RateLimitedMessage, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Recipients_RequiresMobileBearer_NotCookie()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var response = await client.GetAsync($"{MessagesApiEndpoints.RecipientsPath}?q=Fan");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Recipients_MatchesWebsiteComposeSearch_AndExcludesSelf()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-search-alice@example.com",
            "API Search Alice",
            "api-search-bob@example.com",
            "API Search Bob");
        var carolId = Guid.NewGuid();
        await SeedMemberAsync(carolId, "API Search Carol", "api-search-carol@example.com");

        using var aliceApi = CreateBearerClient(aliceId, "API Search Alice", "api-search-alice@example.com");
        using var empty = await aliceApi.GetAsync($"{MessagesApiEndpoints.RecipientsPath}?q=");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("no-store", empty.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var emptyPayload = await empty.Content.ReadFromJsonAsync<MessageRecipientsDto>(JsonOptions);
        Assert.Empty(emptyPayload!.Items);

        using var search = await aliceApi.GetAsync($"{MessagesApiEndpoints.RecipientsPath}?q=API%20Search");
        var matches = await search.Content.ReadFromJsonAsync<MessageRecipientsDto>(JsonOptions);
        Assert.Equal(2, matches!.Items.Count);
        Assert.DoesNotContain(matches.Items, item => item.MemberId == aliceId);
        Assert.Contains(matches.Items, item => item.MemberId == bobId && item.DisplayName == "API Search Bob");
        Assert.Contains(matches.Items, item => item.MemberId == carolId && item.DisplayName == "API Search Carol");

        using var aliceWeb = CreateCookieClient(aliceId, "API Search Alice", "api-search-alice@example.com");
        var html = await aliceWeb.GetStringAsync("/messages/compose?q=API%20Search");
        Assert.Contains("API Search Bob", html, StringComparison.Ordinal);
        Assert.Contains("API Search Carol", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/messages/compose?to={aliceId:D}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compose_RequiresMobileBearer_NotCookie()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var response = await client.PostAsJsonAsync(
                MessagesApiEndpoints.Path,
                new { recipientMemberId = Guid.NewGuid(), body = "Hello" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Compose_CreatesConversation_MatchingWebsiteCompose()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-compose-alice@example.com",
            "API Compose Alice",
            "api-compose-bob@example.com",
            "API Compose Bob");

        using var aliceApi = CreateBearerClient(aliceId, "API Compose Alice", "api-compose-alice@example.com");
        using var created = await aliceApi.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = bobId, body = "Hello from app compose" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var detail = await created.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(bobId, detail!.OtherParticipantId);
        Assert.Equal("API Compose Bob", detail.OtherParticipantDisplayName);
        Assert.Equal($"/messages/{detail.ConversationId:D}", detail.DetailPath);
        Assert.Equal(created.Headers.Location?.OriginalString, detail.DetailPath);
        Assert.True(detail.CanSendReply);
        var message = Assert.Single(detail.Messages);
        Assert.Equal("Hello from app compose", message.Body);
        Assert.True(message.IsMine);
        Assert.Equal(aliceId, message.SenderMemberId);

        using var bobApi = CreateBearerClient(bobId, "API Compose Bob", "api-compose-bob@example.com");
        using var inboxResponse = await bobApi.GetAsync(MessagesApiEndpoints.Path);
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<ApiPagedResponse<InboxConversationDto>>(JsonOptions);
        var row = Assert.Single(inbox!.Items);
        Assert.Equal(detail.ConversationId, row.ConversationId);
        Assert.True(row.HasUnread);
        Assert.Equal(1, row.UnreadCount);

        using var aliceWeb = CreateCookieClient(aliceId, "API Compose Alice", "api-compose-alice@example.com");
        var html = await aliceWeb.GetStringAsync($"/messages/{detail.ConversationId:D}");
        Assert.Contains("Hello from app compose", html, StringComparison.Ordinal);
        Assert.Contains("API Compose Bob", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compose_RejectsMissingRecipient_Self_AndInvalidBodies()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-compose-body-alice@example.com",
            "API Compose Body Alice",
            "api-compose-body-bob@example.com",
            "API Compose Body Bob");
        using var alice = CreateBearerClient(aliceId, "API Compose Body Alice", "api-compose-body-alice@example.com");

        using var noRecipient = await alice.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { body = "Hello" });
        Assert.Equal(HttpStatusCode.BadRequest, noRecipient.StatusCode);
        var noRecipientProblem = await noRecipient.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Choose a recipient.", noRecipientProblem.GetProperty("detail").GetString());

        using var self = await alice.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = aliceId, body = "Hello me" });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        var selfProblem = await self.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("You cannot message yourself.", selfProblem.GetProperty("detail").GetString());

        using var empty = await alice.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = bobId, body = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var emptyProblem = await empty.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Message body is required.", emptyProblem.GetProperty("detail").GetString());

        using var tooLong = await alice.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = bobId, body = new string('a', PrivateMessageLimits.MaxBodyLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        using var missing = await alice.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = Guid.NewGuid(), body = "Hello" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Recipient was not found.", missingProblem.GetProperty("detail").GetString());

        using var noJson = await alice.PostAsync(
            MessagesApiEndpoints.Path,
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, noJson.StatusCode);
        var noJsonProblem = await noJson.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("A JSON body is required.", noJsonProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Compose_ReturnsForbidden_WhenBlockedOrPrivacyDisallows()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-compose-block-alice@example.com",
            "API Compose Block Alice",
            "api-compose-block-bob@example.com",
            "API Compose Block Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        Assert.True((await service.BlockAsync(bobId, aliceId)).Succeeded);

        using var alice = CreateBearerClient(aliceId, "API Compose Block Alice", "api-compose-block-alice@example.com");
        using var blocked = await alice.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = bobId, body = "Should fail" });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var blockedProblem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PrivateMessageService.UnableToSendMessage, blockedProblem.GetProperty("detail").GetString());

        var (carolId, daveId) = await SeedConversationPairAsync(
            "api-compose-privacy-carol@example.com",
            "API Compose Privacy Carol",
            "api-compose-privacy-dave@example.com",
            "API Compose Privacy Dave");
        using (var scope = factory.Services.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
            await members.UpdateMessagePrivacyAsync(daveId, MemberMessagePrivacy.Nobody);
        }

        using var carol = CreateBearerClient(
            carolId,
            "API Compose Privacy Carol",
            "api-compose-privacy-carol@example.com");
        using var privacy = await carol.PostAsJsonAsync(
            MessagesApiEndpoints.Path,
            new { recipientMemberId = daveId, body = "Should fail privacy" });
        Assert.Equal(HttpStatusCode.Forbidden, privacy.StatusCode);
        var privacyProblem = await privacy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PrivateMessageService.UnableToSendMessage, privacyProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ReportMessage_CreatesSnapshot_AndRejectsOutsiders()
    {
        var (aliceId, bobId) = await SeedConversationPairAsync(
            "api-report-alice@example.com",
            "API Report Alice",
            "api-report-bob@example.com",
            "API Report Bob");
        var carolId = Guid.NewGuid();
        await SeedMemberAsync(carolId, "API Report Carol", "api-report-carol@example.com");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var sent = await service.ComposeAsync(aliceId, bobId, "Report this");
        var conversationId = sent.ConversationId!.Value;
        var message = Assert.Single(
            (await service.GetConversationAsync(conversationId, bobId, markRead: false))!.Messages);

        using var bob = CreateBearerClient(bobId, "API Report Bob", "api-report-bob@example.com");
        using var created = await bob.PostAsJsonAsync(
            MessagesApiEndpoints.ReportPath(conversationId, message.Id),
            new { reason = "Harassment" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<ReportMessageDto>(JsonOptions);
        Assert.False(dto!.AlreadyReported);
        Assert.NotEqual(Guid.Empty, dto.ReportId);

        using var conversation = await bob.GetAsync(MessagesApiEndpoints.ConversationPath(conversationId));
        var detail = await conversation.Content.ReadFromJsonAsync<ConversationDetailDto>(JsonOptions);
        Assert.True(Assert.Single(detail!.Messages).ReportedByViewer);

        using var again = await bob.PostAsJsonAsync(
            MessagesApiEndpoints.ReportPath(conversationId, message.Id),
            new { reason = "Again" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var againDto = await again.Content.ReadFromJsonAsync<ReportMessageDto>(JsonOptions);
        Assert.True(againDto!.AlreadyReported);
        Assert.Equal(dto.ReportId, againDto.ReportId);

        using var own = CreateBearerClient(aliceId, "API Report Alice", "api-report-alice@example.com");
        using var ownResponse = await own.PostAsJsonAsync(
            MessagesApiEndpoints.ReportPath(conversationId, message.Id),
            new { reason = "Mine" });
        Assert.Equal(HttpStatusCode.BadRequest, ownResponse.StatusCode);

        using var carol = CreateBearerClient(carolId, "API Report Carol", "api-report-carol@example.com");
        using var outsider = await carol.PostAsJsonAsync(
            MessagesApiEndpoints.ReportPath(conversationId, message.Id),
            new { reason = "Nope" });
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);

        using var anon = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var unauth = await anon.PostAsJsonAsync(
            MessagesApiEndpoints.ReportPath(conversationId, message.Id),
            new { reason = "Nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);
    }

    [Fact]
    public void Mapper_CopiesInboxAndConversationFields()
    {
        var conversationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var otherId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var at = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var inbox = MessagesApiMapper.ToInboxItem(
            new PrivateConversationListItem(
                conversationId,
                otherId,
                "Roger",
                "See you at Wembley",
                at,
                true,
                2));

        Assert.Equal(conversationId, inbox.ConversationId);
        Assert.Equal(otherId, inbox.OtherParticipantId);
        Assert.Equal("Roger", inbox.OtherParticipantDisplayName);
        Assert.Equal("See you at Wembley", inbox.LastMessagePreview);
        Assert.Equal(at, inbox.LastMessageAt);
        Assert.True(inbox.HasUnread);
        Assert.Equal(2, inbox.UnreadCount);
        Assert.Equal("/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", inbox.DetailPath);
        Assert.Equal(inbox.DetailPath, Assert.Single(MessagesApiMapper.ToInboxItems(
            [new PrivateConversationListItem(conversationId, otherId, "Roger", "See you at Wembley", at, true, 2)])).DetailPath);

        var messageId = Guid.NewGuid();
        var detail = MessagesApiMapper.ToConversation(
            new PrivateConversationDetail(
                conversationId,
                otherId,
                "Roger",
                [new PrivateMessageItem(messageId, otherId, "Roger", "See you at Wembley", at, false, 9)],
                TotalCount: 1,
                Page: 1,
                PageSize: 50),
            canSendReply: true);

        Assert.Equal(conversationId, detail.ConversationId);
        Assert.Equal(1, detail.TotalCount);
        Assert.Equal(1, detail.Page);
        Assert.Equal(50, detail.PageSize);
        var message = Assert.Single(detail.Messages);
        Assert.Equal(messageId, message.Id);
        Assert.Equal(otherId, message.SenderMemberId);
        Assert.Equal("Roger", message.SenderDisplayName);
        Assert.Equal("See you at Wembley", message.Body);
        Assert.False(message.IsMine);
        Assert.Equal(9, message.SortKey);
        Assert.False(message.ReportedByViewer);
        Assert.True(detail.CanSendReply);
        Assert.False(detail.HasBlockedOtherParticipant);
        Assert.Equal("/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", detail.DetailPath);

        var blocked = MessagesApiMapper.ToConversation(
            new PrivateConversationDetail(
                conversationId,
                otherId,
                "Roger",
                [new PrivateMessageItem(messageId, otherId, "Roger", "See you at Wembley", at, false, 9)],
                TotalCount: 1,
                Page: 1,
                PageSize: 50),
            canSendReply: false,
            hasBlockedOtherParticipant: true);
        Assert.False(blocked.CanSendReply);
        Assert.True(blocked.HasBlockedOtherParticipant);

        var recipientId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var recipients = MessagesApiMapper.ToRecipients(
            [new MemberRecipientMatch(recipientId, "Brian")]);
        var recipient = Assert.Single(recipients.Items);
        Assert.Equal(recipientId, recipient.MemberId);
        Assert.Equal("Brian", recipient.DisplayName);
    }

    [Fact]
    public void Mapper_UsesWebsiteConversationPath()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.Equal("/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", MessagesApiMapper.ConversationDetailPath(id));
    }

    private async Task<(Guid AliceId, Guid BobId)> SeedConversationPairAsync(
        string aliceEmail,
        string aliceName,
        string bobEmail,
        string bobName)
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        await SeedMemberAsync(aliceId, aliceName, aliceEmail);
        await SeedMemberAsync(bobId, bobName, bobEmail);
        return (aliceId, bobId);
    }

    private HttpClient CreateBearerClient(Guid memberId, string displayName, string email) =>
        CreateBearerClient(factory, memberId, displayName, email);

    private static void SetIdempotencyKey(HttpClient client, Guid key) =>
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            IdempotentApiWrites.HeaderName,
            key.ToString("D"));

    private static HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory source,
        Guid memberId,
        string displayName,
        string email)
    {
        using var scope = source.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, email, displayName);
        var client = source.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateCookieClient(Guid memberId, string displayName, string email)
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, displayName);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, email);
        return client;
    }

    private Task SeedMemberAsync(Guid memberId, string displayName, string email) =>
        SeedMemberAsync(factory, memberId, displayName, email);

    private static async Task SeedMemberAsync(
        QueenZoneWebApplicationFactory source,
        Guid memberId,
        string displayName,
        string email)
    {
        using var scope = source.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        await repository.CreateAsync(new MemberAccount
        {
            Id = memberId,
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """(?:name=["']__RequestVerificationToken["'][^>]*value=["'](?<token>[^"']+)["'])|(?:value=["'](?<token>[^"']+)["'][^>]*name=["']__RequestVerificationToken["'])""",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }
}
