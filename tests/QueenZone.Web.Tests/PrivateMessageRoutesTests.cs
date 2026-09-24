using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;
using QueenZone.Web.Pages.Members;

namespace QueenZone.Web.Tests;

public sealed class PrivateMessageRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PrivateMessageRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_Messages_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/messages");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_Inbox_ShowsEmptyState_ForSignedInMember()
    {
        var (client, _) = await CreateMemberAsync("empty-inbox@example.com", "Empty Inbox");

        var html = await client.GetStringAsync("/messages");

        Assert.Contains("You have no private messages yet", html);
        Assert.Contains("/messages/compose", html);
        Assert.Contains(">Messages<", html);
        Assert.Contains("aria-label=\"Messages\"", html);
    }

    [Fact]
    public async Task Compose_Send_Reply_AndUnreadFlow_Works()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-alice@example.com", "PM Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-bob@example.com", "PM Bob");

        var composePage = await aliceClient.GetStringAsync($"/messages/compose?to={bob.Id}");
        Assert.Contains("PM Bob", composePage);

        var sendResponse = await aliceClient.PostAsync("/messages/compose", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(composePage),
            ["Input.RecipientMemberId"] = bob.Id.ToString(),
            ["Input.Body"] = "Hello from Alice",
        }));
        Assert.Equal(HttpStatusCode.Redirect, sendResponse.StatusCode);
        var conversationPath = sendResponse.Headers.Location!.OriginalString;
        Assert.StartsWith("/messages/", conversationPath);

        var bobInbox = await bobClient.GetStringAsync("/messages");
        Assert.Contains("PM Alice", bobInbox);
        Assert.Contains("Hello from Alice", bobInbox);
        Assert.Contains("unread", bobInbox, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Messages, 1 unread conversations\"", bobInbox);
        Assert.Contains("New message", bobInbox);
        Assert.Contains("Archive", bobInbox);
        Assert.Contains("Remove", bobInbox);

        var bobConversation = await bobClient.GetStringAsync(conversationPath);
        Assert.Contains("Hello from Alice", bobConversation);

        var messages = factory.Services.GetRequiredService<IPrivateMessageRepository>();
        Assert.Equal(0, await messages.CountUnreadConversationsAsync(bob.Id));

        var replyResponse = await bobClient.PostAsync(conversationPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(bobConversation),
            ["Input.Body"] = "Hello back",
        }));
        Assert.Equal(HttpStatusCode.Redirect, replyResponse.StatusCode);

        Assert.Equal(1, await messages.CountUnreadConversationsAsync(alice.Id));

        var aliceConversation = await aliceClient.GetStringAsync(conversationPath);
        Assert.Contains("Hello from Alice", aliceConversation);
        Assert.Contains("Hello back", aliceConversation);
        Assert.Equal(0, await messages.CountUnreadConversationsAsync(alice.Id));
    }

    [Fact]
    public async Task Get_Inbox_PaginatesConversations()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-inbox-page-alice@example.com", "Inbox Alice");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();

        const int total = PrivateMessageLimits.InboxPageSize + 1;
        for (var i = 1; i <= total; i++)
        {
            var (_, peer) = await CreateMemberAsync($"pm-inbox-peer-{i}@example.com", $"Inbox Peer {i}");
            Assert.True((await service.ComposeAsync(alice.Id, peer.Id, $"Hello {i}")).Succeeded);
        }

        var page1 = await aliceClient.GetStringAsync("/messages");
        Assert.Contains("Inbox conversation pagination", page1);
        Assert.Contains("Page 1 of 2", page1);
        Assert.Contains("/messages?pageNumber=2", page1);
        Assert.Contains("Inbox Peer 51", page1);
        Assert.DoesNotContain("Inbox Peer 1</", page1);

        var page2 = await aliceClient.GetStringAsync("/messages?pageNumber=2");
        Assert.Contains("Page 2 of 2", page2);
        Assert.Contains("Inbox Peer 1</", page2);
    }

    [Fact]
    public async Task Get_Conversation_PaginatesMessages_AndDefaultsToLatestPage()
    {
        var (_, alice) = await CreateMemberAsync("pm-page-alice@example.com", "Page Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-page-bob@example.com", "Page Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();

        const int totalMessages = PrivateMessageLimits.ConversationPageSize + 1;
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Msg 1");
        var conversationId = created.ConversationId!.Value;
        for (var i = 2; i <= totalMessages; i++)
        {
            Assert.True((await service.ReplyAsync(conversationId, alice.Id, $"Msg {i}")).Succeeded);
        }

        var defaultHtml = await bobClient.GetStringAsync($"/messages/{conversationId}");
        Assert.Contains($"Msg {totalMessages}", defaultHtml);
        Assert.DoesNotContain(">Msg 1<", defaultHtml);
        Assert.Contains("Conversation message pagination", defaultHtml);
        Assert.Contains($"/messages/{conversationId}?pageNumber=1", defaultHtml);
        Assert.Contains("Page 2 of 2", defaultHtml);

        var pageOneHtml = await bobClient.GetStringAsync($"/messages/{conversationId}?pageNumber=1");
        Assert.Contains(">Msg 1<", pageOneHtml);
        Assert.DoesNotContain($">Msg {totalMessages}<", pageOneHtml);
        Assert.Contains("Page 1 of 2", pageOneHtml);
    }

    [Fact]
    public async Task Get_Conversation_ReturnsNotFound_ForNonParticipant()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-owner@example.com", "Owner");
        var (bobClient, bob) = await CreateMemberAsync("pm-peer@example.com", "Peer");
        var (carolClient, _) = await CreateMemberAsync("pm-outsider@example.com", "Outsider");

        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Top secret");

        var response = await carolClient.GetAsync($"/messages/{created.ConversationId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_FromInbox_HidesConversation_AndListsInArchivedView()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-archive-alice@example.com", "Archive Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-archive-bob@example.com", "Archive Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Archive this thread");
        var conversationId = created.ConversationId!.Value;

        var inboxHtml = await aliceClient.GetStringAsync("/messages");
        var archiveResponse = await aliceClient.PostAsync(
            "/messages?handler=Archive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(inboxHtml),
                ["conversationId"] = conversationId.ToString(),
            }));
        Assert.Equal(HttpStatusCode.Redirect, archiveResponse.StatusCode);

        var aliceInboxAfter = await aliceClient.GetStringAsync("/messages");
        Assert.Contains("You have no private messages yet", aliceInboxAfter);
        Assert.Contains("Conversation archived", aliceInboxAfter);

        var bobInbox = await bobClient.GetStringAsync("/messages");
        Assert.Contains("Archive Alice", bobInbox);

        var archivedPage = await aliceClient.GetStringAsync("/messages/archived");
        Assert.Contains("Archive Bob", archivedPage);

        var unarchiveResponse = await aliceClient.PostAsync(
            "/messages/archived?handler=Unarchive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(archivedPage),
                ["conversationId"] = conversationId.ToString(),
            }));
        Assert.Equal(HttpStatusCode.Redirect, unarchiveResponse.StatusCode);

        var aliceInboxRestored = await aliceClient.GetStringAsync("/messages");
        Assert.Contains("Archive Bob", aliceInboxRestored);
    }

    [Fact]
    public async Task Archive_ReceivingNewMessage_ReturnsConversationToInbox()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-reopen-alice@example.com", "Reopen Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-reopen-bob@example.com", "Reopen Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        var inboxHtml = await aliceClient.GetStringAsync("/messages");
        await aliceClient.PostAsync(
            "/messages?handler=Archive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(inboxHtml),
                ["conversationId"] = conversationId.ToString(),
            }));
        Assert.Contains("You have no private messages yet", await aliceClient.GetStringAsync("/messages"));

        Assert.True((await service.ReplyAsync(conversationId, bob.Id, "New reply")).Succeeded);

        var aliceInboxAfter = await aliceClient.GetStringAsync("/messages");
        Assert.Contains("Reopen Bob", aliceInboxAfter);
    }

    [Fact]
    public async Task Archive_FromConversationView_RedirectsToInbox()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-conv-archive-alice@example.com", "Conv Archive Alice");
        var (_, bob) = await CreateMemberAsync("pm-conv-archive-bob@example.com", "Conv Archive Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello there");
        var conversationId = created.ConversationId!.Value;

        var conversationHtml = await aliceClient.GetStringAsync($"/messages/{conversationId}");
        var archiveResponse = await aliceClient.PostAsync(
            $"/messages/{conversationId}?handler=Archive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(conversationHtml),
            }));
        Assert.Equal(HttpStatusCode.Redirect, archiveResponse.StatusCode);
        Assert.Equal("/messages", archiveResponse.Headers.Location!.OriginalString);

        Assert.Contains("You have no private messages yet", await aliceClient.GetStringAsync("/messages"));
    }

    [Fact]
    public async Task Archive_ReturnsNotFound_ForNonParticipant()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-archive-owner@example.com", "Archive Owner");
        var (_, bob) = await CreateMemberAsync("pm-archive-peer@example.com", "Archive Peer");
        var (carolClient, _) = await CreateMemberAsync("pm-archive-outsider@example.com", "Archive Outsider");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Top secret");

        var composeHtml = await carolClient.GetStringAsync("/messages/compose");
        var response = await carolClient.PostAsync(
            "/messages?handler=Archive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(composeHtml),
                ["conversationId"] = created.ConversationId.ToString()!,
            }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Remove_FromInbox_HidesConversation_ButNotForOtherParticipant()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-remove-alice@example.com", "Remove Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-remove-bob@example.com", "Remove Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Remove this thread");
        var conversationId = created.ConversationId!.Value;

        var inboxHtml = await aliceClient.GetStringAsync("/messages");
        var removeResponse = await aliceClient.PostAsync(
            "/messages?handler=Remove",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(inboxHtml),
                ["conversationId"] = conversationId.ToString(),
            }));
        Assert.Equal(HttpStatusCode.Redirect, removeResponse.StatusCode);

        var aliceInboxAfter = await aliceClient.GetStringAsync("/messages");
        Assert.Contains("You have no private messages yet", aliceInboxAfter);
        Assert.Contains("Conversation removed", aliceInboxAfter);

        var bobInbox = await bobClient.GetStringAsync("/messages");
        Assert.Contains("Remove Alice", bobInbox);
    }

    [Fact]
    public async Task Remove_ReceivingNewMessage_ReturnsConversationToInbox()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-remove-reopen-alice@example.com", "Reopen Alice");
        var (_, bob) = await CreateMemberAsync("pm-remove-reopen-bob@example.com", "Reopen Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello");
        var conversationId = created.ConversationId!.Value;

        var inboxHtml = await aliceClient.GetStringAsync("/messages");
        await aliceClient.PostAsync(
            "/messages?handler=Remove",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(inboxHtml),
                ["conversationId"] = conversationId.ToString(),
            }));
        Assert.Contains("You have no private messages yet", await aliceClient.GetStringAsync("/messages"));

        Assert.True((await service.ReplyAsync(conversationId, bob.Id, "New reply")).Succeeded);

        var aliceInboxAfter = await aliceClient.GetStringAsync("/messages");
        Assert.Contains("Reopen Bob", aliceInboxAfter);
    }

    [Fact]
    public async Task Remove_FromConversationView_RedirectsToInbox()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-conv-remove-alice@example.com", "Conv Remove Alice");
        var (_, bob) = await CreateMemberAsync("pm-conv-remove-bob@example.com", "Conv Remove Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello there");
        var conversationId = created.ConversationId!.Value;

        var conversationHtml = await aliceClient.GetStringAsync($"/messages/{conversationId}");
        var removeResponse = await aliceClient.PostAsync(
            $"/messages/{conversationId}?handler=Remove",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(conversationHtml),
            }));
        Assert.Equal(HttpStatusCode.Redirect, removeResponse.StatusCode);
        Assert.Equal("/messages", removeResponse.Headers.Location!.OriginalString);

        Assert.Contains("You have no private messages yet", await aliceClient.GetStringAsync("/messages"));
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_ForNonParticipant()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-remove-owner@example.com", "Remove Owner");
        var (_, bob) = await CreateMemberAsync("pm-remove-peer@example.com", "Remove Peer");
        var (carolClient, _) = await CreateMemberAsync("pm-remove-outsider@example.com", "Remove Outsider");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Top secret");

        var composeHtml = await carolClient.GetStringAsync("/messages/compose");
        var response = await carolClient.PostAsync(
            "/messages?handler=Remove",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(composeHtml),
                ["conversationId"] = created.ConversationId.ToString()!,
            }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MemberProfile_ShowsMessageAction_WhenAllowed()
    {
        var (aliceClient, alice) = await CreateMemberAsync("profile-alice@example.com", "Profile Alice");
        var (_, bob) = await CreateMemberAsync("profile-bob@example.com", "Profile Bob");

        var html = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains("Profile Bob", html);
        Assert.Contains($"/messages/compose?to={bob.Id}", html);
        Assert.Contains(">Message<", html);
        Assert.Contains(">Block<", html);

        var selfHtml = await aliceClient.GetStringAsync($"/members/{alice.Id}");
        Assert.DoesNotContain($"/messages/compose?to={alice.Id}", selfHtml);
        Assert.DoesNotContain(">Block<", selfHtml);
    }

    [Fact]
    public async Task Block_FromProfile_StopsMessaging_AndUnblockRestores()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-block-profile-alice@example.com", "Block Profile Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-block-profile-bob@example.com", "Block Profile Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        Assert.True((await service.ComposeAsync(alice.Id, bob.Id, "Hello")).Succeeded);

        var profileHtml = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        var blockResponse = await aliceClient.PostAsync(
            $"/members/{bob.Id}?handler=Block",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(profileHtml),
            }));
        Assert.Equal(HttpStatusCode.Redirect, blockResponse.StatusCode);

        var aliceProfileAfter = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains("Member blocked", aliceProfileAfter);
        Assert.Contains(">Unblock<", aliceProfileAfter);
        Assert.DoesNotContain($"/messages/compose?to={bob.Id}", aliceProfileAfter);

        var bobCompose = await service.ComposeAsync(bob.Id, alice.Id, "Should fail");
        Assert.False(bobCompose.Succeeded);
        Assert.Equal(PrivateMessageService.UnableToSendMessage, bobCompose.ErrorMessage);

        // Bob never sees an explicit "you are blocked" message on the profile.
        var bobView = await bobClient.GetStringAsync($"/members/{alice.Id}");
        Assert.DoesNotContain("you are blocked", bobView, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Member blocked", bobView, StringComparison.OrdinalIgnoreCase);

        var unblockResponse = await aliceClient.PostAsync(
            $"/members/{bob.Id}?handler=Unblock",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(aliceProfileAfter),
            }));
        Assert.Equal(HttpStatusCode.Redirect, unblockResponse.StatusCode);

        var aliceProfileRestored = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains("Member unblocked", aliceProfileRestored);
        Assert.Contains($"/messages/compose?to={bob.Id}", aliceProfileRestored);
        Assert.True((await service.ComposeAsync(bob.Id, alice.Id, "Allowed again")).Succeeded);
    }

    [Fact]
    public async Task Block_FromConversation_KeepsThreadVisible_AndBlocksReply()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-block-conv-alice@example.com", "Block Conv Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-block-conv-bob@example.com", "Block Conv Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Hello there");
        var conversationId = created.ConversationId!.Value;

        var conversationHtml = await aliceClient.GetStringAsync($"/messages/{conversationId}");
        Assert.Contains("Block Block Conv Bob", conversationHtml);

        var blockResponse = await aliceClient.PostAsync(
            $"/messages/{conversationId}?handler=Block",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(conversationHtml),
            }));
        Assert.Equal(HttpStatusCode.Redirect, blockResponse.StatusCode);

        var aliceConversationAfter = await aliceClient.GetStringAsync($"/messages/{conversationId}");
        Assert.Contains("Hello there", aliceConversationAfter);
        Assert.Contains("Member blocked", aliceConversationAfter);
        Assert.Contains("You have blocked this member", aliceConversationAfter);
        Assert.Contains("Unblock Block Conv Bob", aliceConversationAfter);
        Assert.DoesNotContain("Send reply", aliceConversationAfter);

        var bobConversation = await bobClient.GetStringAsync($"/messages/{conversationId}");
        Assert.Contains("Hello there", bobConversation);
        Assert.Contains(PrivateMessageService.UnableToSendMessage, bobConversation);
        Assert.DoesNotContain("Send reply", bobConversation);

        var replyResponse = await bobClient.PostAsync(
            $"/messages/{conversationId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(bobConversation),
                ["Input.Body"] = "Should fail",
            }));
        Assert.Equal(HttpStatusCode.OK, replyResponse.StatusCode);
        var replyHtml = await replyResponse.Content.ReadAsStringAsync();
        Assert.Contains(PrivateMessageService.UnableToSendMessage, replyHtml);
        Assert.DoesNotContain("blocked", replyHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compose_MissingBody_ShowsValidationError()
    {
        var (aliceClient, _) = await CreateMemberAsync("pm-validate-alice@example.com", "Validate Alice");
        var (_, bob) = await CreateMemberAsync("pm-validate-bob@example.com", "Validate Bob");

        var composePage = await aliceClient.GetStringAsync($"/messages/compose?to={bob.Id}");
        var response = await aliceClient.PostAsync("/messages/compose", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(composePage),
            ["Input.RecipientMemberId"] = bob.Id.ToString(),
            ["Input.Body"] = "",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Message body is required.", html);
    }

    [Fact]
    public async Task Conversation_WrapsLongMessageBodies()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-wrap-alice@example.com", "Wrap Alice");
        var (_, bob) = await CreateMemberAsync("pm-wrap-bob@example.com", "Wrap Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var longBody = "https://example.com/" + new string('a', 180);
        var created = await service.ComposeAsync(alice.Id, bob.Id, longBody);

        var html = await aliceClient.GetStringAsync($"/messages/{created.ConversationId}");
        Assert.Contains(longBody, html);
        Assert.Contains("Send reply", html);
    }

    [Fact]
    public async Task ConversationAndInbox_EncodeMarkupAndDoNotLinkifyUrls()
    {
        var (_, alice) = await CreateMemberAsync("pm-xss-alice@example.com", "Xss Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-xss-bob@example.com", "Xss Bob");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        const string markup = """<script>alert(1)</script><img onerror="alert(1)" src=x>""";
        const string withUrl = "See https://example.com and javascript:alert(1)";
        var created = await service.ComposeAsync(alice.Id, bob.Id, markup);
        Assert.True(created.Succeeded);

        var inbox = await bobClient.GetStringAsync("/messages");
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", inbox);
        Assert.DoesNotContain("<script>alert(1)</script>", inbox);

        Assert.True((await service.ReplyAsync(created.ConversationId!.Value, bob.Id, withUrl)).Succeeded);

        var conversation = await bobClient.GetStringAsync($"/messages/{created.ConversationId}");
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", conversation);
        Assert.Contains("&lt;img onerror=&quot;alert(1)&quot; src=x&gt;", conversation);
        Assert.DoesNotContain("<script>alert(1)</script>", conversation);
        Assert.DoesNotContain("""<img onerror="alert(1)" """, conversation);
        Assert.Contains("See https://example.com and javascript:alert(1)", conversation);
        Assert.DoesNotContain("href=\"https://example.com\"", conversation);
        Assert.DoesNotContain("href=\"javascript:", conversation);
    }

    [Fact]
    public async Task ComposeAndReply_OversizedBody_ShowsValidationError()
    {
        var (aliceClient, alice) = await CreateMemberAsync("pm-long-alice@example.com", "Long Alice");
        var (bobClient, bob) = await CreateMemberAsync("pm-long-bob@example.com", "Long Bob");
        var tooLong = new string('a', PrivateMessageLimits.MaxBodyLength + 1);

        var composePage = await aliceClient.GetStringAsync($"/messages/compose?to={bob.Id}");
        var composeResponse = await aliceClient.PostAsync(
            "/messages/compose",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(composePage),
                ["Input.RecipientMemberId"] = bob.Id.ToString(),
                ["Input.Body"] = tooLong,
            }));
        Assert.Equal(HttpStatusCode.OK, composeResponse.StatusCode);
        var composeHtml = await composeResponse.Content.ReadAsStringAsync();
        Assert.Contains("Message body must be 4000 characters or fewer.", composeHtml);
        Assert.Contains("You have no private messages yet", await aliceClient.GetStringAsync("/messages"));

        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Short hello");
        var conversationPath = $"/messages/{created.ConversationId}";
        var conversationPage = await bobClient.GetStringAsync(conversationPath);
        var replyResponse = await bobClient.PostAsync(
            conversationPath,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(conversationPage),
                ["Input.Body"] = tooLong,
            }));
        Assert.Equal(HttpStatusCode.OK, replyResponse.StatusCode);
        var replyHtml = await replyResponse.Content.ReadAsStringAsync();
        Assert.Contains("Message body must be 4000 characters or fewer.", replyHtml);
        Assert.DoesNotContain(tooLong, await bobClient.GetStringAsync(conversationPath));
    }

    [Fact]
    public async Task Conversation_ReportMessage_IsParticipantOnly_AndDoesNotNotify()
    {
        var (aliceClient, alice) = await CreateMemberAsync("report-alice@example.com", "Report Alice");
        var (bobClient, bob) = await CreateMemberAsync("report-bob@example.com", "Report Bob");
        var (carolClient, _) = await CreateMemberAsync("report-carol@example.com", "Report Carol");
        var service = factory.Services.GetRequiredService<PrivateMessageService>();
        var created = await service.ComposeAsync(alice.Id, bob.Id, "Please report this");
        var conversationId = created.ConversationId!.Value;
        var message = Assert.Single(
            (await service.GetConversationAsync(conversationId, bob.Id, markRead: false))!.Messages);

        var bobPage = await bobClient.GetStringAsync($"/messages/{conversationId}");
        Assert.Contains("Report message", bobPage);

        var alicePage = await aliceClient.GetStringAsync($"/messages/{conversationId}");
        Assert.DoesNotContain("Report message", alicePage);

        var unreadBefore = await service.CountUnreadConversationsAsync(alice.Id);
        var messageCountBefore = (await service.GetConversationAsync(
            conversationId,
            alice.Id,
            markRead: false))!.TotalCount;
        var reportResponse = await bobClient.PostAsync(
            $"/messages/{conversationId}?handler=Report",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(bobPage),
                ["ReportInput.MessageId"] = message.Id.ToString(),
                ["ReportInput.Reason"] = "Harassment",
            }));
        Assert.Equal(HttpStatusCode.Redirect, reportResponse.StatusCode);

        var after = await bobClient.GetStringAsync($"/messages/{conversationId}");
        Assert.Contains("Message reported", after);
        Assert.Contains(">Reported<", after);
        Assert.Equal(unreadBefore, await service.CountUnreadConversationsAsync(alice.Id));
        Assert.Equal(
            messageCountBefore,
            (await service.GetConversationAsync(conversationId, alice.Id, markRead: false))!.TotalCount);

        var carolInbox = await carolClient.GetStringAsync("/messages");
        var outsider = await carolClient.PostAsync(
            $"/messages/{conversationId}?handler=Report",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(carolInbox),
                ["ReportInput.MessageId"] = message.Id.ToString(),
                ["ReportInput.Reason"] = "Nope",
            }));
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
    }

    [Fact]
    public async Task MemberProfile_Unauthenticated_PromptsSignInToMessage()
    {
        var (_, bob) = await CreateMemberAsync("profile-public@example.com", "Public Bob");
        var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var html = await anon.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains("Sign in to message", html);
        Assert.Contains("Sign in to follow", html);
        Assert.Contains("/account/login", html);
    }

    [Fact]
    public async Task MemberProfile_FollowAndUnfollow_UpdatesStatus()
    {
        var (aliceClient, _) = await CreateMemberAsync("follow-alice@example.com", "Follow Alice");
        var (_, bob) = await CreateMemberAsync("follow-bob@example.com", "Follow Bob");

        var profile = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains(">Follow</", profile);
        Assert.Contains($"/messages/compose?to={bob.Id}", profile);

        var followResponse = await aliceClient.PostAsync(
            $"/members/{bob.Id}?handler=Follow",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(profile),
            }));
        Assert.Equal(HttpStatusCode.Redirect, followResponse.StatusCode);

        var afterFollow = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains("You are now following this member.", afterFollow);
        Assert.Contains(">Unfollow</", afterFollow);

        var unfollowResponse = await aliceClient.PostAsync(
            $"/members/{bob.Id}?handler=Unfollow",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(afterFollow),
            }));
        Assert.Equal(HttpStatusCode.Redirect, unfollowResponse.StatusCode);

        var afterUnfollow = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains("You unfollowed this member.", afterUnfollow);
        Assert.Contains(">Follow</", afterUnfollow);
    }

    [Fact]
    public async Task MemberProfile_HidesMessage_WhenRecipientAcceptsFollowedOnly()
    {
        var (aliceClient, alice) = await CreateMemberAsync("privacy-alice@example.com", "Privacy Alice");
        var (_, bob) = await CreateMemberAsync("privacy-bob@example.com", "Privacy Bob");
        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        await members.UpdateMessagePrivacyAsync(bob.Id, MemberMessagePrivacy.Followed);

        var html = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.DoesNotContain($"/messages/compose?to={bob.Id}", html);
        Assert.Contains(">Follow</", html);

        var compose = await aliceClient.GetStringAsync($"/messages/compose?to={bob.Id}");
        Assert.Contains(PrivateMessageService.UnableToSendMessage, compose);

        var follows = factory.Services.GetRequiredService<IMemberFollowRepository>();
        await follows.FollowAsync(bob.Id, alice.Id, DateTimeOffset.UtcNow);
        var afterFollow = await aliceClient.GetStringAsync($"/members/{bob.Id}");
        Assert.Contains($"/messages/compose?to={bob.Id}", afterFollow);
    }

    [Fact]
    public async Task MemberProfile_ListsAndPaginatesPublicActivity()
    {
        var memberId = Guid.NewGuid();
        var activity = Enumerable.Range(1, ProfileModel.ActivityPageSize + 1)
            .Select(index => new MemberPublicActivityItem(
                MemberPublicActivityType.ForumPost,
                $"Topic {index}",
                $"Post summary {index}",
                DateTimeOffset.UtcNow.AddMinutes(-index),
                ContentId: index,
                ParentId: 1000 + index,
                Slug: $"topic-{index}"))
            .ToList();
        using var profileFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMemberPublicActivityRepository>();
            services.AddSingleton<IMemberPublicActivityRepository>(new StubMemberPublicActivityRepository(activity));
        }));
        var members = profileFactory.Services.GetRequiredService<IMemberAccountRepository>();
        await members.CreateAsync(new MemberAccount
        {
            Id = memberId,
            Email = "activity@example.com",
            DisplayName = "Active Member",
            CreatedAt = DateTime.UtcNow,
        });
        var client = profileFactory.CreateClient();

        var firstPage = await client.GetStringAsync($"/members/{memberId}");
        Assert.Contains("Public contributions", firstPage);
        Assert.Contains("Topic 1", firstPage);
        Assert.DoesNotContain("Topic 21", firstPage);
        Assert.Contains("Member activity pagination", firstPage);

        var secondPage = await client.GetStringAsync($"/members/{memberId}?pageNumber=2");
        Assert.Contains("Topic 21", secondPage);
        Assert.DoesNotContain("Topic 1</a>", secondPage);
    }

    private async Task<(HttpClient Client, MemberAccount Member)> CreateMemberAsync(
        string email,
        string displayName)
    {
        var memberId = Guid.NewGuid();
        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var member = await members.CreateAsync(new MemberAccount
        {
            Id = memberId,
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, member.Email);
        return (client, member);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """(?:name=["']__RequestVerificationToken["'][^>]*value=["'](?<token>[^"']+)["'])|(?:value=["'](?<token>[^"']+)["'][^>]*name=["']__RequestVerificationToken["'])""",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Antiforgery token was not found in the form. Snip: " + SnipTokenArea(html));
        return match.Groups["token"].Value;
    }

    private static string SnipTokenArea(string html)
    {
        var idx = html.IndexOf("RequestVerification", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            idx = html.IndexOf("<form", StringComparison.OrdinalIgnoreCase);
        }

        if (idx < 0)
        {
            return html.Length <= 400 ? html : html[..400];
        }

        var start = Math.Max(0, idx - 80);
        return html.Substring(start, Math.Min(500, html.Length - start));
    }

    private sealed class StubMemberPublicActivityRepository(IReadOnlyList<MemberPublicActivityItem> items)
        : IMemberPublicActivityRepository
    {
        public Task<MemberPublicActivityPage> GetPageAsync(
            Guid memberId,
            int? linkedLegacyUserId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new MemberPublicActivityPage(pageItems, items.Count, page, pageSize));
        }

        public Task<MemberPublicActivityPage> GetFeedPageAsync(
            IReadOnlyCollection<Guid> memberIds,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Following feed should not N+1 through GetPageAsync.");
    }
}
