using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed class MobileAuthServiceTests
{
    [Fact]
    public void StartAuthorization_RejectsUnknownRedirectWithoutRedirecting()
    {
        var result = CreateService().StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            "https://evil.example/callback",
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            MobileAuthPkce.MethodS256,
            "state-1",
            MemberAuthenticationSchemes.Google);

        Assert.False(result.Success);
        Assert.False(result.RedirectSafe);
        Assert.Equal("invalid_request", result.Error);
    }

    [Theory]
    [InlineData("token")]
    [InlineData(null)]
    public void StartAuthorization_RequiresCodeResponseType(string? responseType)
    {
        var result = CreateService().StartAuthorization(
            responseType,
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            MobileAuthPkce.MethodS256,
            "state-1",
            MemberAuthenticationSchemes.Google);

        Assert.False(result.Success);
        Assert.Equal("invalid_request", result.Error);
    }

    [Theory]
    [InlineData("Facebook")]
    [InlineData("")]
    [InlineData(null)]
    public void StartAuthorization_RejectsUnknownProvider(string? provider)
    {
        var result = CreateService().StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            MobileAuthPkce.MethodS256,
            "state-1",
            provider);

        Assert.False(result.Success);
        Assert.Equal("invalid_request", result.Error);
        Assert.True(result.RedirectSafe);
    }

    [Fact]
    public void StartAuthorization_RejectsPlainCodeChallengeMethod()
    {
        var result = CreateService().StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            "plain",
            "state-1",
            MemberAuthenticationSchemes.Google);

        Assert.False(result.Success);
        Assert.Equal("invalid_request", result.Error);
    }

    [Fact]
    public void StartAuthorization_StoresPkceSession()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var result = CreateService().StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            "google");

        Assert.True(result.Success);
        Assert.NotNull(result.Session);
        Assert.Equal(MemberAuthenticationSchemes.Google, result.Session.Provider);
        Assert.Equal(pair.Challenge, result.Session.CodeChallenge);
        Assert.Equal("csrf-state", result.Session.State);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_IssuesTokens_AndRejectsReuse()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var service = CreateService();
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.GitHub);
        var completed = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.GitHub,
            "gh-subject-1",
            "fan@example.com",
            "Fan",
            true,
            CancellationToken.None);

        Assert.True(completed.Success);
        Assert.False(string.IsNullOrWhiteSpace(completed.Code));

        var tokens = await service.ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            completed.Code,
            pair.Verifier,
            CancellationToken.None);

        Assert.True(tokens.Success);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal(15 * 60, tokens.ExpiresIn);

        var reused = await service.ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            completed.Code,
            pair.Verifier,
            CancellationToken.None);

        Assert.False(reused.Success);
        Assert.Equal("invalid_grant", reused.Error);
        Assert.Null(reused.RefreshToken);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_RejectsWrongVerifier()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var other = MobileAuthPkceTestData.CreatePair();
        var service = CreateService();
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Microsoft);
        var completed = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Microsoft,
            "ms-subject-1",
            "msfan@example.com",
            "MS Fan",
            true,
            CancellationToken.None);

        var tokens = await service.ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            completed.Code,
            other.Verifier,
            CancellationToken.None);

        Assert.False(tokens.Success);
        Assert.Equal("invalid_grant", tokens.Error);
    }

    [Fact]
    public async Task CompleteExternalLogin_RejectsSuspendedAccount()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var members = new InMemoryMemberAccountRepository();
        var service = CreateService(members);
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Google);
        var first = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "suspended-subject",
            "suspended@example.com",
            "Suspended",
            true,
            CancellationToken.None);
        Assert.True(first.Success);

        var account = await members.FindByExternalLoginAsync(MemberAuthenticationSchemes.Google, "suspended-subject");
        Assert.NotNull(account);
        account!.IsSuspended = true;

        var secondStart = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state-2",
            MemberAuthenticationSchemes.Google);
        var completed = await service.CompleteExternalLoginAsync(
            secondStart.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "suspended-subject",
            "suspended@example.com",
            "Suspended",
            true,
            CancellationToken.None);

        Assert.False(completed.Success);
        Assert.Equal("access_denied", completed.Error);
        Assert.Equal("account_suspended", completed.ErrorDescription);
    }

    [Fact]
    public async Task CompleteExternalLogin_DoesNotIssueCode_WhenVerifiedEmailNeedsConfirmation()
    {
        var members = new InMemoryMemberAccountRepository();
        var account = await SeedPasswordAccountAsync(members, "confirm-mobile@example.com", "S3curePass!");
        var service = CreateService(members);
        var pair = MobileAuthPkceTestData.CreatePair();
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Google);

        var pending = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "google-mobile-confirm",
            "confirm-mobile@example.com",
            "Confirm Fan",
            emailVerified: true,
            CancellationToken.None);

        Assert.True(pending.RequiresConfirmation);
        Assert.False(pending.Success);
        Assert.Null(pending.Code);
        Assert.Null(await members.FindByExternalLoginAsync(MemberAuthenticationSchemes.Google, "google-mobile-confirm"));

        var linker = CreateMemberAccountService(members);
        var linked = await linker.LinkExternalLoginAsync(
            account.Id,
            MemberAuthenticationSchemes.Google,
            "google-mobile-confirm",
            "confirm-mobile@example.com");
        Assert.True(linked.Succeeded);

        var completed = await service.CompleteConfirmedLoginAsync(
            started.Session.RequestId,
            linked.Account!,
            CancellationToken.None);
        Assert.True(completed.Success);
        Assert.False(string.IsNullOrWhiteSpace(completed.Code));

        var tokens = await service.ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            completed.Code,
            pair.Verifier,
            CancellationToken.None);
        Assert.True(tokens.Success);
    }

    [Fact]
    public async Task CompleteExternalLogin_RejectsUnverifiedEmail_WithoutCreatingAnAccount()
    {
        var members = new InMemoryMemberAccountRepository();
        var service = CreateService(members);
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            MobileAuthPkceTestData.CreatePair().Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Google);

        var completed = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "google-mobile-unverified",
            "unverified-mobile@example.com",
            "Unverified",
            emailVerified: false,
            CancellationToken.None);

        Assert.False(completed.Success);
        Assert.False(completed.RequiresConfirmation);
        Assert.Equal(ExternalLoginMessages.UnverifiedEmail, completed.ErrorDescription);
        Assert.Null(await members.FindByEmailAsync("unverified-mobile@example.com"));
        Assert.Null(await members.FindByExternalLoginAsync(MemberAuthenticationSchemes.Google, "google-mobile-unverified"));
    }

    [Fact]
    public async Task CompleteExternalLogin_RejectsMissingRequest()
    {
        var completed = await CreateService().CompleteExternalLoginAsync(
            null,
            MemberAuthenticationSchemes.Google,
            "subject",
            "fan@example.com",
            "Fan",
            true,
            CancellationToken.None);

        Assert.False(completed.Success);
        Assert.Equal("invalid_request", completed.Error);
        Assert.Null(completed.RedirectUri);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_RejectsUnsupportedGrantType()
    {
        var result = await CreateService().ExchangeAuthorizationCodeAsync(
            "password",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            "code",
            "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("unsupported_grant_type", result.Error);
    }

    [Fact]
    public async Task ExchangeRefreshToken_RotatesAndRejectsReuseAfterTheGraceWindow()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var issued = await IssueTokensAsync(timeProvider: time);

        var refreshed = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.True(refreshed.Success);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshed.RefreshToken));
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        Assert.DoesNotContain(issued.RefreshToken!, refreshed.ErrorDescription ?? string.Empty);

        // Past the reuse grace window, a replay of the rotated-away token is
        // theft, not a lost rotation response.
        time.Advance(TimeSpan.FromSeconds(31));
        var reused = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(reused.Success);
        Assert.Equal("invalid_grant", reused.Error);
        Assert.Null(reused.RefreshToken);
        Assert.DoesNotContain(issued.RefreshToken!, reused.ErrorDescription ?? string.Empty);
        Assert.DoesNotContain(issued.RefreshToken!, reused.Error ?? string.Empty);
    }

    [Fact]
    public async Task ExchangeRefreshToken_LogsReuseBeforeRevokingEveryGrant()
    {
        // Reuse revokes every grant the member holds, signing them out on every
        // device. Past the grace window, a client that lost a rotation response
        // looks identical to a stolen token from here, so the decision has to be
        // traceable.
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var log = new RecordingServiceLogger();
        var issued = await IssueTokensAsync(serviceLogger: log, timeProvider: time);
        await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(31));
        var reused = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(reused.Success);
        var warning = Assert.Single(
            log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("reuse detected", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("revoking all grants", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(issued.RefreshToken!, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeRefreshToken_WithinGraceWindow_RecoversALostRotationResponseInsteadOfRevokingEverything()
    {
        // The client rotated once but never saw the response (killed mid launch,
        // dropped connection, ...) and retries with the same, now-rotated-away
        // refresh token a moment later. That must not sign the member out.
        var log = new RecordingServiceLogger();
        var issued = await IssueTokensAsync(serviceLogger: log);
        var firstRotation = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);
        Assert.True(firstRotation.Success);

        var retried = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.True(retried.Success);
        Assert.False(string.IsNullOrWhiteSpace(retried.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(retried.RefreshToken));
        Assert.NotEqual(firstRotation.RefreshToken, retried.RefreshToken);
        Assert.DoesNotContain(
            log.Entries,
            entry => entry.Message.Contains("reuse detected", StringComparison.OrdinalIgnoreCase));

        // The recovered pair is fully usable going forward.
        var next = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            retried.RefreshToken,
            CancellationToken.None);
        Assert.True(next.Success);
    }

    [Fact]
    public async Task ExchangeRefreshToken_WithinGraceWindow_StillRevokesEverythingWhenTheChainDeadEnds()
    {
        // The rotated-away token is replayed, but whatever replaced it was itself
        // revoked outside any grace window (e.g. the member signed out in the
        // meantime) — there is no live grant to recover, so this still reads as
        // theft.
        var log = new RecordingServiceLogger();
        var issued = await IssueTokensAsync(serviceLogger: log);
        var firstRotation = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);
        Assert.True(firstRotation.Success);

        await issued.Service.RevokeRefreshTokenAsync(firstRotation.RefreshToken, CancellationToken.None);

        var replayed = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(replayed.Success);
        Assert.Equal("invalid_grant", replayed.Error);
        Assert.Contains(
            log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("reuse detected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExchangeRefreshToken_LogsAnUnknownGrantWithoutEchoingTheToken()
    {
        // The common cause is a grant issued by another environment: TestFlight
        // ships staging and production under one bundle id.
        var log = new RecordingServiceLogger();
        var service = CreateService(serviceLogger: log);

        var result = await service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            "grant-from-another-origin",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_grant", result.Error);
        var entry = Assert.Single(
            log.Entries,
            e => e.Message.Contains("no grant matches", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("grant-from-another-origin", entry.Message, StringComparison.Ordinal);
        Assert.Contains(MobileAuthOptions.DefaultClientId, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeRefreshToken_UnknownGrant_LogsConfiguredClientIdNotRequestParameter()
    {
        var log = new RecordingServiceLogger();
        const string configuredClientId = "configured-mobile-client";
        var service = CreateService(
            serviceLogger: log,
            mobileOptions: new MobileAuthOptions { ClientId = configuredClientId });

        var result = await service.ExchangeRefreshTokenAsync(
            configuredClientId,
            "grant-from-another-origin",
            CancellationToken.None);

        Assert.False(result.Success);
        var entry = Assert.Single(
            log.Entries,
            e => e.Message.Contains("no grant matches", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(configuredClientId, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("grant-from-another-origin", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeRefreshToken_RejectsAndLogsTheLoserOfARotationRace()
    {
        var log = new RecordingServiceLogger();
        var grants = new RotationRaceLoserGrantRepository(
            new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore()));
        var issued = await IssueTokensAsync(serviceLogger: log, grants: grants);

        grants.LoseNextRotation = true;
        var lost = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(lost.Success);
        Assert.Equal("invalid_grant", lost.Error);
        Assert.Null(lost.RefreshToken);
        var warning = Assert.Single(
            log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("rotation race", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(issued.RefreshToken!, warning.Message, StringComparison.Ordinal);

        // The race loser must not take the whole account down with it: the grant
        // is still there for the winner, so a later refresh still works.
        var recovered = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);
        Assert.True(recovered.Success);
    }

    [Fact]
    public async Task ExchangeRefreshToken_RevokesEveryGrantWhenTheAccountIsSuspended()
    {
        var log = new RecordingServiceLogger();
        var accounts = new InMemoryMemberAccountRepository();
        var issued = await IssueTokensAsync(serviceLogger: log, accounts: accounts);

        var account = await accounts.FindByExternalLoginAsync(
            MemberAuthenticationSchemes.Google,
            "refresh-subject-1");
        Assert.NotNull(account);
        account!.IsSuspended = true;

        var result = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_grant", result.Error);
        Assert.Single(
            log.Entries,
            entry => entry.Message.Contains("missing or suspended", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExchangeRefreshToken_RejectsRevokedToken()
    {
        var issued = await IssueTokensAsync();
        await issued.Service.RevokeRefreshTokenAsync(issued.RefreshToken, CancellationToken.None);

        var refreshed = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(refreshed.Success);
        Assert.Equal("invalid_grant", refreshed.Error);
        Assert.DoesNotContain(issued.RefreshToken!, refreshed.ErrorDescription ?? string.Empty);
    }

    [Fact]
    public async Task ExchangeRefreshToken_RejectsExpiredToken()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(timeProvider: time);
        var pair = MobileAuthPkceTestData.CreatePair();
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Google);
        var completed = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "expired-refresh-subject",
            "expired-refresh@example.com",
            "Expired Refresh",
            true,
            CancellationToken.None);
        var issued = await service.ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            completed.Code,
            pair.Verifier,
            CancellationToken.None);
        Assert.True(issued.Success);

        time.Advance(TimeSpan.FromDays(31));
        var refreshed = await service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(refreshed.Success);
        Assert.Equal("invalid_grant", refreshed.Error);
        Assert.DoesNotContain(issued.RefreshToken!, refreshed.ErrorDescription ?? string.Empty);
    }

    [Fact]
    public async Task RevokeAllRefreshTokensForMember_InvalidatesActiveRefresh()
    {
        var issued = await IssueTokensAsync();
        var session = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);
        Assert.True(session.Success);

        var memberId = Guid.Parse(
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ReadJwtToken(session.AccessToken).Subject);
        await issued.Service.RevokeAllRefreshTokensForMemberAsync(memberId, CancellationToken.None);

        var afterRevoke = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            session.RefreshToken,
            CancellationToken.None);

        Assert.False(afterRevoke.Success);
        Assert.Equal("invalid_grant", afterRevoke.Error);
    }

    [Fact]
    public async Task CompleteExternalLogin_RejectsProviderMismatch()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var service = CreateService();
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Discord);

        var completed = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "discord-subject-1",
            "mix@example.com",
            "Mix",
            true,
            CancellationToken.None);

        Assert.False(completed.Success);
        Assert.Equal("access_denied", completed.Error);
        Assert.Null(completed.Code);
    }

    [Fact]
    public void StartAuthorization_FailsClosed_WhenProductionSigningKeyMissing()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var result = CreateService(environmentName: "Production").StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "state-1",
            MemberAuthenticationSchemes.Google);

        Assert.False(result.Success);
        Assert.Equal("temporarily_unavailable", result.Error);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_FailsClosed_WhenProductionSigningKeyMissing()
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var result = await CreateService(environmentName: "Production").ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            "unused-code",
            pair.Verifier,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("temporarily_unavailable", result.Error);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task ExchangeRefreshToken_FailsClosed_WhenProductionSigningKeyMissing()
    {
        var result = await CreateService(environmentName: "Production").ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            "unused-refresh-token",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("temporarily_unavailable", result.Error);
        Assert.Null(result.RefreshToken);
    }

    [Fact]
    public async Task ExchangePasswordGrant_IssuesTokens_ForValidCredentials()
    {
        var members = new InMemoryMemberAccountRepository();
        await SeedPasswordAccountAsync(members, "reviewer@example.com", "S3curePass!");
        var service = CreateService(members);

        var tokens = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "reviewer@example.com",
            "S3curePass!",
            CancellationToken.None);

        Assert.True(tokens.Success);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal(15 * 60, tokens.ExpiresIn);
        Assert.Equal(StatusCodes.Status200OK, tokens.StatusCode);
    }

    [Fact]
    public async Task ExchangePasswordGrant_RejectsWrongPassword_WithoutAccountEnumeration()
    {
        var members = new InMemoryMemberAccountRepository();
        await SeedPasswordAccountAsync(members, "reviewer@example.com", "S3curePass!");
        var service = CreateService(members);

        var wrong = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "reviewer@example.com",
            "not-the-password",
            CancellationToken.None);
        var unknown = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "nobody@example.com",
            "S3curePass!",
            CancellationToken.None);

        Assert.False(wrong.Success);
        Assert.False(unknown.Success);
        Assert.Equal("invalid_grant", wrong.Error);
        Assert.Equal("invalid_grant", unknown.Error);
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, wrong.ErrorDescription);
        Assert.Equal(unknown.ErrorDescription, wrong.ErrorDescription);
        Assert.DoesNotContain("reviewer@example.com", wrong.ErrorDescription ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("nobody@example.com", unknown.ErrorDescription ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(wrong.AccessToken);
        Assert.Null(unknown.RefreshToken);
    }

    [Fact]
    public async Task ExchangePasswordGrant_RejectsUnknownClientAndMissingFields()
    {
        var members = new InMemoryMemberAccountRepository();
        await SeedPasswordAccountAsync(members, "reviewer@example.com", "S3curePass!");
        var service = CreateService(members);

        var unknownClient = await service.ExchangePasswordGrantAsync(
            "not-the-mobile-client",
            "reviewer@example.com",
            "S3curePass!",
            CancellationToken.None);
        var missingPassword = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "reviewer@example.com",
            "   ",
            CancellationToken.None);

        Assert.False(unknownClient.Success);
        Assert.False(missingPassword.Success);
        Assert.Equal("invalid_grant", unknownClient.Error);
        Assert.Equal("invalid_grant", missingPassword.Error);
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, unknownClient.ErrorDescription);
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, missingPassword.ErrorDescription);
    }

    [Fact]
    public async Task ExchangePasswordGrant_RejectsSuspendedAccount()
    {
        var members = new InMemoryMemberAccountRepository();
        var registered = await SeedPasswordAccountAsync(members, "suspended@example.com", "S3curePass!");
        registered.IsSuspended = true;
        var service = CreateService(members);

        var tokens = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "suspended@example.com",
            "S3curePass!",
            CancellationToken.None);

        Assert.False(tokens.Success);
        Assert.Equal("invalid_grant", tokens.Error);
        Assert.Equal(MemberAccountService.SuspendedSignInError, tokens.ErrorDescription);
        Assert.Null(tokens.AccessToken);
    }

    [Fact]
    public async Task ExchangePasswordGrant_FailsClosed_WhenProductionSigningKeyMissing()
    {
        var result = await CreateService(environmentName: "Production").ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "reviewer@example.com",
            "S3curePass!",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("temporarily_unavailable", result.Error);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task ExchangePasswordGrant_RateLimitsRepeatedSignInForSameAccount()
    {
        var members = new InMemoryMemberAccountRepository();
        await SeedPasswordAccountAsync(members, "rate@example.com", "S3curePass!");
        var service = CreateService(
            members,
            authLimits: new AuthRateLimitingOptions { AccountPermitLimit = 1, AccountWindowMinutes = 60 });

        var first = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "rate@example.com",
            "S3curePass!",
            CancellationToken.None);
        var second = await service.ExchangePasswordGrantAsync(
            MobileAuthOptions.DefaultClientId,
            "rate@example.com",
            "S3curePass!",
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("temporarily_unavailable", second.Error);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.StatusCode);
        Assert.Equal(MobileAuthAccountRateLimiter.ClientMessage, second.ErrorDescription);
        Assert.DoesNotContain("rate@example.com", second.ErrorDescription ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteExternalLogin_RateLimitsRepeatedSignInForSameAccount()
    {
        var logger = new RecordingAuthLogger();
        var service = CreateService(
            authLimits: new AuthRateLimitingOptions { AccountPermitLimit = 1, AccountWindowMinutes = 60 },
            logger: logger);
        var first = await CompleteGoogleSignInAsync(service, "rate-subject", "rate@example.com");
        var secondStart = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            MobileAuthPkceTestData.CreatePair().Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state-2",
            MemberAuthenticationSchemes.Google);
        var second = await service.CompleteExternalLoginAsync(
            secondStart.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "rate-subject",
            "rate@example.com",
            "Rate Fan",
            true,
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("temporarily_unavailable", second.Error);
        Assert.Equal(MobileAuthAccountRateLimiter.ClientMessage, second.ErrorDescription);
        Assert.Contains(logger.Messages, message => message.Contains("account partition", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("rate@example.com", StringComparison.Ordinal));
        Assert.All(logger.Messages, message => Assert.DoesNotContain("token", message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExchangeRefreshToken_RateLimitsWithoutConsumingRefreshToken()
    {
        var tight = new AuthRateLimitingOptions { AccountPermitLimit = 1, AccountWindowMinutes = 60 };
        var issued = await IssueTokensAsync(tight);
        var first = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);
        var retry = await issued.Service.ExchangeRefreshTokenAsync(
            MobileAuthOptions.DefaultClientId,
            issued.RefreshToken,
            CancellationToken.None);

        Assert.False(first.Success);
        Assert.Equal(StatusCodes.Status429TooManyRequests, first.StatusCode);
        Assert.Equal("temporarily_unavailable", first.Error);
        Assert.DoesNotContain(issued.RefreshToken, first.ErrorDescription ?? string.Empty);
        Assert.False(retry.Success);
        Assert.Equal(StatusCodes.Status429TooManyRequests, retry.StatusCode);
        Assert.DoesNotContain(issued.RefreshToken, retry.ErrorDescription ?? string.Empty);
    }

    private static async Task<MobileAuthCallbackResult> CompleteGoogleSignInAsync(
        MobileAuthService service,
        string subject,
        string email)
    {
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            MobileAuthPkceTestData.CreatePair().Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Google);
        return await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            subject,
            email,
            "Rate Fan",
            true,
            CancellationToken.None);
    }

    private static async Task<(MobileAuthService Service, string RefreshToken)> IssueTokensAsync(
        AuthRateLimitingOptions? authLimits = null,
        ILogger<MobileAuthService>? serviceLogger = null,
        InMemoryMemberAccountRepository? accounts = null,
        IMobileAuthGrantRepository? grants = null,
        TimeProvider? timeProvider = null)
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        var service = CreateService(
            accounts,
            timeProvider: timeProvider,
            authLimits: authLimits,
            serviceLogger: serviceLogger,
            grants: grants);
        var started = service.StartAuthorization(
            "code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            pair.Challenge,
            MobileAuthPkce.MethodS256,
            "csrf-state",
            MemberAuthenticationSchemes.Google);
        var completed = await service.CompleteExternalLoginAsync(
            started.Session!.RequestId,
            MemberAuthenticationSchemes.Google,
            "refresh-subject-1",
            "refresh@example.com",
            "Refresh Fan",
            true,
            CancellationToken.None);
        var tokens = await service.ExchangeAuthorizationCodeAsync(
            "authorization_code",
            MobileAuthOptions.DefaultClientId,
            MobileAuthPkceTestData.RedirectUri,
            completed.Code,
            pair.Verifier,
            CancellationToken.None);
        Assert.True(tokens.Success);
        return (service, tokens.RefreshToken!);
    }

    private static MemberAccountService CreateMemberAccountService(InMemoryMemberAccountRepository members) =>
        new(
            members,
            new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>()),
            new AzureBlobUploadService(new InMemoryBlobStorageBackend(), Options.Create(new BlobUploadOptions())),
            new MemberUploadQuotaService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                TimeProvider.System,
                Options.Create(new UploadQuotaOptions { Enabled = false })));

    private static async Task<MemberAccount> SeedPasswordAccountAsync(
        InMemoryMemberAccountRepository members,
        string email,
        string password)
    {
        var accounts = CreateMemberAccountService(members);
        var registered = await accounts.RegisterAsync(email, password, "Reviewer");
        Assert.True(registered.Succeeded, registered.Error);
        Assert.NotNull(registered.Account);
        return registered.Account;
    }

    private static MobileAuthService CreateService(
        InMemoryMemberAccountRepository? accounts = null,
        string environmentName = "Testing",
        TimeProvider? timeProvider = null,
        AuthRateLimitingOptions? authLimits = null,
        ILogger<MobileAuthAccountRateLimiter>? logger = null,
        ILogger<MobileAuthService>? serviceLogger = null,
        IMobileAuthGrantRepository? grants = null,
        MobileAuthOptions? mobileOptions = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var options = Options.Create(mobileOptions ?? new MobileAuthOptions());
        var site = Options.Create(new SiteOptions());
        var environment = new FakeHostEnvironment(environmentName);
        var members = new MemberAccountService(
            accounts ?? new InMemoryMemberAccountRepository(),
            new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>()),
            new AzureBlobUploadService(new InMemoryBlobStorageBackend(), Options.Create(new BlobUploadOptions())),
            new MemberUploadQuotaService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                clock,
                Options.Create(new UploadQuotaOptions { Enabled = false })));

        return new MobileAuthService(
            new MobileAuthAuthorizationSessionStore(clock),
            grants ?? new InMemoryMobileAuthGrantRepository(new SharedMobileAuthGrantStore()),
            new MobileAuthTokenIssuer(options, site, environment, clock),
            members,
            new MobileAuthAccountRateLimiter(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                clock,
                Options.Create(authLimits ?? new AuthRateLimitingOptions()),
                logger ?? NullLogger<MobileAuthAccountRateLimiter>.Instance),
            options,
            clock,
            serviceLogger ?? NullLogger<MobileAuthService>.Instance);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }

    private sealed class RecordingAuthLogger : ILogger<MobileAuthAccountRateLimiter>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    /// <summary>
    /// Rotation loser: the grant is readable and unrevoked, but a concurrent
    /// refresh claims it first, so <c>TryRevokeRefreshTokenAsync</c> comes back
    /// false. Forcing it beats racing two real refreshes in a test.
    /// </summary>
    private sealed class RotationRaceLoserGrantRepository(IMobileAuthGrantRepository inner)
        : IMobileAuthGrantRepository
    {
        public bool LoseNextRotation { get; set; }

        public Task StoreAuthorizationCodeAsync(
            MobileAuthAuthorizationCodeEntity code,
            CancellationToken cancellationToken = default) =>
            inner.StoreAuthorizationCodeAsync(code, cancellationToken);

        public Task<MobileAuthAuthorizationCodeEntity?> RedeemAuthorizationCodeAsync(
            string codeHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            inner.RedeemAuthorizationCodeAsync(codeHash, utcNow, cancellationToken);

        public Task StoreRefreshTokenAsync(
            MobileAuthRefreshTokenEntity token,
            CancellationToken cancellationToken = default) =>
            inner.StoreRefreshTokenAsync(token, cancellationToken);

        public Task<MobileAuthRefreshTokenEntity?> FindRefreshTokenByHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            inner.FindRefreshTokenByHashAsync(tokenHash, cancellationToken);

        public Task<bool> TryRevokeRefreshTokenAsync(
            string tokenHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            if (LoseNextRotation)
            {
                LoseNextRotation = false;
                return Task.FromResult(false);
            }

            return inner.TryRevokeRefreshTokenAsync(tokenHash, utcNow, cancellationToken);
        }

        public Task<int> RevokeAllRefreshTokensForMemberAsync(
            Guid memberAccountId,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            inner.RevokeAllRefreshTokensForMemberAsync(memberAccountId, utcNow, cancellationToken);

        public Task<bool> LinkRefreshTokenRotationAsync(
            string oldTokenHash,
            string newTokenHash,
            CancellationToken cancellationToken = default) =>
            inner.LinkRefreshTokenRotationAsync(oldTokenHash, newTokenHash, cancellationToken);
    }

    private sealed class RecordingServiceLogger : ILogger<MobileAuthService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
