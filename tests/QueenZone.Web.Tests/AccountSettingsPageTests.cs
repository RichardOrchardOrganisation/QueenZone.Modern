using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed partial class AccountSettingsPageTests : IClassFixture<InspectableBlobWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly InspectableBlobWebApplicationFactory inspectableFactory;

    public AccountSettingsPageTests(InspectableBlobWebApplicationFactory factory)
    {
        inspectableFactory = factory;
        this.factory = factory;
    }

    [Fact]
    public async Task Get_RedirectsUnauthenticatedUsersToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/account/settings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_ShowsPrefilledForm_ForAuthenticatedMember()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-get@example.com",
            displayName: "Settings Fan",
            subject: "google-settings-get");

        var body = await client.GetStringAsync("/account/settings");

        Assert.Contains("Account settings", body);
        Assert.Contains("settings-get@example.com", body);
        Assert.Contains("value=\"Settings Fan\"", body);
        Assert.Contains("Google", body);
        Assert.Contains("name=\"DisplayName\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Save display name", body);
        Assert.Contains("Legacy forum account", body);
        Assert.Contains("No legacy forum account is linked to this email.", body);
        Assert.Contains("href=\"/account/delete\"", body);
        Assert.Contains("Delete my account", body);
        Assert.Contains("Who can message me", body);
        Assert.Contains("Signed-in members", body);
        Assert.Contains("People I follow", body);
        Assert.Contains("Nobody", body);
        Assert.Contains("Save messaging privacy", body);
        Assert.Contains("href=\"/following\"", body);
        Assert.Contains(">Following</a>", body);
        Assert.Contains("Social profiles", body);
        Assert.Contains("Save social profiles", body);
        Assert.Contains("name=\"SocialX\"", body);
        Assert.Contains("name=\"SocialBluesky\"", body);
    }

    [Fact]
    public async Task PostUpdateSocialLinks_SavesHandleAndUrl_AndReopensPrefills()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-socials@example.com",
            displayName: "Social Fan",
            subject: "google-settings-socials",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var response = await client.PostAsync(
            "/account/settings?handler=UpdateSocialLinks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["SocialX"] = "@queen",
                ["SocialYouTube"] = "https://www.youtube.com/@QueenOfficial",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var updated = await client.GetStringAsync("/account/settings");
        Assert.Contains("Social profiles updated.", updated);
        Assert.Contains("value=\"https://x.com/queen\"", updated);
        Assert.Contains("value=\"https://www.youtube.com/@QueenOfficial\"", updated);

        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("settings-socials@example.com");
        var links = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .ListSocialLinksAsync(member!.Id);
        Assert.Equal(
            [
                new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
                new MemberSocialLink(MemberSocialChannel.YouTube, "https://www.youtube.com/@QueenOfficial"),
            ],
            links);

        var profile = await client.GetStringAsync($"/members/{member.Id}");
        Assert.Contains("https://x.com/queen", profile);
        Assert.Contains("https://www.youtube.com/@QueenOfficial", profile);
        Assert.DoesNotContain("instagram.com", profile);
    }

    [Fact]
    public async Task PostUpdateSocialLinks_FieldError_DoesNotWipeOtherChannels()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-socials-error@example.com",
            displayName: "Social Error",
            subject: "google-settings-socials-error",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var token = ExtractAntiforgeryToken(formPage);
        var saved = await client.PostAsync(
            "/account/settings?handler=UpdateSocialLinks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SocialX"] = "@queen",
                ["SocialInstagram"] = "queenofficial",
            }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var retryPage = await client.GetStringAsync("/account/settings");
        var rejected = await client.PostAsync(
            "/account/settings?handler=UpdateSocialLinks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(retryPage),
                ["SocialX"] = "@queen",
                ["SocialInstagram"] = "queenofficial",
                ["SocialYouTube"] = "javascript:alert(1)",
            }));

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains(MemberSocialLinkUrl.InvalidValueMessage, body);
        Assert.Contains("javascript:alert(1)", body);
        Assert.DoesNotContain(">Display name is required.</span>", body);

        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("settings-socials-error@example.com");
        var links = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .ListSocialLinksAsync(member!.Id);
        Assert.Equal(
            [
                new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
                new MemberSocialLink(MemberSocialChannel.Instagram, "https://www.instagram.com/queenofficial"),
            ],
            links);
        Assert.DoesNotContain(links, link => link.Channel == MemberSocialChannel.YouTube);
    }

    [Fact]
    public async Task PostUpdateSocialLinks_ClearRemovesRowFromProfile()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-socials-clear@example.com",
            displayName: "Social Clear",
            subject: "google-settings-socials-clear",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        await client.PostAsync(
            "/account/settings?handler=UpdateSocialLinks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["SocialX"] = "@queen",
            }));

        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("settings-socials-clear@example.com");
        var before = await client.GetStringAsync($"/members/{member!.Id}");
        Assert.Contains("https://x.com/queen", before);

        var retryPage = await client.GetStringAsync("/account/settings");
        await client.PostAsync(
            "/account/settings?handler=UpdateSocialLinks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(retryPage),
                ["SocialX"] = string.Empty,
            }));

        var after = await client.GetStringAsync($"/members/{member.Id}");
        Assert.DoesNotContain("https://x.com/queen", after);
        Assert.DoesNotContain("qz-member-profile__socials", after);
        var links = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .ListSocialLinksAsync(member.Id);
        Assert.Empty(links);
    }

    [Fact]
    public async Task PostUpdateMessagePrivacy_SavesFollowedSetting()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-privacy@example.com",
            displayName: "Privacy Fan",
            subject: "google-settings-privacy",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var response = await client.PostAsync(
            "/account/settings?handler=UpdateMessagePrivacy",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["MessagePrivacy"] = nameof(MemberMessagePrivacy.Followed),
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var updated = await client.GetStringAsync("/account/settings");
        Assert.Contains("Messaging privacy updated.", updated);
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("settings-privacy@example.com");
        Assert.Equal(MemberMessagePrivacy.Followed, member!.MessagePrivacy);
    }

    [Fact]
    public async Task Get_ShowsAppearanceOptions_AndNoThemeOverrideByDefault()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-theme-default@example.com",
            displayName: "Theme Default",
            subject: "google-settings-theme-default");

        var body = await client.GetStringAsync("/account/settings");

        Assert.Contains("Appearance", body);
        Assert.Contains("Use device setting", body);
        Assert.Contains("Save appearance", body);
        Assert.DoesNotContain("data-theme=", body);
    }

    [Fact]
    public async Task PostUpdateThemePreference_SavesToProfileAndAppliesToEveryPage()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-theme@example.com",
            displayName: "Theme Fan",
            subject: "google-settings-theme",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var response = await client.PostAsync(
            "/account/settings?handler=UpdateThemePreference",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["ThemePreference"] = nameof(MemberThemePreference.Dark),
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var updated = await client.GetStringAsync("/account/settings");
        Assert.Contains("Appearance updated.", updated);
        Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", updated);
        var home = await client.GetStringAsync("/");
        Assert.Contains("data-theme=\"dark\"", home);
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("settings-theme@example.com");
        Assert.Equal(MemberThemePreference.Dark, member!.ThemePreference);
    }

    [Theory]
    [InlineData(MemberThemePreference.Light, "light", "#FFFFFF")]
    [InlineData(MemberThemePreference.Dark, "dark", "#111111")]
    public async Task Get_applies_external_account_change_on_first_response(
        MemberThemePreference preference, string attribute, string chrome)
    {
        var identity = Guid.NewGuid().ToString("N");
        using var client = await CreateSignedInMemberClientAsync(
            email: identity + "@example.com", displayName: "Theme Sync", subject: identity);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByEmailAsync(identity + "@example.com");
        await repository.UpdateThemePreferenceAsync(account!.Id, preference);
        var first = await client.GetStringAsync("/account/settings");
        Assert.Contains($"data-theme=\"{attribute}\"", first);
        Assert.Contains($"name=\"theme-color\" content=\"{chrome}\"", first);
        Assert.Contains($"data-theme=\"{attribute}\"", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task PostUpdateDeviceTheme_OverridesAccountPreferenceOnThisBrowserOnly()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-device-theme@example.com",
            displayName: "Device Theme",
            subject: "google-settings-device-theme",
            options: new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        await client.PostAsync(
            "/account/settings?handler=UpdateThemePreference",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["ThemePreference"] = nameof(MemberThemePreference.Dark),
            }));

        formPage = await client.GetStringAsync("/account/settings");
        Assert.Contains("data-theme=\"dark\"", formPage);
        var deviceResponse = await client.PostAsync(
            "/account/settings?handler=UpdateDeviceTheme",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["DeviceTheme"] = nameof(DeviceThemeChoice.Light),
            }));

        Assert.Equal(HttpStatusCode.Redirect, deviceResponse.StatusCode);
        Assert.Contains(
            deviceResponse.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith("qz_theme=light", StringComparison.Ordinal));
        var home = await client.GetStringAsync("/");
        Assert.Contains("data-theme=\"light\"", home);
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("settings-device-theme@example.com");
        Assert.Equal(MemberThemePreference.Dark, member!.ThemePreference);
        var prepared = await client.GetFromJsonAsync<Dictionary<string, string>>("/appearance?handler=Token");
        using var systemRequest = new HttpRequestMessage(HttpMethod.Post, "/appearance")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["DeviceTheme"] = "System" }),
        };
        systemRequest.Headers.Add("RequestVerificationToken", prepared!["token"]);
        Assert.Equal(HttpStatusCode.Redirect, (await client.SendAsync(systemRequest)).StatusCode);
        Assert.Contains("data-theme=\"system\"", await client.GetStringAsync("/"));
        Assert.Equal(MemberThemePreference.Dark, member.ThemePreference);

        formPage = await client.GetStringAsync("/account/settings");
        var cleared = await client.PostAsync(
            "/account/settings?handler=UpdateDeviceTheme",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["DeviceTheme"] = nameof(DeviceThemeChoice.Account),
            }));

        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);
        Assert.Contains("data-theme=\"dark\"", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task DeviceThemeCookie_AppliesToSignedOutVisitorsAndIgnoresUnknownValues()
    {
        var client = factory.CreateClient();

        using var forced = new HttpRequestMessage(HttpMethod.Get, "/");
        forced.Headers.Add("Cookie", "qz_theme=dark");
        var forcedBody = await (await client.SendAsync(forced)).Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", forcedBody);

        using var bogus = new HttpRequestMessage(HttpMethod.Get, "/");
        bogus.Headers.Add("Cookie", "qz_theme=\"><script>");
        var bogusBody = await (await client.SendAsync(bogus)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-theme=", bogusBody);
    }

    [Fact]
    public async Task DeleteAccount_RequiresExactConfirmation()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "delete-invalid@example.com",
            displayName: "Delete Invalid",
            subject: "google-delete-invalid",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var page = await client.GetStringAsync("/account/delete");

        var response = await client.PostAsync(
            "/account/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page),
                ["Confirmation"] = "delete",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Type DELETE to confirm", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var member = await scope.ServiceProvider
            .GetRequiredService<IMemberAccountRepository>()
            .FindByEmailAsync("delete-invalid@example.com");
        Assert.Null(member!.DeletionRequestedAt);
    }

    [Fact]
    public async Task DeleteAccount_AnonymisesAndSignsOut_ButLoginAndCancellationRestoreIdentity()
    {
        const string email = "delete-valid@example.com";
        var client = await CreateSignedInMemberClientAsync(
            email,
            displayName: "Delete Valid",
            subject: "google-delete-valid",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var page = await client.GetStringAsync("/account/delete");
        Assert.Contains("Delete my account now", page);
        Assert.Contains("Deletion cannot be undone", page);

        var response = await client.PostAsync(
            "/account/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page),
                ["Confirmation"] = "DELETE",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/deletion-requested", response.Headers.Location!.OriginalString);
        var settingsAfterDeletion = await client.GetAsync("/account/settings");
        Assert.Equal(HttpStatusCode.Redirect, settingsAfterDeletion.StatusCode);
        Assert.Contains("/account/login", settingsAfterDeletion.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var members = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var deleted = await members.FindByEmailAsync(email);
        Assert.NotNull(deleted);
        Assert.False(deleted.IsSuspended);
        Assert.NotNull(deleted.DeletionRequestedAt);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, deleted.DisplayName);
        Assert.Equal("Delete Valid", deleted.DeletionRecoveryDisplayName);
        Assert.Equal(["Google"], await members.ListExternalProvidersAsync(deleted.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/members/{deleted.Id}")).StatusCode);

        var signedInAgain = await CreateSignedInMemberClientAsync(
            email,
            displayName: "Delete Valid",
            subject: "google-delete-valid",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var pendingPage = await signedInAgain.GetStringAsync("/account/delete");
        Assert.Contains("Deletion scheduled", pendingPage);
        Assert.Contains("Cancel account deletion", pendingPage);

        var cancelResponse = await signedInAgain.PostAsync(
            "/account/delete?handler=Cancel",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(pendingPage),
            }));

        Assert.Equal(HttpStatusCode.Redirect, cancelResponse.StatusCode);
        Assert.Equal("/account/settings", cancelResponse.Headers.Location!.OriginalString);
        var restored = await members.FindByEmailAsync(email);
        Assert.Null(restored!.DeletionRequestedAt);
        Assert.False(restored.IsSuspended);
        Assert.Equal("Delete Valid", restored.DisplayName);
        Assert.Null(restored.DeletionRecoveryDisplayName);
        Assert.Equal(HttpStatusCode.OK, (await signedInAgain.GetAsync($"/members/{restored.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_Immediate_ProvidesStatusReceiptAfterSignOut()
    {
        var client = await CreateSignedInMemberClientAsync(
            "delete-immediate-page@example.com",
            displayName: "Immediate Page",
            subject: "google-delete-immediate-page",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });
        var page = await client.GetStringAsync("/account/delete");

        using var response = await client.PostAsync(
            "/account/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page),
                ["Confirmation"] = "DELETE",
                ["Immediate"] = "true",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/deletion-status?receipt=", response.Headers.Location!.OriginalString);
        using var status = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("Account deletion complete", await status.Content.ReadAsStringAsync());
        Assert.Equal("no-store", status.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Get_ShowsLegacyClaimOffer_WhenEmailMatchesLegacyAccount()
    {
        var client = await CreateSignedInMemberClientWithLegacyMatchAsync(
            email: "claim-offer@example.com",
            displayName: "Modern Fan",
            subject: "google-legacy-claim-offer",
            legacyUserId: 9001,
            legacyUsername: "ArchiveOffer");

        var body = await client.GetStringAsync("/account/settings");

        Assert.Contains("ArchiveOffer", body);
        Assert.Contains("Claim legacy account", body);
        Assert.Contains("Use legacy username", body);
        Assert.DoesNotContain("Linked to legacy forum account", body);
    }

    [Fact]
    public async Task PostClaimLegacy_LinksAccount_AndShowsLinkedStatus()
    {
        var client = await CreateSignedInMemberClientWithLegacyMatchAsync(
            email: "claim-post@example.com",
            displayName: "Modern Fan",
            subject: "google-legacy-claim-post",
            legacyUserId: 9002,
            legacyUsername: "ArchiveClaimed",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        Assert.Contains("Claim legacy account", formPage);

        var response = await client.PostAsync(
            "/account/settings?handler=ClaimLegacy",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["SelectedLegacyUserId"] = "9002",
                ["AdoptLegacyDisplayName"] = "true",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/settings", response.Headers.Location!.OriginalString);

        var updated = await client.GetStringAsync("/account/settings");
        Assert.Contains("Legacy account claimed", updated);
        Assert.Contains("Linked to legacy forum account", updated);
        Assert.Contains("ArchiveClaimed", updated);
        Assert.Contains("Unlink legacy account", updated);
        Assert.DoesNotContain("Claim legacy account", updated);
        Assert.Contains("value=\"ArchiveClaimed\"", updated);
    }

    [Fact]
    public async Task PostUnlinkLegacy_ClearsLink_AndShowsClaimOfferAgain()
    {
        var client = await CreateSignedInMemberClientWithLegacyMatchAsync(
            email: "unlink-post@example.com",
            displayName: "Modern Fan",
            subject: "google-legacy-unlink-post",
            legacyUserId: 9003,
            legacyUsername: "ArchiveUnlink",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var claimPage = await client.GetStringAsync("/account/settings");
        var claimResponse = await client.PostAsync(
            "/account/settings?handler=ClaimLegacy",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(claimPage),
                ["SelectedLegacyUserId"] = "9003",
                ["AdoptLegacyDisplayName"] = "false",
            }));
        Assert.Equal(HttpStatusCode.Redirect, claimResponse.StatusCode);

        var linkedPage = await client.GetStringAsync("/account/settings");
        Assert.Contains("Unlink legacy account", linkedPage);
        Assert.Contains("ArchiveUnlink", linkedPage);

        var unlinkResponse = await client.PostAsync(
            "/account/settings?handler=UnlinkLegacy",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(linkedPage),
            }));

        Assert.Equal(HttpStatusCode.Redirect, unlinkResponse.StatusCode);
        Assert.Equal("/account/settings", unlinkResponse.Headers.Location!.OriginalString);

        var unlinked = await client.GetStringAsync("/account/settings");
        Assert.Contains("Legacy account unlinked", unlinked);
        Assert.Contains("Claim legacy account", unlinked);
        Assert.Contains("ArchiveUnlink", unlinked);
        Assert.DoesNotContain("Linked to legacy forum account", unlinked);
        Assert.DoesNotContain("Unlink legacy account", unlinked);
    }

    [Fact]
    public async Task Get_ShowsLegacyChoiceList_WhenEmailMatchesMultipleAccounts()
    {
        var client = await CreateSignedInMemberClientWithLegacyMatchesAsync(
            email: "multi-claim@example.com",
            displayName: "Modern Fan",
            subject: "google-legacy-multi-claim-get",
            matches:
            [
                new LegacyMemberMatch(9101, "FirstArchive"),
                new LegacyMemberMatch(9102, "SecondArchive"),
            ]);

        var body = await client.GetStringAsync("/account/settings");

        Assert.Contains("multiple classic QueenZone forum accounts", body);
        Assert.Contains("FirstArchive", body);
        Assert.Contains("SecondArchive", body);
        Assert.Contains("name=\"SelectedLegacyUserId\"", body);
        Assert.Contains("value=\"9101\"", body);
        Assert.Contains("value=\"9102\"", body);
        Assert.Contains("Claim legacy account", body);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task Get_explains_occupied_legacy_matches_without_offering_them_for_claim(
        int occupiedCount, bool hasFreeMatch)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var matches = Enumerable.Range(0, occupiedCount + (hasFreeMatch ? 1 : 0))
            .Select(index => new LegacyMemberMatch(9600 + occupiedCount * 10 + (hasFreeMatch ? 100 : 0) + index,
                "Archive" + index)).ToArray();
        for (var index = 0; index < occupiedCount; index++)
        {
            using var owner = await CreateSignedInMemberClientWithLegacyMatchesAsync(
                "owner-" + suffix + index + "@example.com", "Archive owner", "owner-" + suffix + index,
                [matches[index]]);
            var form = await owner.GetStringAsync("/account/settings");
            var claimed = await owner.PostAsync("/account/settings?handler=ClaimLegacy",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = ExtractAntiforgeryToken(form),
                    ["SelectedLegacyUserId"] = matches[index].UserId.ToString(),
                }));
            Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            Assert.Contains("Linked to legacy forum account", await claimed.Content.ReadAsStringAsync());
        }
        using var member = await CreateSignedInMemberClientWithLegacyMatchesAsync(
            "member-" + suffix + "@example.com", "Modern fan", "member-" + suffix, matches);
        var body = await member.GetStringAsync("/account/settings");
        Assert.Contains("already linked to", body);
        Assert.Contains("Archive0", body);
        if (hasFreeMatch)
        {
            Assert.Contains("Claim legacy account", body);
            Assert.Contains("name=\"SelectedLegacyUserId\" value=\"" + matches[^1].UserId + "\"", body);
        }
        else
        {
            Assert.DoesNotContain("Claim legacy account</button>", body);
        }
    }

    [Fact]
    public async Task PostClaimLegacy_ClaimsSelectedAccount_WhenMultipleMatches()
    {
        var client = await CreateSignedInMemberClientWithLegacyMatchesAsync(
            email: "multi-claim-post@example.com",
            displayName: "Modern Fan",
            subject: "google-legacy-multi-claim-post",
            matches:
            [
                new LegacyMemberMatch(9201, "KeepMe"),
                new LegacyMemberMatch(9202, "PickMe"),
            ],
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        Assert.Contains("PickMe", formPage);

        var response = await client.PostAsync(
            "/account/settings?handler=ClaimLegacy",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["SelectedLegacyUserId"] = "9202",
                ["AdoptLegacyDisplayName"] = "true",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var updated = await client.GetStringAsync("/account/settings");
        Assert.Contains("Legacy account claimed", updated);
        Assert.Contains("Linked to legacy forum account", updated);
        Assert.Contains("PickMe", updated);
        Assert.Contains("(id 9202)", updated);
        Assert.DoesNotContain("Claim legacy account", updated);
        Assert.Contains("value=\"PickMe\"", updated);
    }

    [Fact]
    public async Task Post_ValidName_SucceedsAndReissuesCookieWithUpdatedClaim()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-post@example.com",
            displayName: "Before Name",
            subject: "google-settings-post",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        Assert.Contains("value=\"Before Name\"", formPage);

        var response = await client.PostAsync(
            "/account/settings",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["DisplayName"] = "  After Name  ",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/settings", response.Headers.Location!.OriginalString);

        // Follow redirect; header should show the new display name from the re-issued cookie.
        var updatedPage = await client.GetStringAsync("/account/settings");
        Assert.Contains("Display name updated.", updatedPage);
        Assert.Contains("After Name", updatedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Before Name", updatedPage, StringComparison.Ordinal);
        Assert.Contains("name=\"DisplayName\"", updatedPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_InvalidName_ReturnsValidationErrors()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-invalid@example.com",
            displayName: "Valid Name",
            subject: "google-settings-invalid",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var response = await client.PostAsync(
            "/account/settings",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["DisplayName"] = " ",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("required", body, StringComparison.OrdinalIgnoreCase);
        // Form value retained (empty after trim may show empty input; original name should not be silently reapplied as success).
        Assert.DoesNotContain("Display name updated.", body);
    }

    [Fact]
    public async Task Post_TooShortName_ReturnsValidationErrors()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-short@example.com",
            displayName: "Valid Name",
            subject: "google-settings-short",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var response = await client.PostAsync(
            "/account/settings",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
                ["DisplayName"] = "A",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("between", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Display name updated.", body);
        Assert.Contains("value=\"A\"", body);
    }

    [Fact]
    public async Task SignedInHeader_IncludesSettingsLink()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-nav@example.com",
            displayName: "Nav Fan",
            subject: "google-settings-nav");

        var body = await client.GetStringAsync("/fan-performances");

        Assert.Contains("href=\"/account/settings\"", body);
        Assert.Contains(">Settings</a>", body);
        Assert.Contains("href=\"/following\"", body);
        Assert.Contains(">Following</a>", body);
        Assert.Contains("qz-avatar", body);
    }

    [Fact]
    public async Task PostUploadAvatar_StoresAvatar_AndShowsImageOnSettings()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-avatar@example.com",
            displayName: "Avatar Uploader",
            subject: "google-settings-avatar",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/account/settings");
        var token = ExtractAntiforgeryToken(formPage);
        await using var png = await CreatePngAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        var fileContent = new StreamContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "AvatarFile", "face.png");

        var response = await client.PostAsync("/account/settings?handler=UploadAvatar", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var updated = await client.GetStringAsync("/account/settings");
        Assert.Contains("Avatar updated.", updated);
        Assert.Contains("/account/avatar/", updated);
        Assert.Contains("Remove avatar", updated);
    }

    [Fact]
    public async Task PostRemoveAvatar_ClearsAvatar()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "settings-remove-avatar@example.com",
            displayName: "Remove Avatar Fan",
            subject: "google-settings-remove-avatar",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        // Upload first.
        var formPage = await client.GetStringAsync("/account/settings");
        await using var png = await CreatePngAsync();
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new StringContent(ExtractAntiforgeryToken(formPage)), "__RequestVerificationToken");
            var fileContent = new StreamContent(png);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "AvatarFile", "face.png");
            var upload = await client.PostAsync("/account/settings?handler=UploadAvatar", content);
            Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        }

        var afterUpload = await client.GetStringAsync("/account/settings");
        Assert.Contains("Remove avatar", afterUpload);
        using var remove = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(afterUpload),
        });
        var removeResponse = await client.PostAsync("/account/settings?handler=RemoveAvatar", remove);
        Assert.Equal(HttpStatusCode.Redirect, removeResponse.StatusCode);

        var afterRemove = await client.GetStringAsync("/account/settings");
        Assert.Contains("Avatar removed.", afterRemove);
        Assert.DoesNotContain("Remove avatar", afterRemove);
    }

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(40, 40, new Rgba32(10, 180, 90));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(
        string email,
        string displayName,
        string subject,
        WebApplicationFactoryClientOptions? options = null) =>
        await CreateSignedInMemberClientAsync(factory, email, displayName, subject, options);

    private async Task<HttpClient> CreateSignedInMemberClientWithLegacyMatchAsync(
        string email,
        string displayName,
        string subject,
        int legacyUserId,
        string legacyUsername,
        WebApplicationFactoryClientOptions? options = null) =>
        await CreateSignedInMemberClientWithLegacyMatchesAsync(
            email,
            displayName,
            subject,
            [new LegacyMemberMatch(legacyUserId, legacyUsername)],
            options);

    private async Task<HttpClient> CreateSignedInMemberClientWithLegacyMatchesAsync(
        string email,
        string displayName,
        string subject,
        IReadOnlyList<LegacyMemberMatch> matches,
        WebApplicationFactoryClientOptions? options = null)
    {
        inspectableFactory.LegacyLookup.Seed(email, matches);
        return await CreateSignedInMemberClientAsync(factory, email, displayName, subject, options);
    }

    private static async Task<HttpClient> CreateSignedInMemberClientAsync(
        WebApplicationFactory<Program> sourceFactory,
        string email,
        string displayName,
        string subject,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = sourceFactory.CreateClient(options ?? new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callbackResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callbackResponse.StatusCode}");
        if (callbackResponse.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.DoesNotContain("/account/login", callbackResponse.Headers.Location!.OriginalString);
        }

        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
