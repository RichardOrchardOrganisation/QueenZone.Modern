using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class MeApiTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly QueenZoneWebApplicationFactory factory;

    public MeApiTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_RequiresMobileBearer_NotCookie()
    {
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var response = await client.GetAsync(MeApiEndpoints.Path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Get_ReturnsAccountSettingsFields_ForBearerMember()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Settings Fan", "settings-me@example.com");
        using var client = CreateBearerClient(memberId, "Settings Fan", "settings-me@example.com");

        using var response = await client.GetAsync(MeApiEndpoints.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var profile = await response.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.NotNull(profile);
        Assert.Equal(memberId, profile!.MemberId);
        Assert.Equal("settings-me@example.com", profile.Email);
        Assert.Equal("Settings Fan", profile.DisplayName);
        Assert.False(profile.HasAvatar);
        Assert.Null(profile.AvatarPath);
        Assert.Equal(MemberMessagePrivacy.Members, profile.MessagePrivacy);
        Assert.Equal(MemberAccountService.MinDisplayNameLength, profile.Limits.MinDisplayNameLength);
        Assert.Equal(MemberAccountService.MaxDisplayNameLength, profile.Limits.MaxDisplayNameLength);
        Assert.Equal(MemberAvatarPaths.MaxUploadBytes, profile.Limits.MaxAvatarBytes);
        Assert.Equal(AccountDeletionCopy.ConfirmationPhrase, profile.Deletion.ConfirmationPhrase);
        Assert.Equal(AccountDeletionCopy.RequestedTitle, profile.Deletion.RequestedTitle);
        Assert.Equal(AccountDeletionCopy.WhatHappens, profile.Deletion.WhatHappens);
        Assert.Equal(LegacyAccountLinkKind.None, profile.LegacyLink.Kind);
        Assert.Null(profile.ScheduledDeletionAt);
    }

    [Fact]
    public async Task Patch_UpdatesDisplayName_WithWebsiteValidation()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Old Name", "rename-me@example.com");
        using var client = CreateBearerClient(memberId, "Old Name", "rename-me@example.com");

        using var tooShort = await client.PatchAsJsonAsync(
            MeApiEndpoints.Path,
            new { displayName = "A" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        var shortProblem = await tooShort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("2", shortProblem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var renamed = await client.PatchAsJsonAsync(
            MeApiEndpoints.Path,
            new { displayName = "New Stage Name" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var profile = await renamed.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.Equal("New Stage Name", profile!.DisplayName);

        var stored = await FindMemberAsync(memberId);
        Assert.Equal("New Stage Name", stored.DisplayName);
    }

    [Fact]
    public async Task Patch_UpdatesMessagePrivacy()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Private Fan", "privacy-me@example.com");
        using var client = CreateBearerClient(memberId, "Private Fan", "privacy-me@example.com");

        using var response = await client.PatchAsJsonAsync(
            MeApiEndpoints.Path,
            new { messagePrivacy = "followed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.Equal(MemberMessagePrivacy.Followed, profile!.MessagePrivacy);
        Assert.Equal("Private Fan", profile.DisplayName);
    }

    [Fact]
    public async Task Patch_EmptyBody_ReturnsProblemDetails()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Empty Patch", "empty-patch@example.com");
        using var client = CreateBearerClient(memberId, "Empty Patch", "empty-patch@example.com");

        using var response = await client.PatchAsJsonAsync(MeApiEndpoints.Path, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("display name", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Avatar_UploadAndRemove_RoundTrip()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Avatar Fan", "avatar-me@example.com");
        using var client = CreateBearerClient(memberId, "Avatar Fan", "avatar-me@example.com");

        await using var png = await CreatePngAsync();
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "avatar.png");

        using var uploaded = await client.PostAsync($"{MeApiEndpoints.Path}/avatar", content);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var afterUpload = await uploaded.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.True(afterUpload!.HasAvatar);
        Assert.Equal(MemberAvatarPaths.GetServePath(memberId), afterUpload.AvatarPath);
        Assert.Equal(MemberAvatarPaths.GetServePath(memberId, thumb: true), afterUpload.AvatarThumbPath);

        using var image = await client.GetAsync(afterUpload.AvatarPath);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);

        using var removed = await client.DeleteAsync($"{MeApiEndpoints.Path}/avatar");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var afterRemove = await removed.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.False(afterRemove!.HasAvatar);
        Assert.Null(afterRemove.AvatarPath);
    }

    [Fact]
    public async Task Avatar_MissingFile_ReturnsProblemDetails()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "No File", "nofile-me@example.com");
        using var client = CreateBearerClient(memberId, "No File", "nofile-me@example.com");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("ignored"), "note");
        using var response = await client.PostAsync($"{MeApiEndpoints.Path}/avatar", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("image file", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyLink_ClaimAndUnlink()
    {
        const string email = "legacy-me@example.com";
        var memberId = Guid.NewGuid();
        using var specialized = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<ILegacyMemberLookupRepository>();
            services.AddSingleton<ILegacyMemberLookupRepository>(_ =>
                new InMemoryLegacyMemberLookupRepository(
                    new Dictionary<string, LegacyMemberMatch>(StringComparer.OrdinalIgnoreCase)
                    {
                        [email] = new LegacyMemberMatch(42, "ClassicFan"),
                    }));
        });

        await SeedMemberAsync(specialized, memberId, "Modern Fan", email);
        using var client = CreateBearerClient(specialized, memberId, "Modern Fan", email);

        using var before = await client.GetAsync(MeApiEndpoints.Path);
        var beforeProfile = await before.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.Equal(LegacyAccountLinkKind.Claimable, beforeProfile!.LegacyLink.Kind);
        Assert.Equal(42, beforeProfile.LegacyLink.Match?.UserId);

        using var claimed = await client.PostAsJsonAsync(
            $"{MeApiEndpoints.Path}/legacy-link",
            new { legacyUserId = 42, adoptDisplayName = true });
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
        var claimedProfile = await claimed.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.Equal(LegacyAccountLinkKind.Linked, claimedProfile!.LegacyLink.Kind);
        Assert.Equal("ClassicFan", claimedProfile.DisplayName);

        using var unlinked = await client.DeleteAsync($"{MeApiEndpoints.Path}/legacy-link");
        Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
        var unlinkedProfile = await unlinked.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.Equal(LegacyAccountLinkKind.Claimable, unlinkedProfile!.LegacyLink.Kind);
        Assert.Equal("ClassicFan", unlinkedProfile.DisplayName);
    }

    [Fact]
    public async Task Deletion_RequiresDeleteConfirmation_ThenMatchesWebsiteCopy()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Leaving Fan", "delete-me@example.com");
        using var client = CreateBearerClient(memberId, "Leaving Fan", "delete-me@example.com");

        using var missing = await client.PostAsJsonAsync(
            $"{MeApiEndpoints.Path}/deletion-request",
            new { confirmation = "please" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AccountDeletionCopy.ConfirmationRequired, missingProblem.GetProperty("detail").GetString());

        using var requested = await client.PostAsJsonAsync(
            $"{MeApiEndpoints.Path}/deletion-request",
            new { confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var payload = await requested.Content.ReadFromJsonAsync<DeletionRequestedResponse>(JsonOptions);
        Assert.True(payload!.Requested);
        Assert.Equal(AccountDeletionCopy.RequestedTitle, payload.Title);
        Assert.Equal(AccountDeletionCopy.RequestedMessage, payload.Message);

        var stored = await FindMemberAsync(memberId);
        Assert.NotNull(stored.DeletionRequestedAt);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, stored.DisplayName);
    }

    [Fact]
    public async Task Deletion_ImmediateRequest_PurgesAccountWithoutScheduledDate()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Immediate Fan", "immediate-delete@example.com");
        using var client = CreateBearerClient(memberId, "Immediate Fan", "immediate-delete@example.com");

        using var response = await client.PostAsJsonAsync(
            $"{MeApiEndpoints.Path}/deletion-request",
            new { confirmation = "DELETE", immediate = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DeletionRequestedResponse>(JsonOptions);
        Assert.True(payload!.Requested);
        Assert.Null(payload.ScheduledDeletionAt);
        Assert.Equal(AccountDeletionCopy.ImmediateTitle, payload.Title);
        Assert.False(string.IsNullOrWhiteSpace(payload.StatusReceipt));
        using var status = await client.GetAsync(
            "/api/v1/account-deletion-status?receipt=" + Uri.EscapeDataString(payload.StatusReceipt));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var progress = await status.Content.ReadFromJsonAsync<DeletionProgressResponse>(JsonOptions);
        Assert.Equal("complete", progress!.Status);
        using var invalidReceipt = await client.GetAsync("/api/v1/account-deletion-status?receipt=invalid");
        Assert.Equal(HttpStatusCode.NotFound, invalidReceipt.StatusCode);
        var stored = await FindMemberAsync(memberId);
        Assert.NotNull(stored.PersonalDataPurgedAt);
        Assert.True(stored.IsSuspended);
        Assert.Equal(MemberAccountDeletionPolicy.CreateDeletedEmail(memberId), stored.Email);
    }

    [Fact]
    public async Task Deletion_Cancel_RestoresProfile()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAsync(memberId, "Stay Fan", "cancel-me@example.com");
        using var client = CreateBearerClient(memberId, "Stay Fan", "cancel-me@example.com");

        using var requested = await client.PostAsJsonAsync(
            $"{MeApiEndpoints.Path}/deletion-request",
            new { confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        using var stale = await client.PostAsync($"{MeApiEndpoints.Path}/deletion-request/cancel", null);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        await WaitUntilClockMovesAsync();
        using var fresh = CreateBearerClient(memberId, "Stay Fan", "cancel-me@example.com");
        using var cancelled = await fresh.PostAsync($"{MeApiEndpoints.Path}/deletion-request/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var profile = await cancelled.Content.ReadFromJsonAsync<MemberProfileDto>(JsonOptions);
        Assert.Equal("Stay Fan", profile!.DisplayName);
        Assert.Null(profile.ScheduledDeletionAt);
    }

    [Fact]
    public async Task OpenApi_IncludesMeAndAuthProviderRoutes()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(ApiV1.OpenApiPath);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = payload.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/me", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/avatar", out _));
        Assert.True(paths.TryGetProperty("/api/v1/me/deletion-request", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/providers", out _));
    }

    [Fact]
    public async Task Get_AuthProviders_ListsConfiguredProviders()
    {
        using var specialized = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:Google:ClientId", "google-test-client");
            builder.UseSetting("Authentication:Google:ClientSecret", "google-test-secret");
            builder.UseSetting("Authentication:Microsoft:ClientId", "ms-test-client");
            builder.UseSetting("Authentication:Microsoft:ClientSecret", "ms-test-secret");
            builder.UseSetting("Authentication:Discord:ClientId", "discord-test-client");
            builder.UseSetting("Authentication:Discord:ClientSecret", "discord-test-secret");
            builder.UseSetting("Authentication:GitHub:ClientId", "github-test-client");
            builder.UseSetting("Authentication:GitHub:ClientSecret", "github-test-secret");
            builder.UseSetting("Authentication:Apple:ClientId", "apple-test-client");
            builder.UseSetting("Authentication:Apple:TeamId", "TEAMID");
            builder.UseSetting("Authentication:Apple:KeyId", "KEYID");
            builder.UseSetting("Authentication:Apple:PrivateKey", "test-apple-private-key");
        });
        using var client = specialized.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(MobileAuthEndpoints.ProvidersPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MobileAuthProvidersResponse>(JsonOptions);
        Assert.Equal(
            [
                MemberAuthenticationSchemes.Google,
                MemberAuthenticationSchemes.Microsoft,
                MemberAuthenticationSchemes.Discord,
                MemberAuthenticationSchemes.GitHub,
                MemberAuthenticationSchemes.Apple,
            ],
            payload!.Providers.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Get_AuthProviders_IsPublic()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(MobileAuthEndpoints.ProvidersPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MobileAuthProvidersResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Providers);
    }

    [Fact]
    public async Task WebsiteDeletePage_UsesTheSameConfirmationCopy()
    {
        using var client = factory.CreateAnonymousClient();
        var body = await client.GetStringAsync("/account/deletion-requested");
        Assert.Contains("Deleted member", body, StringComparison.Ordinal);
        Assert.Contains("30-day", body, StringComparison.Ordinal);
        Assert.Contains(AccountDeletionCopy.RequestedTitle, body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntilClockMovesAsync()
    {
        var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= start)
        {
            await Task.Delay(1);
        }
    }

    private HttpClient CreateBearerClient(Guid memberId, string displayName, string email) =>
        CreateBearerClient(factory, memberId, displayName, email);

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

    private async Task<MemberAccount> FindMemberAsync(Guid memberId)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.NotNull(account);
        return account!;
    }

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(40, 40, new Rgba32(10, 180, 90));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
