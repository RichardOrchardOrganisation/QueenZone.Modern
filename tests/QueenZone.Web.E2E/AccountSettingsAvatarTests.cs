using Microsoft.Playwright;
using QueenZone.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.E2E;

/// <summary>
/// Account settings avatar upload and removal through <c>MemberAvatarService</c> (#2437). Uses the
/// Testing host's <see cref="BrowserFixtureMembers.SettingsAvatarOwner"/> (MemberBrowserFixture:Enabled).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AccountSettingsAvatarTests : E2EPageTest
{
    private static readonly BrowserFixtureMember Owner = BrowserFixtureMembers.SettingsAvatarOwner;

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-Test-Member-Id"] = Owner.Id.ToString(),
                ["X-Test-Member-Name"] = Owner.DisplayName,
                ["X-Test-Member-Email"] = Owner.Email,
            },
        };

    [Test]
    public async Task UploadingThenRemovingAnAvatarShowsEachConfirmation()
    {
        await Page.GotoAsync("/account/settings");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Profile avatar" })).ToBeVisibleAsync();

        await Page.GetByLabel("Upload image").SetInputFilesAsync(new FilePayload
        {
            Name = "e2e-avatar.png",
            MimeType = "image/png",
            Buffer = CreatePng(),
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload avatar" }).ClickAsync();

        await Expect(Page.GetByText("Avatar updated.")).ToBeVisibleAsync();
        var removeButton = Page.GetByRole(AriaRole.Button, new() { Name = "Remove avatar" });
        await Expect(removeButton).ToBeVisibleAsync();

        await removeButton.ClickAsync();

        await Expect(Page.GetByText("Avatar removed.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Remove avatar" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UploadingANonImageShowsTheValidationMessage()
    {
        await Page.GotoAsync("/account/settings");

        await Page.GetByLabel("Upload image").SetInputFilesAsync(new FilePayload
        {
            Name = "notes.png",
            MimeType = "image/png",
            Buffer = "not-an-image"u8.ToArray(),
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload avatar" }).ClickAsync();

        await Expect(Page.GetByText("Avatar must be a JPEG, PNG, or WebP image.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProfileEditsPersistAfterReload()
    {
        await Page.GotoAsync("/account/settings");
        await Page.GetByLabel("Display name").FillAsync("Profile Edit Fan");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save display name" }).ClickAsync();
        await Expect(Page.GetByText("Display name updated.")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Radio, new() { Name = "Dark", Exact = true }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save appearance", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Appearance updated.")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Radio, new() { Name = "Nobody", Exact = true }).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save messaging privacy" }).ClickAsync();
        await Expect(Page.GetByText("Messaging privacy updated.")).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByLabel("Display name")).ToHaveValueAsync("Profile Edit Fan");
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Dark", Exact = true })).ToBeCheckedAsync();
        await Expect(Page.GetByRole(AriaRole.Radio, new() { Name = "Nobody", Exact = true })).ToBeCheckedAsync();
    }

    private static byte[] CreatePng()
    {
        using var image = new Image<Rgba32>(96, 96, new Rgba32(120, 40, 160, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
