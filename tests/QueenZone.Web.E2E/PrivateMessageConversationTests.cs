using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

/// <summary>
/// Conversation page send, reply and report outcome handling (#2436). Uses the Testing host's
/// <see cref="BrowserFixtureMembers"/> (MemberBrowserFixture:Enabled) so both participants exist.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class PrivateMessageConversationTests : E2EPageTest
{
    private static readonly BrowserFixtureMember Sender = BrowserFixtureMembers.ConversationSender;
    private static readonly BrowserFixtureMember Recipient = BrowserFixtureMembers.ConversationRecipient;

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = HeadersFor(Sender),
        };

    [Test]
    public async Task ReportingTheOtherParticipantsReplyShowsConfirmation()
    {
        var (conversationUrl, reply) = await StartConversationWithReplyAsync();

        var replyMessage = Page.Locator("article").Filter(new() { HasText = reply });
        await replyMessage.GetByRole(AriaRole.Button, new() { Name = "Report message" }).ClickAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Report message" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($".*{Regex.Escape(new Uri(conversationUrl).AbsolutePath)}.*"));
        await Expect(Page.GetByText("Message reported. Moderators will review it.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.Locator("article").Filter(new() { HasText = reply }).GetByText("Reported")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ReportingAMissingMessageReturnsNotFound()
    {
        var (conversationUrl, _) = await StartConversationWithReplyAsync();
        var conversationPath = new Uri(conversationUrl).AbsolutePath;

        // Point a real report form (with its antiforgery token) at a message that does not exist.
        var response = await Page.RunAndWaitForNavigationAsync(
            () => Page.EvaluateAsync("""
                id => {
                    const form = document.querySelector('form.qz-message-report');
                    form.querySelector("input[name='ReportInput.MessageId']").value = id;
                    form.requestSubmit();
                }
                """, Guid.NewGuid().ToString()),
            new() { UrlFunc = url => url.Contains("handler=Report", StringComparison.Ordinal) });

        Assert.That(response?.Status, Is.EqualTo(404));

        // Return to the conversation so passed-proof captures the thread, not the 404 page.
        await Page.GotoAsync(conversationPath);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send reply" })).ToBeVisibleAsync();
    }

    private async Task<(string ConversationUrl, string Reply)> StartConversationWithReplyAsync()
    {
        var marker = Guid.NewGuid().ToString("N");
        var opening = $"E2E conversation opening {marker}";
        var reply = $"E2E conversation reply {marker}";

        await Page.GotoAsync($"/messages/compose?to={Recipient.Id}");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Message", Exact = true }).FillAsync(opening);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/messages/[0-9a-fA-F-]{36}$"));
        var conversationUrl = Page.Url;
        await Expect(Page.GetByText(opening)).ToBeVisibleAsync();

        var recipientContext = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = HeadersFor(Recipient),
        });
        var recipientPage = await recipientContext.NewPageAsync();
        await recipientPage.GotoAsync(conversationUrl);
        await recipientPage.GetByRole(AriaRole.Textbox, new() { Name = "Reply", Exact = true }).FillAsync(reply);
        await recipientPage.GetByRole(AriaRole.Button, new() { Name = "Send reply" }).ClickAsync();
        await Expect(recipientPage.GetByText(reply)).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByText(reply)).ToBeVisibleAsync();
        return (conversationUrl, reply);
    }

    private static Dictionary<string, string> HeadersFor(BrowserFixtureMember member) => new()
    {
        ["X-Test-Member-Id"] = member.Id.ToString(),
        ["X-Test-Member-Name"] = member.DisplayName,
        ["X-Test-Member-Email"] = member.Email,
    };
}
