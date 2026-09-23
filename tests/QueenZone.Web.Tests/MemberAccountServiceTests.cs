using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class MemberAccountServiceTests
{
    private static MemberAccountService CreateService(
        IMemberAccountRepository? memberAccountRepository = null,
        ILegacyMemberLookupRepository? legacyMemberLookupRepository = null,
        IBlobUploadService? blobUploadService = null,
        InMemoryBlobStorageBackend? blobBackend = null,
        MemberUploadQuotaService? uploadQuota = null,
        TimeSpan? blobDeleteTimeout = null,
        TimeProvider? timeProvider = null,
        PasswordSignInLockoutOptions? passwordLockout = null,
        IOutboundEmailSender? emailSender = null)
    {
        var backend = blobBackend ?? new InMemoryBlobStorageBackend();
        var blobs = blobUploadService
            ?? new AzureBlobUploadService(backend, Options.Create(new BlobUploadOptions()));
        return new MemberAccountService(
            memberAccountRepository ?? new InMemoryMemberAccountRepository(),
            legacyMemberLookupRepository ?? new InMemoryLegacyMemberLookupRepository(
                new Dictionary<string, LegacyMemberMatch>()),
            blobs,
            uploadQuota ?? CreateDisabledUploadQuota(),
            timeProvider,
            passwordLockout is null ? null : Options.Create(passwordLockout),
            emailSender: emailSender)
        {
            BlobDeleteTimeout = blobDeleteTimeout ?? MemberAccountService.DefaultBlobDeleteTimeout,
        };
    }

    private sealed class LockoutClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }

    private static MemberUploadQuotaService CreateDisabledUploadQuota() =>
        new(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            TimeProvider.System,
            Options.Create(new UploadQuotaOptions { Enabled = false }));

    private static async Task<MemoryStream> CreatePngAsync(int width = 32, int height = 32)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 120, 200));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task RegisterAsync_CreatesAccount_WithoutSilentLegacyLink()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>
        {
            ["fan@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
        });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);

        var result = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "New Fan");

        Assert.True(result.Succeeded);
        Assert.Null(result.Account!.LinkedLegacyUserId);
        Assert.Equal("fan@queenzone.org", result.Account.Email);

        var state = await service.GetLegacyLinkStateAsync(result.Account);
        Assert.Equal(LegacyAccountLinkKind.Claimable, state.Kind);
        Assert.Equal(123, state.Match!.UserId);
        Assert.Equal("OldFan", state.Match.Username);
    }

    [Fact]
    public async Task RegisterAsync_LeavesLinkNull_WhenNoLegacyMatch()
    {
        var service = CreateService();

        var result = await service.RegisterAsync("nobody@example.com", "S3curePass!", "Nobody");

        Assert.True(result.Succeeded);
        Assert.Null(result.Account!.LinkedLegacyUserId);

        var state = await service.GetLegacyLinkStateAsync(result.Account);
        Assert.Equal(LegacyAccountLinkKind.None, state.Kind);
    }

    [Fact]
    public async Task RegisterAsync_Fails_WhenEmailAlreadyRegistered()
    {
        var service = CreateService();
        await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        var result = await service.RegisterAsync("fan@queenzone.org", "DifferentPass!", "Fan Again");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SignInAsync_Succeeds_WithCorrectPassword()
    {
        var service = CreateService();
        await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        var result = await service.SignInAsync("fan@queenzone.org", "S3curePass!");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_Fails_WithWrongPassword()
    {
        var service = CreateService();
        await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        var result = await service.SignInAsync("fan@queenzone.org", "WrongPassword!");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_Fails_WhenAccountDoesNotExist()
    {
        var service = CreateService();

        var result = await service.SignInAsync("ghost@example.com", "whatever");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SignInAsync_Fails_WhenAccountIsSuspended()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        await repository.SuspendAsync(registered.Account!.Id, "Spamming the board", "admin@queenzone.org", DateTime.UtcNow);

        var result = await service.SignInAsync("fan@queenzone.org", "S3curePass!");

        Assert.False(result.Succeeded);
        Assert.Equal(MemberAccountService.InvalidPasswordSignInError, result.Error);
    }

    [Fact]
    public async Task SignInAsync_UsesTheSameError_ForMissingWrongAndSuspendedAccounts()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        await repository.SuspendAsync(registered.Account!.Id, "Spamming the board", "admin@queenzone.org", DateTime.UtcNow);

        var missing = await service.SignInAsync("ghost@example.com", "whatever");
        var wrong = await service.SignInAsync("fan@queenzone.org", "WrongPassword!");
        var suspended = await service.SignInAsync("fan@queenzone.org", "S3curePass!");

        Assert.Equal(MemberAccountService.InvalidPasswordSignInError, missing.Error);
        Assert.Equal(missing.Error, wrong.Error);
        Assert.Equal(missing.Error, suspended.Error);
        Assert.DoesNotContain("suspended", suspended.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt", suspended.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignInAsync_LocksTheAccount_AndSuccessfulSignInClearsTheCounter()
    {
        var repository = new InMemoryMemberAccountRepository();
        var clock = new LockoutClock(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            memberAccountRepository: repository,
            timeProvider: clock,
            passwordLockout: new PasswordSignInLockoutOptions { MaxFailures = 2, WindowMinutes = 15 });
        await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        Assert.False((await service.SignInAsync("fan@queenzone.org", "nope-1")).Succeeded);
        Assert.False((await service.SignInAsync("fan@queenzone.org", "nope-2")).Succeeded);
        var locked = await service.SignInAsync("fan@queenzone.org", "S3curePass!");
        Assert.False(locked.Succeeded);
        Assert.Equal(MemberAccountService.InvalidPasswordSignInError, locked.Error);

        var stored = await repository.FindByEmailAsync("fan@queenzone.org");
        Assert.Equal(2, stored!.PasswordFailureCount);

        var cleared = await service.SignInAsync("fan@queenzone.org", "S3curePass!");
        Assert.False(cleared.Succeeded);

        clock.Advance(TimeSpan.FromMinutes(15));
        var afterWindow = await service.SignInAsync("fan@queenzone.org", "nope-3");
        Assert.False(afterWindow.Succeeded);
        var success = await service.SignInAsync("fan@queenzone.org", "S3curePass!");
        Assert.True(success.Succeeded);
        stored = await repository.FindByEmailAsync("fan@queenzone.org");
        Assert.Equal(0, stored!.PasswordFailureCount);
        Assert.Null(stored.PasswordFailureWindowStartedAt);
    }

    [Fact]
    public async Task SignInAsync_RewritesHash_WhenVerificationNeedsRehash()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        var legacyHasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
        {
            IterationCount = 1_000,
        }));
        var legacyHash = legacyHasher.HashPassword(registered.Account!, "S3curePass!");
        registered.Account!.PasswordHash = legacyHash;

        var result = await service.SignInAsync("fan@queenzone.org", "S3curePass!");

        Assert.True(result.Succeeded);
        var stored = await repository.FindByEmailAsync("fan@queenzone.org");
        Assert.NotEqual(legacyHash, stored!.PasswordHash);
        var current = new PasswordHasher<MemberAccount>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            current.VerifyHashedPassword(stored, stored.PasswordHash!, "S3curePass!"));
    }

    [Fact]
    public async Task FindOrCreateFromExternalLoginAsync_CreatesNewAccount_WithoutSilentLegacyLink()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>
        {
            ["fan@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
        });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);

        var account = await SignedInExternalAsync(service, "Google", "google-subject-1", "fan@queenzone.org", "Fan");

        Assert.Null(account.LinkedLegacyUserId);
        var state = await service.GetLegacyLinkStateAsync(account);
        Assert.Equal(LegacyAccountLinkKind.Claimable, state.Kind);
    }

    [Fact]
    public async Task SignInAsync_BackfillsLegacyLink_ForExistingUnlinkedAccount()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>
        {
            ["fan@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
        });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        var signedIn = await service.SignInAsync("fan@queenzone.org", "S3curePass!");

        Assert.True(signedIn.Succeeded);
        Assert.Equal(123, signedIn.Account!.LinkedLegacyUserId);
    }

    [Fact]
    public async Task ClaimLegacyAccountAsync_LinksAndOptionallyAdoptsDisplayName()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>
        {
            ["fan@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
        });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "New Fan");

        var claimed = await service.ClaimLegacyAccountAsync(
            registered.Account!.Id,
            legacyUserId: 123,
            adoptLegacyDisplayName: true);

        Assert.True(claimed.Succeeded);
        Assert.Equal(123, claimed.Account!.LinkedLegacyUserId);
        Assert.Equal("OldFan", claimed.Account.DisplayName);

        var state = await service.GetLegacyLinkStateAsync(claimed.Account);
        Assert.Equal(LegacyAccountLinkKind.Linked, state.Kind);
        Assert.Equal("OldFan", state.Match!.Username);
    }

    [Fact]
    public async Task ClaimLegacyAccountAsync_Fails_WhenLegacyIdAlreadyTaken()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>
        {
            ["first@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
            ["second@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
        });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var first = await service.RegisterAsync("first@queenzone.org", "S3curePass!", "First");
        Assert.True((await service.ClaimLegacyAccountAsync(
            first.Account!.Id,
            legacyUserId: 123,
            adoptLegacyDisplayName: false)).Succeeded);

        var second = await service.RegisterAsync("second@queenzone.org", "S3curePass!", "Second");
        var conflict = await service.ClaimLegacyAccountAsync(
            second.Account!.Id,
            legacyUserId: 123,
            adoptLegacyDisplayName: false);

        Assert.False(conflict.Succeeded);
        Assert.Contains("already linked", conflict.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlinkLegacyAccountAsync_ClearsLink_AndAllowsReclaim()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(
            new Dictionary<string, IReadOnlyList<LegacyMemberMatch>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fan@queenzone.org"] =
                [
                    new LegacyMemberMatch(100, "WrongFan"),
                    new LegacyMemberMatch(200, "RightFan"),
                ],
            });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "New Fan");
        Assert.True((await service.ClaimLegacyAccountAsync(
            registered.Account!.Id,
            legacyUserId: 100,
            adoptLegacyDisplayName: false)).Succeeded);

        var unlinked = await service.UnlinkLegacyAccountAsync(registered.Account.Id);

        Assert.True(unlinked.Succeeded);
        Assert.Null(unlinked.Account!.LinkedLegacyUserId);

        var state = await service.GetLegacyLinkStateAsync(unlinked.Account);
        Assert.Equal(LegacyAccountLinkKind.Claimable, state.Kind);
        Assert.True(state.HasMultipleClaimable);

        var reclaimed = await service.ClaimLegacyAccountAsync(
            registered.Account.Id,
            legacyUserId: 200,
            adoptLegacyDisplayName: true);

        Assert.True(reclaimed.Succeeded);
        Assert.Equal(200, reclaimed.Account!.LinkedLegacyUserId);
        Assert.Equal("RightFan", reclaimed.Account.DisplayName);
    }

    [Fact]
    public async Task UnlinkLegacyAccountAsync_IsNoOp_WhenAlreadyUnlinked()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        var result = await service.UnlinkLegacyAccountAsync(registered.Account!.Id);

        Assert.True(result.Succeeded);
        Assert.Null(result.Account!.LinkedLegacyUserId);
    }

    [Fact]
    public async Task UnlinkLegacyAccountAsync_Fails_WhenAccountDoesNotExist()
    {
        var service = CreateService();

        var result = await service.UnlinkLegacyAccountAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("Account not found.", result.Error);
    }

    [Fact]
    public async Task GetLegacyLinkStateAsync_ReturnsMultipleClaimable_WhenEmailMatchesSeveralFreeAccounts()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(
            new Dictionary<string, IReadOnlyList<LegacyMemberMatch>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fan@queenzone.org"] =
                [
                    new LegacyMemberMatch(200, "BetaFan"),
                    new LegacyMemberMatch(100, "AlphaFan"),
                ],
            });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "New Fan");

        var state = await service.GetLegacyLinkStateAsync(registered.Account!);

        Assert.Equal(LegacyAccountLinkKind.Claimable, state.Kind);
        Assert.True(state.HasMultipleClaimable);
        Assert.Null(state.Match);
        Assert.Equal([100, 200], state.ClaimableMatches.Select(m => m.UserId).ToArray());
        Assert.Equal(["AlphaFan", "BetaFan"], state.ClaimableMatches.Select(m => m.Username).ToArray());
    }

    [Fact]
    public async Task ClaimLegacyAccountAsync_ClaimsChosenMatch_WhenMultipleShareEmail()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(
            new Dictionary<string, IReadOnlyList<LegacyMemberMatch>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fan@queenzone.org"] =
                [
                    new LegacyMemberMatch(100, "AlphaFan"),
                    new LegacyMemberMatch(200, "BetaFan"),
                ],
            });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "New Fan");

        var claimed = await service.ClaimLegacyAccountAsync(
            registered.Account!.Id,
            legacyUserId: 200,
            adoptLegacyDisplayName: true);

        Assert.True(claimed.Succeeded);
        Assert.Equal(200, claimed.Account!.LinkedLegacyUserId);
        Assert.Equal("BetaFan", claimed.Account.DisplayName);
    }

    [Fact]
    public async Task ClaimLegacyAccountAsync_Fails_WhenChosenIdDoesNotMatchEmail()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(
            new Dictionary<string, IReadOnlyList<LegacyMemberMatch>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fan@queenzone.org"] = [new LegacyMemberMatch(100, "AlphaFan")],
                ["other@queenzone.org"] = [new LegacyMemberMatch(999, "OtherFan")],
            });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "New Fan");

        var result = await service.ClaimLegacyAccountAsync(
            registered.Account!.Id,
            legacyUserId: 999,
            adoptLegacyDisplayName: false);

        Assert.False(result.Succeeded);
        Assert.Contains("does not match", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await service.FindByIdAsync(registered.Account.Id))!.LinkedLegacyUserId);
    }

    [Fact]
    public async Task SignInAsync_DoesNotBackfill_WhenMultipleFreeLegacyMatches()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(
            new Dictionary<string, IReadOnlyList<LegacyMemberMatch>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fan@queenzone.org"] =
                [
                    new LegacyMemberMatch(100, "AlphaFan"),
                    new LegacyMemberMatch(200, "BetaFan"),
                ],
            });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");

        var signedIn = await service.SignInAsync("fan@queenzone.org", "S3curePass!");

        Assert.True(signedIn.Succeeded);
        Assert.Null(signedIn.Account!.LinkedLegacyUserId);

        var state = await service.GetLegacyLinkStateAsync(signedIn.Account);
        Assert.Equal(LegacyAccountLinkKind.Claimable, state.Kind);
        Assert.True(state.HasMultipleClaimable);
    }

    [Fact]
    public async Task GetLegacyLinkStateAsync_SplitsFreeAndTakenMatches()
    {
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(
            new Dictionary<string, IReadOnlyList<LegacyMemberMatch>>(StringComparer.OrdinalIgnoreCase)
            {
                ["shared@queenzone.org"] =
                [
                    new LegacyMemberMatch(100, "FreeFan"),
                    new LegacyMemberMatch(200, "TakenFan"),
                ],
                ["other@queenzone.org"] = [new LegacyMemberMatch(200, "TakenFan")],
            });
        var service = CreateService(legacyMemberLookupRepository: legacyLookup);
        var other = await service.RegisterAsync("other@queenzone.org", "S3curePass!", "Other");
        Assert.True((await service.ClaimLegacyAccountAsync(
            other.Account!.Id,
            legacyUserId: 200,
            adoptLegacyDisplayName: false)).Succeeded);

        var shared = await service.RegisterAsync("shared@queenzone.org", "S3curePass!", "Shared");
        var state = await service.GetLegacyLinkStateAsync(shared.Account!);

        Assert.Equal(LegacyAccountLinkKind.Claimable, state.Kind);
        Assert.False(state.HasMultipleClaimable);
        Assert.Single(state.ClaimableMatches);
        Assert.Equal(100, state.ClaimableMatches[0].UserId);
        Assert.Single(state.UnavailableMatches);
        Assert.Equal(200, state.UnavailableMatches[0].UserId);
    }

    [Fact]
    public async Task FindOrCreateFromExternalLoginAsync_ReturnsSameAccount_OnSecondLoginWithSameProviderKey()
    {
        var service = CreateService();

        var first = await SignedInExternalAsync(service, "Google", "google-subject-1", "fan@queenzone.org", "Fan");
        var second = await SignedInExternalAsync(service, "Google", "google-subject-1", "fan@queenzone.org", "Fan", emailVerified: false);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task FindOrCreateFromExternalLoginAsync_DoesNotLinkVerifiedEmail_UntilTheAccountConfirms()
    {
        var repository = new InMemoryMemberAccountRepository();
        var (accounts, counter) = CountingMemberAccountRepository.Create(repository);
        var service = CreateService(memberAccountRepository: accounts);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        var addsBefore = counter.AddExternalLoginCalls;

        var resolution = await service.FindOrCreateFromExternalLoginAsync(
            "Google",
            "google-subject-1",
            "fan@queenzone.org",
            "Fan",
            emailVerified: true);

        Assert.Equal(ExternalLoginStatus.LinkConfirmationRequired, resolution.Status);
        Assert.Equal(registered.Account!.Id, resolution.Account!.Id);
        Assert.Equal(addsBefore, counter.AddExternalLoginCalls);
        Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-subject-1"));
        Assert.Empty(await service.ListExternalProvidersAsync(registered.Account.Id));
    }

    [Fact]
    public async Task FindOrCreateFromExternalLoginAsync_DoesNotCreateOrLink_WhenEmailIsUnverified()
    {
        var repository = new InMemoryMemberAccountRepository();
        var (accounts, counter) = CountingMemberAccountRepository.Create(repository);
        var service = CreateService(memberAccountRepository: accounts);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        var addsBefore = counter.AddExternalLoginCalls;

        var matched = await service.FindOrCreateFromExternalLoginAsync(
            "Google",
            "google-unverified-match",
            "fan@queenzone.org",
            "Fan",
            emailVerified: false);
        var created = await service.FindOrCreateFromExternalLoginAsync(
            "Discord",
            "discord-unverified-new",
            "new-fan@queenzone.org",
            "New Fan",
            emailVerified: false);

        Assert.Equal(ExternalLoginStatus.UnverifiedEmail, matched.Status);
        Assert.Null(matched.Account);
        Assert.Equal(ExternalLoginStatus.UnverifiedEmail, created.Status);
        Assert.Equal(addsBefore, counter.AddExternalLoginCalls);
        Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-unverified-match"));
        Assert.Null(await repository.FindByEmailAsync("new-fan@queenzone.org"));
        Assert.Empty(await service.ListExternalProvidersAsync(registered.Account!.Id));
    }

    [Fact]
    public async Task LinkExternalLoginAsync_LinksOnlyAfterTheMatchingAccountConfirms()
    {
        var repository = new InMemoryMemberAccountRepository();
        var (accounts, counter) = CountingMemberAccountRepository.Create(repository);
        var legacyLookup = new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>
        {
            ["fan@queenzone.org"] = new LegacyMemberMatch(123, "OldFan"),
        });
        var service = CreateService(memberAccountRepository: accounts, legacyMemberLookupRepository: legacyLookup);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        var pending = await service.FindOrCreateFromExternalLoginAsync(
            "Google",
            "google-confirm-1",
            "fan@queenzone.org",
            "Fan",
            emailVerified: true);
        Assert.Equal(ExternalLoginStatus.LinkConfirmationRequired, pending.Status);
        Assert.Equal(0, counter.AddExternalLoginCalls);

        var wrongMember = await service.RegisterAsync("other@queenzone.org", "S3curePass!", "Other");
        var mismatch = await service.LinkExternalLoginAsync(
            wrongMember.Account!.Id,
            "Google",
            "google-confirm-1",
            "fan@queenzone.org");
        Assert.False(mismatch.Succeeded);
        Assert.Equal(ExternalLoginMessages.EmailMismatch, mismatch.Error);
        Assert.Equal(0, counter.AddExternalLoginCalls);

        var linked = await service.LinkExternalLoginAsync(
            registered.Account!.Id,
            "Google",
            "google-confirm-1",
            "fan@queenzone.org");

        Assert.True(linked.Succeeded);
        Assert.Equal(registered.Account.Id, linked.Account!.Id);
        Assert.Equal(1, counter.AddExternalLoginCalls);
        Assert.Equal(123, linked.Account.LinkedLegacyUserId);
        Assert.Equal(["Google"], await service.ListExternalProvidersAsync(registered.Account.Id));
    }

    [Fact]
    public async Task LinkExternalLoginAsync_RejectsUnknownProviderMissingAccountAndTakenSubject()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var owner = await service.RegisterAsync("owner@queenzone.org", "S3curePass!", "Owner");
        await service.LinkExternalLoginAsync(owner.Account!.Id, "Google", "taken-subject", "owner@queenzone.org");
        var other = await service.RegisterAsync("other-taken@queenzone.org", "S3curePass!", "Other");

        var unknown = await service.LinkExternalLoginAsync(other.Account!.Id, "Facebook", "fb-1", "other-taken@queenzone.org");
        var blank = await service.LinkExternalLoginAsync(other.Account.Id, "Google", " ", "other-taken@queenzone.org");
        var missing = await service.LinkExternalLoginAsync(Guid.NewGuid(), "Google", "new-subject", "other-taken@queenzone.org");
        var taken = await service.LinkExternalLoginAsync(other.Account.Id, "Google", "taken-subject", "other-taken@queenzone.org");
        await repository.SuspendAsync(other.Account.Id, "Spamming the board", "admin@queenzone.org", DateTime.UtcNow);
        var suspended = await service.LinkExternalLoginAsync(other.Account.Id, "GitHub", "gh-suspended", "other-taken@queenzone.org");

        Assert.Equal(ExternalLoginMessages.UnknownProvider, unknown.Error);
        Assert.Equal(ExternalLoginMessages.UnverifiedEmail, blank.Error);
        Assert.Equal("Account not found.", missing.Error);
        Assert.Equal(ExternalLoginMessages.AlreadyLinked, taken.Error);
        Assert.Equal(MemberAccountService.SuspendedSignInError, suspended.Error);
        Assert.Equal(["Google"], await service.ListExternalProvidersAsync(owner.Account.Id));
        Assert.Empty(await service.ListExternalProvidersAsync(other.Account.Id));
    }

    [Fact]
    public async Task FindOrCreateFromExternalLoginAsync_DoesNotLinkSuspendedAccount_ByEmail()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Fan");
        await repository.SuspendAsync(registered.Account!.Id, "Spamming the board", "admin@queenzone.org", DateTime.UtcNow);

        var resolution = await service.FindOrCreateFromExternalLoginAsync(
            "Google",
            "google-suspended-email",
            "fan@queenzone.org",
            "Fan",
            emailVerified: true);

        Assert.Equal(ExternalLoginStatus.Suspended, resolution.Status);
        Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-suspended-email"));
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_PersistsNewName()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Original Name");

        var result = await service.UpdateDisplayNameAsync(registered.Account!.Id, "  New Name  ");

        Assert.True(result.Succeeded);
        Assert.Equal("New Name", result.Account!.DisplayName);

        var reloaded = await service.FindByIdAsync(registered.Account.Id);
        Assert.Equal("New Name", reloaded!.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDisplayNameAsync_RejectsEmptyOrWhitespace(string? displayName)
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Original Name");

        var result = await service.UpdateDisplayNameAsync(registered.Account!.Id, displayName!);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Null(result.Account);

        var reloaded = await service.FindByIdAsync(registered.Account.Id);
        Assert.Equal("Original Name", reloaded!.DisplayName);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_RejectsTooShortName()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Original Name");

        var result = await service.UpdateDisplayNameAsync(registered.Account!.Id, "A");

        Assert.False(result.Succeeded);
        Assert.Contains("at least", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_RejectsTooLongName()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("fan@queenzone.org", "S3curePass!", "Original Name");
        var tooLong = new string('x', MemberAccountService.MaxDisplayNameLength + 1);

        var result = await service.UpdateDisplayNameAsync(registered.Account!.Id, tooLong);

        Assert.False(result.Succeeded);
        Assert.Contains("at most", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_AllowsDuplicateDisplayNames()
    {
        var service = CreateService();
        await service.RegisterAsync("fan1@queenzone.org", "S3curePass!", "Shared Name");
        var second = await service.RegisterAsync("fan2@queenzone.org", "S3curePass!", "Other Name");

        var result = await service.UpdateDisplayNameAsync(second.Account!.Id, "Shared Name");

        Assert.True(result.Succeeded);
        Assert.Equal("Shared Name", result.Account!.DisplayName);
    }

    [Fact]
    public async Task UpdateDisplayNameAsync_Fails_WhenAccountDoesNotExist()
    {
        var service = CreateService();

        var result = await service.UpdateDisplayNameAsync(Guid.NewGuid(), "Nobody");

        Assert.False(result.Succeeded);
        Assert.Equal("Account not found.", result.Error);
        Assert.Null(result.Account);
    }

    [Fact]
    public async Task UpdateMessagePrivacyAsync_PersistsValidSetting()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("privacy@queenzone.org", "S3curePass!", "Privacy Fan");

        var result = await service.UpdateMessagePrivacyAsync(
            registered.Account!.Id,
            MemberMessagePrivacy.Followed);

        Assert.True(result.Succeeded);
        Assert.Equal(MemberMessagePrivacy.Followed, result.Account!.MessagePrivacy);
        var reloaded = await service.FindByIdAsync(registered.Account.Id);
        Assert.Equal(MemberMessagePrivacy.Followed, reloaded!.MessagePrivacy);
    }

    [Fact]
    public async Task UpdateMessagePrivacyAsync_RejectsUndefinedValue()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("privacy-bad@queenzone.org", "S3curePass!", "Privacy Fan");

        var result = await service.UpdateMessagePrivacyAsync(
            registered.Account!.Id,
            (MemberMessagePrivacy)99);

        Assert.False(result.Succeeded);
        Assert.Contains("valid", result.Error, StringComparison.OrdinalIgnoreCase);
        var reloaded = await service.FindByIdAsync(registered.Account.Id);
        Assert.Equal(MemberMessagePrivacy.Members, reloaded!.MessagePrivacy);
    }

    [Fact]
    public async Task UpdateMessagePrivacyAsync_RejectsPendingDeletion()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("privacy-delete@queenzone.org", "S3curePass!", "Privacy Fan");
        await service.RequestDeletionAsync(registered.Account!.Id);

        var result = await service.UpdateMessagePrivacyAsync(
            registered.Account.Id,
            MemberMessagePrivacy.Nobody);

        Assert.False(result.Succeeded);
        Assert.Equal(MemberAccountService.PendingDeletionEditError, result.Error);
    }

    [Fact]
    public async Task ListExternalProvidersAsync_ReturnsLinkedProviders()
    {
        var service = CreateService();
        var account = await SignedInExternalAsync(
            service, "Google", "google-providers-1", "providers@example.com", "Provider Fan");
        var linked = await service.LinkExternalLoginAsync(
            account.Id, "GitHub", "github-providers-1", "providers@example.com");
        Assert.True(linked.Succeeded);

        var providers = await service.ListExternalProvidersAsync(account.Id);

        Assert.Equal(["GitHub", "Google"], providers);
    }

    [Fact]
    public async Task ListExternalProvidersAsync_ReturnsEmpty_WhenNoneLinked()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync("native@example.com", "S3curePass!", "Native Fan");

        var providers = await service.ListExternalProvidersAsync(registered.Account!.Id);

        Assert.Empty(providers);
    }

    [Fact]
    public async Task UpdateAvatarAsync_PersistsBlobPath_AndStoresFullAndThumb()
    {
        var backend = new InMemoryBlobStorageBackend();
        var service = CreateService(blobBackend: backend);
        var registered = await service.RegisterAsync("avatar@example.com", "S3curePass!", "Avatar Fan");
        await using var png = await CreatePngAsync(120, 80);

        var result = await service.UpdateAvatarAsync(registered.Account!.Id, png, "photo.png");

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Account!.AvatarUrl));
        Assert.StartsWith($"members/{registered.Account.Id:N}/avatar-", result.Account.AvatarUrl);
        Assert.EndsWith(".webp", result.Account.AvatarUrl);
        Assert.True(backend.Exists(MemberAvatarPaths.Container, result.Account.AvatarUrl!));
        Assert.True(backend.Exists(
            MemberAvatarPaths.Container,
            MemberAvatarPaths.ToThumbBlobName(result.Account.AvatarUrl!)));
    }

    [Fact]
    public async Task UpdateSocialLinksAsync_FieldError_DoesNotPersistAnyChannel()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("social-service@example.com", "S3curePass!", "Social Service");

        var saved = await service.UpdateSocialLinksAsync(
            registered.Account!.Id,
            new Dictionary<MemberSocialChannel, string?>
            {
                [MemberSocialChannel.X] = "@queen",
                [MemberSocialChannel.Instagram] = "queenofficial",
            });
        Assert.True(saved.Succeeded);

        var rejected = await service.UpdateSocialLinksAsync(
            registered.Account.Id,
            new Dictionary<MemberSocialChannel, string?>
            {
                [MemberSocialChannel.X] = "@queen",
                [MemberSocialChannel.Instagram] = "queenofficial",
                [MemberSocialChannel.YouTube] = "javascript:alert(1)",
            });

        Assert.False(rejected.Succeeded);
        Assert.Equal(
            [new MemberSocialLinkFieldError(MemberSocialChannel.YouTube, MemberSocialLinkUrl.InvalidValueMessage)],
            rejected.FieldErrors);
        Assert.Equal(
            [
                new MemberSocialLink(MemberSocialChannel.X, "https://x.com/queen"),
                new MemberSocialLink(MemberSocialChannel.Instagram, "https://www.instagram.com/queenofficial"),
            ],
            await repository.ListSocialLinksAsync(registered.Account.Id));
    }

    [Fact]
    public async Task UpdateSocialLinksAsync_PendingDeletion_RejectsAndKeepsRowsUntilAnonymise()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("social-delete@example.com", "S3curePass!", "Social Delete");
        await service.UpdateSocialLinksAsync(
            registered.Account!.Id,
            new Dictionary<MemberSocialChannel, string?>
            {
                [MemberSocialChannel.X] = "queen",
            });

        var deleted = await service.RequestDeletionAsync(registered.Account.Id);
        var update = await service.UpdateSocialLinksAsync(
            registered.Account.Id,
            new Dictionary<MemberSocialChannel, string?>
            {
                [MemberSocialChannel.X] = "stillhere",
            });

        Assert.True(deleted.Succeeded);
        Assert.False(update.Succeeded);
        Assert.Equal(MemberAccountService.PendingDeletionEditError, update.Error);
        Assert.Empty(await repository.ListSocialLinksAsync(registered.Account.Id));
    }

    [Fact]
    public async Task RequestDeletionAsync_AnonymisesImmediately_AndCancellationRestoresIdentity()
    {
        var backend = new InMemoryBlobStorageBackend();
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository, blobBackend: backend);
        var registered = await service.RegisterAsync(
            "delete-service@example.com",
            "S3curePass!",
            "Delete Service");
        await using var png = await CreatePngAsync();
        var uploaded = await service.UpdateAvatarAsync(registered.Account!.Id, png, "avatar.png");
        var avatarPath = uploaded.Account!.AvatarUrl!;
        var thumbPath = MemberAvatarPaths.ToThumbBlobName(avatarPath);

        var result = await service.RequestDeletionAsync(registered.Account.Id);
        var signIn = await service.SignInAsync("delete-service@example.com", "S3curePass!");
        var rename = await service.UpdateDisplayNameAsync(registered.Account.Id, "Visible Again");

        Assert.True(result.Succeeded);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, result.Account!.DisplayName);
        Assert.Equal("Delete Service", result.Account.DeletionRecoveryDisplayName);
        Assert.Equal(avatarPath, result.Account.DeletionRecoveryAvatarUrl);
        Assert.Null(result.Account.AvatarUrl);
        Assert.NotNull(result.Account.DeletionRequestedAt);
        Assert.False(result.Account.IsSuspended);
        Assert.True(backend.Exists(MemberAvatarPaths.Container, avatarPath));
        Assert.True(backend.Exists(MemberAvatarPaths.Container, thumbPath));
        Assert.True(signIn.Succeeded);
        Assert.False(rename.Succeeded);
        Assert.Equal(MemberAccountService.PendingDeletionEditError, rename.Error);

        var cancelled = await service.CancelDeletionAsync(registered.Account.Id);

        Assert.True(cancelled.Succeeded);
        Assert.Null(cancelled.Account!.DeletionRequestedAt);
        Assert.Equal("Delete Service", cancelled.Account.DisplayName);
        Assert.Equal(avatarPath, cancelled.Account.AvatarUrl);
        Assert.Null(cancelled.Account.DeletionRecoveryDisplayName);
        Assert.Null(cancelled.Account.DeletionRecoveryAvatarUrl);
    }

    [Fact]
    public async Task PendingDeletion_ExistingExternalSignInRemainsAvailable_WithoutAddingProvider()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var account = await SignedInExternalAsync(
            service,
            "Google",
            "deleted-google-subject",
            "deleted-external@example.com",
            "External Delete");
        await service.RequestDeletionAsync(account.Id);

        var returned = await service.FindOrCreateFromExternalLoginAsync(
            "Google",
            "deleted-google-subject",
            "deleted-external@example.com",
            "External Delete",
            emailVerified: false);
        var newProvider = await service.FindOrCreateFromExternalLoginAsync(
            "GitHub",
            "deleted-github-subject",
            "deleted-external@example.com",
            "External Delete",
            emailVerified: true);

        Assert.Equal(ExternalLoginStatus.SignedIn, returned.Status);
        Assert.Equal(account.Id, returned.Account!.Id);
        Assert.Equal(MemberAccountDeletionPolicy.DeletedDisplayName, returned.Account.DisplayName);
        Assert.NotNull(returned.Account.LastLoginAt);
        Assert.Equal(ExternalLoginStatus.LinkConfirmationRequired, newProvider.Status);
        Assert.Equal(["Google"], await service.ListExternalProvidersAsync(account.Id));

        var blocked = await service.LinkExternalLoginAsync(
            account.Id,
            "GitHub",
            "deleted-github-subject",
            "deleted-external@example.com");
        Assert.False(blocked.Succeeded);
        Assert.Equal(MemberAccountService.PendingDeletionEditError, blocked.Error);
        Assert.Equal(["Google"], await service.ListExternalProvidersAsync(account.Id));
    }

    [Fact]
    public async Task UpdateAvatarAsync_RejectsOversizedFile_BeforeUpload()
    {
        var backend = new InMemoryBlobStorageBackend();
        var service = CreateService(blobBackend: backend);
        var registered = await service.RegisterAsync("big@example.com", "S3curePass!", "Big Fan");
        // Oversized payload that is not a valid image — rejected on size before blob upload.
        await using var oversized = new MemoryStream(new byte[MemberAvatarPaths.MaxUploadBytes + 1]);

        var result = await service.UpdateAvatarAsync(registered.Account!.Id, oversized, "huge.png");

        Assert.False(result.Succeeded);
        Assert.Contains("MB", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Account);
        var reloaded = await service.FindByIdAsync(registered.Account.Id);
        Assert.Null(reloaded!.AvatarUrl);
        Assert.Null(backend.TryGet(MemberAvatarPaths.Container, "members/any"));
    }

    [Fact]
    public async Task UpdateAvatarAsync_RejectsNonImageFile_BeforeUpload()
    {
        var backend = new InMemoryBlobStorageBackend();
        var service = CreateService(blobBackend: backend);
        var registered = await service.RegisterAsync("txt@example.com", "S3curePass!", "Text Fan");
        await using var notImage = new MemoryStream("not-an-image"u8.ToArray());

        var result = await service.UpdateAvatarAsync(registered.Account!.Id, notImage, "notes.txt");

        Assert.False(result.Succeeded);
        Assert.Contains("JPEG", result.Error, StringComparison.OrdinalIgnoreCase);
        var reloaded = await service.FindByIdAsync(registered.Account.Id);
        Assert.Null(reloaded!.AvatarUrl);
    }

    [Fact]
    public async Task DeleteImmediatelyAsync_ReturnsSuccess_WhenPurgeCompletes()
    {
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("immediate-ok@example.com", "S3curePass!", "Immediate Ok");

        var result = await service.DeleteImmediatelyAsync(registered.Account!.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Account!.PersonalDataPurgedAt);
        Assert.NotNull((await repository.FindByIdAsync(registered.Account.Id))!.PersonalDataPurgedAt);
    }

    [Fact]
    public async Task DeleteImmediatelyAsync_EmailsOriginalAddressAfterPurge()
    {
        var repository = new InMemoryMemberAccountRepository();
        var sender = new RecordingEmailSender();
        var service = CreateService(memberAccountRepository: repository, emailSender: sender);
        var registered = await service.RegisterAsync("delete-mail@example.com", "S3curePass!", "Delete Mail");

        var result = await service.DeleteImmediatelyAsync(registered.Account!.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("delete-mail@example.com", sender.To);
    }

    private sealed class RecordingEmailSender : IOutboundEmailSender
    {
        public string? To { get; private set; }

        public Task SendAsync(string to, string subject, string body, string? replyTo = null,
            CancellationToken cancellationToken = default)
        {
            To = to;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DeleteImmediatelyAsync_ReturnsFailure_WhenPurgeDoesNotTombstone()
    {
        var inner = new InMemoryMemberAccountRepository();
        var (repository, _) = ThrowingPurgeMemberAccountRepository.Create(inner);
        var service = CreateService(memberAccountRepository: repository);
        var registered = await service.RegisterAsync("immediate-fail@example.com", "S3curePass!", "Immediate Fail");

        var result = await service.DeleteImmediatelyAsync(registered.Account!.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(MemberAccountService.ImmediatePurgeIncompleteError, result.Error);
        Assert.Null((await inner.FindByIdAsync(registered.Account.Id))!.PersonalDataPurgedAt);
    }

    [Fact]
    public async Task DeleteImmediatelyAsync_ReturnsSuccess_WhenBlobDeleteThrowsAfterPurge()
    {
        var repository = new InMemoryMemberAccountRepository();
        var hanging = new HangForeverBlobUploadService();
        var service = CreateService(
            memberAccountRepository: repository,
            blobUploadService: hanging,
            blobDeleteTimeout: TimeSpan.FromMilliseconds(50));
        var registered = await service.RegisterAsync("immediate-blob@example.com", "S3curePass!", "Immediate Blob");
        await repository.UpdateAvatarUrlAsync(registered.Account!.Id, "members/hang/avatar.webp");

        var result = await service.DeleteImmediatelyAsync(registered.Account.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull((await repository.FindByIdAsync(registered.Account.Id))!.PersonalDataPurgedAt);
        Assert.False((await repository.GetDeletionProgressAsync(registered.Account.Id))!.IsComplete);
        Assert.True(hanging.DeleteCalls >= 1);
    }

    [Fact]
    public async Task PurgeDueDeletionsAsync_DeletesAvatarBlobs_WhenRetentionHasElapsed()
    {
        var backend = new InMemoryBlobStorageBackend();
        var repository = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: repository, blobBackend: backend);
        var registered = await service.RegisterAsync("purge-ok@example.com", "S3curePass!", "Purge Fan");
        await using var png = await CreatePngAsync();
        var uploaded = await service.UpdateAvatarAsync(registered.Account!.Id, png, "avatar.png");
        var avatarPath = uploaded.Account!.AvatarUrl!;
        var thumbPath = MemberAvatarPaths.ToThumbBlobName(avatarPath);
        await service.RequestDeletionAsync(registered.Account.Id);

        var purged = await service.PurgeDueDeletionsAsync(
            DateTime.UtcNow.AddDays(MemberAccountDeletionPolicy.RetentionDays + 1));

        Assert.Equal(1, purged);
        Assert.False(backend.Exists(MemberAvatarPaths.Container, avatarPath));
        Assert.False(backend.Exists(MemberAvatarPaths.Container, thumbPath));
        var reloaded = await repository.FindByIdAsync(registered.Account.Id);
        Assert.NotNull(reloaded!.PersonalDataPurgedAt);
    }

    [Fact]
    public async Task PurgeDueDeletionsAsync_Completes_WhenBlobDeleteIgnoresCancellation()
    {
        var repository = new InMemoryMemberAccountRepository();
        var hanging = new HangForeverBlobUploadService();
        var service = CreateService(
            memberAccountRepository: repository,
            blobUploadService: hanging,
            blobDeleteTimeout: TimeSpan.FromMilliseconds(50));
        var registered = await service.RegisterAsync("purge-hang@example.com", "S3curePass!", "Hang Fan");
        await repository.UpdateAvatarUrlAsync(registered.Account!.Id, "members/hang/avatar.webp");
        await repository.RequestDeletionAsync(
            registered.Account.Id,
            DateTime.UtcNow.AddDays(-MemberAccountDeletionPolicy.RetentionDays - 1));

        var started = DateTime.UtcNow;
        var purged = await service.PurgeDueDeletionsAsync(DateTime.UtcNow);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(1, purged);
        Assert.True(hanging.DeleteCalls >= 1);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"Purge waited {elapsed} for a hung blob delete.");
    }

    [Fact]
    public async Task UpdateAvatarAsync_CleansUpNewBlobs_WhenDbSaveFails_AndKeepsOldAvatar()
    {
        var backend = new InMemoryBlobStorageBackend();
        var realRepo = new InMemoryMemberAccountRepository();
        var service = CreateService(memberAccountRepository: realRepo, blobBackend: backend);
        var registered = await service.RegisterAsync("keep@example.com", "S3curePass!", "Keep Fan");
        await using var first = await CreatePngAsync();
        var firstResult = await service.UpdateAvatarAsync(registered.Account!.Id, first, "one.png");
        Assert.True(firstResult.Succeeded);
        var oldPath = firstResult.Account!.AvatarUrl!;
        var oldThumb = MemberAvatarPaths.ToThumbBlobName(oldPath);
        Assert.True(backend.Exists(MemberAvatarPaths.Container, oldPath));

        var failingRepo = new FailingUpdateAvatarRepository(realRepo);
        var failingService = CreateService(memberAccountRepository: failingRepo, blobBackend: backend);
        await using var second = await CreatePngAsync(64, 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingService.UpdateAvatarAsync(registered.Account.Id, second, "two.png"));

        Assert.True(backend.Exists(MemberAvatarPaths.Container, oldPath));
        Assert.True(backend.Exists(MemberAvatarPaths.Container, oldThumb));
        var reloaded = await realRepo.FindByIdAsync(registered.Account.Id);
        Assert.Equal(oldPath, reloaded!.AvatarUrl);
    }

    private static async Task<MemberAccount> SignedInExternalAsync(
        MemberAccountService service,
        string provider,
        string providerKey,
        string email,
        string displayName,
        bool emailVerified = true)
    {
        var result = await service.FindOrCreateFromExternalLoginAsync(
            provider,
            providerKey,
            email,
            displayName,
            emailVerified);
        Assert.Equal(ExternalLoginStatus.SignedIn, result.Status);
        Assert.NotNull(result.Account);
        return result.Account;
    }

    /// <summary>
    /// Never completes <see cref="IBlobUploadService.DeleteAsync"/> and ignores the token,
    /// matching an Azure SDK hang on a dead TCP / retry loop.
    /// </summary>
    private sealed class HangForeverBlobUploadService : IBlobUploadService
    {
        public int DeleteCalls;

        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref DeleteCalls);
            return new TaskCompletionSource().Task;
        }

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlobContent?>(null);
    }

    /// <summary>
    /// Delegates reads to the inner repo but throws on avatar URL writes to simulate DB failure.
    /// </summary>
    private sealed class FailingUpdateAvatarRepository(IMemberAccountRepository inner) : IMemberAccountRepository
    {
        public Task<MemberAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            inner.FindByEmailAsync(email, cancellationToken);

        public Task<MemberAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.FindByIdAsync(id, cancellationToken);

        public Task<IReadOnlySet<Guid>> ListActiveMemberIdsAsync(
            IReadOnlyCollection<Guid> memberIds,
            CancellationToken cancellationToken = default) =>
            inner.ListActiveMemberIdsAsync(memberIds, cancellationToken);

        public Task<MemberAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken = default) =>
            inner.FindByExternalLoginAsync(provider, providerKey, cancellationToken);

        public Task<IReadOnlyList<string>> ListExternalProvidersAsync(Guid memberAccountId, CancellationToken cancellationToken = default) =>
            inner.ListExternalProvidersAsync(memberAccountId, cancellationToken);

        public Task<MemberAccount> CreateAsync(MemberAccount account, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(account, cancellationToken);

        public Task AddExternalLoginAsync(Guid memberAccountId, string provider, string providerKey, string email, CancellationToken cancellationToken = default) =>
            inner.AddExternalLoginAsync(memberAccountId, provider, providerKey, email, cancellationToken);

        public Task SaveAppleRefreshTokenAsync(Guid memberAccountId, string providerKey, string protectedToken, CancellationToken cancellationToken = default) =>
            inner.SaveAppleRefreshTokenAsync(memberAccountId, providerKey, protectedToken, cancellationToken);

        public Task<IReadOnlyList<PendingAppleRevocation>> ListPendingAppleRevocationsAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListPendingAppleRevocationsAsync(limit, cancellationToken);

        public Task CompleteAppleRevocationAsync(Guid externalLoginId, CancellationToken cancellationToken = default) =>
            inner.CompleteAppleRevocationAsync(externalLoginId, cancellationToken);

        public Task<MemberAccount?> UpdateDisplayNameAsync(Guid memberId, string displayName, CancellationToken cancellationToken = default) =>
            inner.UpdateDisplayNameAsync(memberId, displayName, cancellationToken);

        public Task<MemberAccount?> UpdateAvatarUrlAsync(Guid memberId, string? avatarBlobPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated database failure.");

        public Task<MemberAccount?> UpdateMessagePrivacyAsync(
            Guid memberId,
            MemberMessagePrivacy messagePrivacy,
            CancellationToken cancellationToken = default) =>
            inner.UpdateMessagePrivacyAsync(memberId, messagePrivacy, cancellationToken);

        public Task<MemberAccount?> FindByLinkedLegacyUserIdAsync(int legacyUserId, CancellationToken cancellationToken = default) =>
            inner.FindByLinkedLegacyUserIdAsync(legacyUserId, cancellationToken);

        public Task<MemberAccount?> LinkLegacyUserIdAsync(Guid memberId, int legacyUserId, CancellationToken cancellationToken = default) =>
            inner.LinkLegacyUserIdAsync(memberId, legacyUserId, cancellationToken);

        public Task<MemberAccount?> UnlinkLegacyUserIdAsync(Guid memberId, CancellationToken cancellationToken = default) =>
            inner.UnlinkLegacyUserIdAsync(memberId, cancellationToken);

        public Task RecordLoginAsync(Guid memberId, DateTime loginAt, CancellationToken cancellationToken = default) =>
            inner.RecordLoginAsync(memberId, loginAt, cancellationToken);

        public Task RecordPasswordFailureAsync(
            Guid memberId,
            int failureCount,
            DateTime windowStartedAt,
            CancellationToken cancellationToken = default) =>
            inner.RecordPasswordFailureAsync(memberId, failureCount, windowStartedAt, cancellationToken);

        public Task RecordPasswordSignInAsync(
            Guid memberId,
            DateTime loginAt,
            string? rehashedPassword,
            CancellationToken cancellationToken = default) =>
            inner.RecordPasswordSignInAsync(memberId, loginAt, rehashedPassword, cancellationToken);

        public Task<MemberStats> GetStatsAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
            inner.GetStatsAsync(utcNow, cancellationToken);

        public Task<IReadOnlyList<RecentLogin>> GetRecentLoginsAsync(int count, CancellationToken cancellationToken = default) =>
            inner.GetRecentLoginsAsync(count, cancellationToken);

        public Task<IReadOnlyList<DailyRegistration>> GetDailyRegistrationsAsync(DateOnly fromDate, CancellationToken cancellationToken = default) =>
            inner.GetDailyRegistrationsAsync(fromDate, cancellationToken);

        public Task<IReadOnlyList<MemberRecipientMatch>> SearchByDisplayNameAsync(
            string query,
            Guid? excludeMemberId = null,
            int maxResults = PrivateMessageLimits.MaxRecipientSearchResults,
            CancellationToken cancellationToken = default) =>
            inner.SearchByDisplayNameAsync(query, excludeMemberId, maxResults, cancellationToken);

        public Task<MemberSearchResult> SearchMembersAsync(
            string? query,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchMembersAsync(query, pageNumber, pageSize, cancellationToken);

        public Task<IReadOnlyList<LocalPasswordAccountSummary>> ListLocalPasswordAccountsAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListLocalPasswordAccountsAsync(cancellationToken);

        public Task<MemberAccount?> UpdateLocalPasswordAccountAsync(
            Guid memberId,
            string email,
            string displayName,
            string? passwordHash,
            CancellationToken cancellationToken = default) =>
            inner.UpdateLocalPasswordAccountAsync(memberId, email, displayName, passwordHash, cancellationToken);

        public Task<bool> RemoveLocalPasswordAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            inner.RemoveLocalPasswordAsync(memberId, cancellationToken);

        public Task<MemberAccount?> SuspendAsync(
            Guid memberId,
            string reason,
            string suspendedByAdminEmail,
            DateTime suspendedAt,
            CancellationToken cancellationToken = default) =>
            inner.SuspendAsync(memberId, reason, suspendedByAdminEmail, suspendedAt, cancellationToken);

        public Task<MemberAccount?> ReinstateAsync(Guid memberId, CancellationToken cancellationToken = default) =>
            inner.ReinstateAsync(memberId, cancellationToken);

        public Task<MemberAccountDeletionRequestResult?> RequestDeletionAsync(
            Guid memberId,
            DateTime requestedAt,
            CancellationToken cancellationToken = default,
            bool immediate = false) =>
            inner.RequestDeletionAsync(memberId, requestedAt, cancellationToken, immediate);

        public Task<MemberAccount?> CancelDeletionAsync(
            Guid memberId,
            DateTime cancelledAt,
            CancellationToken cancellationToken = default) =>
            inner.CancelDeletionAsync(memberId, cancelledAt, cancellationToken);

        public Task<MemberAccountDeletionPurgeResult> PurgeDeletedAccountsAsync(
            DateTime purgeBefore,
            DateTime purgedAt,
            CancellationToken cancellationToken = default) =>
            inner.PurgeDeletedAccountsAsync(purgeBefore, purgedAt, cancellationToken);

        public Task<IReadOnlyList<PendingMemberDeletionBlob>> ListPendingDeletionBlobsAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListPendingDeletionBlobsAsync(limit, cancellationToken);

        public Task CompleteDeletionBlobAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.CompleteDeletionBlobAsync(id, cancellationToken);

        public Task<MemberDeletionProgress?> GetDeletionProgressAsync(Guid memberId, CancellationToken cancellationToken = default) =>
            inner.GetDeletionProgressAsync(memberId, cancellationToken);

        public Task<IReadOnlyList<MemberSocialLink>> ListSocialLinksAsync(
            Guid memberId,
            CancellationToken cancellationToken = default) =>
            inner.ListSocialLinksAsync(memberId, cancellationToken);

        public Task ReplaceSocialLinksAsync(
            Guid memberId,
            IReadOnlyList<MemberSocialLink> links,
            CancellationToken cancellationToken = default) =>
            inner.ReplaceSocialLinksAsync(memberId, links, cancellationToken);
    }
}

/// <summary>
/// Delegates to an inner repository but throws from <see cref="IMemberAccountRepository.PurgeDeletedAccountsAsync"/>.
/// </summary>
public class ThrowingPurgeMemberAccountRepository : DispatchProxy
{
    private IMemberAccountRepository inner = null!;

    public static (IMemberAccountRepository Proxy, ThrowingPurgeMemberAccountRepository Counter) Create(
        IMemberAccountRepository inner)
    {
        var proxy = DispatchProxy.Create<IMemberAccountRepository, ThrowingPurgeMemberAccountRepository>();
        var counter = (ThrowingPurgeMemberAccountRepository)(object)proxy;
        counter.inner = inner;
        return (proxy, counter);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IMemberAccountRepository.PurgeDeletedAccountsAsync))
        {
            throw new InvalidOperationException("Simulated purge failure.");
        }

        return targetMethod!.Invoke(inner, args);
    }
}

/// <summary>
/// Counts <see cref="IMemberAccountRepository.AddExternalLoginAsync"/> while delegating every call.
/// </summary>
public class CountingMemberAccountRepository : DispatchProxy
{
    private IMemberAccountRepository inner = null!;

    public int AddExternalLoginCalls { get; private set; }

    public static (IMemberAccountRepository Proxy, CountingMemberAccountRepository Counter) Create(
        IMemberAccountRepository inner)
    {
        var proxy = DispatchProxy.Create<IMemberAccountRepository, CountingMemberAccountRepository>();
        var counter = (CountingMemberAccountRepository)(object)proxy;
        counter.inner = inner;
        return (proxy, counter);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IMemberAccountRepository.AddExternalLoginAsync))
        {
            AddExternalLoginCalls++;
        }

        return targetMethod!.Invoke(inner, args);
    }
}
