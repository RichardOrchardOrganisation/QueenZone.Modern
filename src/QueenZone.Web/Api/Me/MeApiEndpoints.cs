using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Signed-in member account for the mobile app (issues #752 / #753 / #754).
/// Mirrors website <c>/account/settings</c> and <c>/account/delete</c> through
/// <see cref="MemberAccountService"/>. Requires
/// <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>.
/// </summary>
public static class MeApiEndpoints
{
    public const string Path = "/api/v1/me";

    public static void MapMeApiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/account-deletion-status", async (
                string? receipt,
                MemberDeletionReceiptService receipts,
                CancellationToken cancellationToken) =>
            {
                var progress = await receipts.GetProgressAsync(receipt, cancellationToken);
                return progress is null
                    ? Results.NotFound()
                    : Results.Ok(new DeletionProgressResponse(progress.IsComplete ? "complete" : "processing"));
            })
            .WithName("GetAccountDeletionStatus")
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Me")
            .WithSummary("Check an account deletion with its private status receipt after sign-out.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)
            .Produces<DeletionProgressResponse>()
            .Produces(StatusCodes.Status404NotFound);

        var group = app.MapGroup("/api/v1")
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Me")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery();

        group.MapGet("/me", GetMeAsync)
            .WithName("GetMemberProfile")
            .WithSummary("Current member profile, matching website account settings.")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPatch("/me", PatchMeAsync)
            .WithName("PatchMemberProfile")
            .WithSummary("Update display name and/or messaging privacy. Same validation as /account/settings.")
            .Accepts<MemberProfilePatchRequest>("application/json")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/me/avatar", UploadAvatarAsync)
            .WithName("UploadMemberAvatar")
            .WithSummary("Upload a new avatar from camera or photo library. JPEG, PNG, or WebP. Max 2 MB.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/me/avatar", RemoveAvatarAsync)
            .WithName("RemoveMemberAvatar")
            .WithSummary("Remove the current avatar.")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/me/legacy-link", ClaimLegacyAsync)
            .WithName("ClaimLegacyAccount")
            .WithSummary("Claim a classic forum account matching this email.")
            .Accepts<ClaimLegacyRequest>("application/json")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/me/legacy-link", UnlinkLegacyAsync)
            .WithName("UnlinkLegacyAccount")
            .WithSummary("Unlink the claimed classic forum account.")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/me/deletion-request", RequestDeletionAsync)
            .WithName("RequestAccountDeletion")
            .WithSummary("Delete an account after typing DELETE. Revokes mobile refresh tokens.")
            .Accepts<DeletionRequestBody>("application/json")
            .Produces<DeletionRequestedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/me/deletion-request/cancel", CancelDeletionAsync)
            .WithName("CancelAccountDeletion")
            .WithSummary("Cancel a scheduled account deletion during the cooling-off period.")
            .Produces<MemberProfileDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> GetMeAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadCurrentAsync(user, memberAccountService, cancellationToken);
        if (loaded.Failure is not null)
        {
            return loaded.Failure;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(loaded.Profile);
    }

    internal static async Task<IResult> PatchMeAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        MemberProfilePatchRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        if (request is null)
        {
            return BadRequest("A JSON body is required.");
        }

        var hasName = request.DisplayName is not null;
        var hasPrivacy = request.MessagePrivacy is not null;
        if (!hasName && !hasPrivacy)
        {
            return BadRequest("Provide a display name or messaging privacy value.");
        }

        if (hasName)
        {
            var renamed = await memberAccountService.UpdateDisplayNameAsync(
                memberId,
                request.DisplayName!,
                cancellationToken);
            if (!renamed.Succeeded)
            {
                return BadRequest(renamed.Error ?? "Could not update display name.");
            }
        }

        if (hasPrivacy)
        {
            var privacy = await memberAccountService.UpdateMessagePrivacyAsync(
                memberId,
                request.MessagePrivacy!.Value,
                cancellationToken);
            if (!privacy.Succeeded)
            {
                return BadRequest(privacy.Error ?? "Could not update messaging privacy.");
            }
        }

        return await OkProfileAsync(memberId, memberAccountService, cancellationToken);
    }

    internal static async Task<IResult> UploadAvatarAsync(
        ClaimsPrincipal user,
        HttpRequest request,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        if (!request.HasFormContentType)
        {
            return BadRequest("Choose an image file to upload.");
        }

        IFormFile? file = null;
        try
        {
            var form = await request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        }
        catch (InvalidDataException)
        {
            return BadRequest("Choose an image file to upload.");
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Choose an image file to upload.");
        }
        catch (BadHttpRequestException)
        {
            return BadRequest("Choose an image file to upload.");
        }

        if (file is null || file.Length <= 0)
        {
            return BadRequest("Choose an image file to upload.");
        }

        await using var stream = file.OpenReadStream();
        var result = await memberAccountService.UpdateAvatarAsync(
            memberId,
            stream,
            file.FileName,
            cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error ?? "Could not update avatar.");
        }

        return await OkProfileAsync(memberId, memberAccountService, cancellationToken);
    }

    internal static async Task<IResult> RemoveAvatarAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var result = await memberAccountService.RemoveAvatarAsync(memberId, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error ?? "Could not remove avatar.");
        }

        return await OkProfileAsync(memberId, memberAccountService, cancellationToken);
    }

    internal static async Task<IResult> ClaimLegacyAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        ClaimLegacyRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        if (request?.LegacyUserId is null)
        {
            return BadRequest("Choose which legacy forum account to claim.");
        }

        var result = await memberAccountService.ClaimLegacyAccountAsync(
            memberId,
            request.LegacyUserId.Value,
            request.AdoptDisplayName,
            cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error ?? "Could not claim the legacy account.");
        }

        return await OkProfileAsync(memberId, memberAccountService, cancellationToken);
    }

    internal static async Task<IResult> UnlinkLegacyAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var result = await memberAccountService.UnlinkLegacyAccountAsync(memberId, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error ?? "Could not unlink the legacy account.");
        }

        return await OkProfileAsync(memberId, memberAccountService, cancellationToken);
    }

    internal static async Task<IResult> RequestDeletionAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        IMobileAuthGrantRepository mobileAuthGrantRepository,
        MemberDeletionReceiptService deletionReceipts,
        DeletionRequestBody? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        if (!string.Equals(
                request?.Confirmation?.Trim(),
                AccountDeletionCopy.ConfirmationPhrase,
                StringComparison.Ordinal))
        {
            return BadRequest(AccountDeletionCopy.ConfirmationRequired);
        }

        var result = request?.Immediate == true
            ? await memberAccountService.DeleteImmediatelyAsync(memberId, cancellationToken)
            : await memberAccountService.RequestDeletionAsync(memberId, cancellationToken);
        if (!result.Succeeded || result.Account is null)
        {
            return BadRequest(result.Error ?? "Could not request account deletion.");
        }

        await mobileAuthGrantRepository.RevokeAllRefreshTokensForMemberAsync(
            memberId,
            DateTime.UtcNow,
            cancellationToken);

        var scheduled = result.Account.DeletionRequestedAt!.Value.AddDays(
            MemberAccountDeletionPolicy.RetentionDays);
        return Results.Ok(new DeletionRequestedResponse(
            Requested: true,
            ScheduledDeletionAt: request?.Immediate == true ? null : ToUtc(scheduled),
            request?.Immediate == true ? AccountDeletionCopy.ImmediateTitle : AccountDeletionCopy.RequestedTitle,
            request?.Immediate == true ? AccountDeletionCopy.ImmediateMessage : AccountDeletionCopy.RequestedMessage,
            request?.Immediate == true ? deletionReceipts.Issue(memberId) : null));
    }

    internal static async Task<IResult> CancelDeletionAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var result = await memberAccountService.CancelDeletionAsync(memberId, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error ?? "Could not cancel account deletion.");
        }

        return await OkProfileAsync(memberId, memberAccountService, cancellationToken);
    }

    internal static MemberProfileDto ToDto(
        Data.Entities.MemberAccount account,
        IReadOnlyList<string> linkedProviders,
        LegacyAccountLinkState legacyLink)
    {
        var hasAvatar = !string.IsNullOrWhiteSpace(account.AvatarUrl);
        DateTimeOffset? scheduled = account.DeletionRequestedAt is null
            ? null
            : ToUtc(account.DeletionRequestedAt.Value.AddDays(MemberAccountDeletionPolicy.RetentionDays));

        return new MemberProfileDto(
            account.Id,
            account.Email,
            account.DisplayName,
            ToUtc(account.CreatedAt),
            account.LastLoginAt is null ? null : ToUtc(account.LastLoginAt.Value),
            hasAvatar,
            hasAvatar ? MemberAvatarPaths.GetServePath(account.Id) : null,
            hasAvatar ? MemberAvatarPaths.GetServePath(account.Id, thumb: true) : null,
            account.MessagePrivacy,
            linkedProviders,
            ToLegacyDto(legacyLink),
            scheduled,
            new MemberProfileLimitsDto(
                MemberAccountService.MinDisplayNameLength,
                MemberAccountService.MaxDisplayNameLength,
                MemberAvatarPaths.MaxUploadBytes,
                [.. MemberAvatarPaths.AllowedContentTypes],
                MemberAccountDeletionPolicy.RetentionDays),
            new AccountDeletionInfoDto(
                AccountDeletionCopy.ConfirmationPhrase,
                AccountDeletionCopy.ConfirmationHint,
                AccountDeletionCopy.RequestedTitle,
                AccountDeletionCopy.RequestedMessage,
                AccountDeletionCopy.WhatHappens));
    }

    private static async Task<IResult> OkProfileAsync(
        Guid memberId,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var account = await memberAccountService.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "The access token is invalid or expired.");
        }

        var providers = await memberAccountService.ListExternalProvidersAsync(memberId, cancellationToken);
        var legacy = await memberAccountService.GetLegacyLinkStateAsync(account, cancellationToken);
        return Results.Ok(ToDto(account, providers, legacy));
    }

    private static async Task<(MemberProfileDto? Profile, IResult? Failure)> LoadCurrentAsync(
        ClaimsPrincipal user,
        MemberAccountService memberAccountService,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return (null, unauthorized);
        }

        var account = await memberAccountService.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return (null, Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "The access token is invalid or expired."));
        }

        var providers = await memberAccountService.ListExternalProvidersAsync(memberId, cancellationToken);
        var legacy = await memberAccountService.GetLegacyLinkStateAsync(account, cancellationToken);
        return (ToDto(account, providers, legacy), null);
    }

    private static Guid RequireMemberId(ClaimsPrincipal user, out IResult? failure)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            failure = Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
            return Guid.Empty;
        }

        failure = null;
        return memberId.Value;
    }

    private static LegacyLinkDto ToLegacyDto(LegacyAccountLinkState state) =>
        new(
            state.Kind,
            state.Match is null ? null : new LegacyMatchDto(state.Match.UserId, state.Match.Username),
            state.ClaimableMatches.Select(item => new LegacyMatchDto(item.UserId, item.Username)).ToArray(),
            state.UnavailableMatches.Select(item => new LegacyMatchDto(item.UserId, item.Username)).ToArray());

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail);
}
