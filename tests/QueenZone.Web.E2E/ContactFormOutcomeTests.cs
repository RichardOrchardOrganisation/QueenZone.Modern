using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Contact form outcome handling (#2436): a service-level rejection keeps the visitor on
/// /contact with the service's message, and a valid submission reaches the confirmation page.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class ContactFormOutcomeTests : E2EPageTest
{
    [Test]
    public async Task UnknownTopicIsRejectedByTheServiceAndStaysOnContactPage()
    {
        await Page.GotoAsync("/contact");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send message" })).ToBeVisibleAsync();

        // Passes model validation (non-empty) but is not a known topic, so the service decides.
        await Page.EvaluateAsync("""
            () => {
                const select = document.querySelector("select[name='Topic']");
                select.add(new Option('Unknown topic', 'not-a-real-topic'));
                select.value = 'not-a-real-topic';
            }
            """);
        await FillValidFieldsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/contact$"));
        await Expect(Page.GetByText("Please choose a topic.").First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send message" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task ValidSubmissionReachesConfirmation()
    {
        await Page.GotoAsync("/contact");
        await Page.GetByLabel("Topic").SelectOptionAsync(new SelectOptionValue { Label = "Other" });
        await FillValidFieldsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/contact/confirmation$"));

        // Leave the page on the feature route so passed-proof captures the contact form.
        await Page.GotoAsync("/contact");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send message" })).ToBeVisibleAsync();
    }

    private async Task FillValidFieldsAsync()
    {
        var marker = Guid.NewGuid().ToString("N");
        await Page.GetByLabel("Subject").FillAsync($"E2E contact {marker[..8]}");
        await Page.GetByLabel("Your name").FillAsync("E2E Contact Visitor");
        await Page.GetByLabel("Email address").FillAsync($"{marker}@e2e.queenzone.local");
        await Page.GetByLabel("Your message").FillAsync($"E2E contact form outcome check {marker}.");
    }
}
