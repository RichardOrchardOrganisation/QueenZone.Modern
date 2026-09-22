using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public static class ForumPollEndpoints
{
    public static void MapForumPollEndpoints(this WebApplication app)
    {
        app.MapPost("/forum/poll/{pollId:guid}/vote", async (
                Guid pollId,
                HttpContext httpContext,
                IForumPollRepository pollRepository,
                IAntiforgery antiforgery,
                CancellationToken cancellationToken) =>
            await VoteAsync(pollId, httpContext, pollRepository, antiforgery, cancellationToken))
            .RequireAuthorization(MemberAuthenticationSchemes.MemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery()
            .WithName("VoteForumPoll");

        app.MapPost("/forum/poll/{pollId:guid}/close", async (
                Guid pollId,
                HttpContext httpContext,
                IForumPollRepository pollRepository,
                IAntiforgery antiforgery,
                IOptions<AdminOptions> adminOptions,
                CancellationToken cancellationToken) =>
            await CloseAsync(pollId, httpContext, pollRepository, antiforgery, adminOptions.Value, cancellationToken))
            .RequireAuthorization(MemberAuthenticationSchemes.MemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery()
            .WithName("CloseForumPoll");
    }

    internal static async Task<IResult> VoteAsync(
        Guid pollId,
        HttpContext httpContext,
        IForumPollRepository pollRepository,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(httpContext.User);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        IFormCollection form;
        try
        {
            // Read form first so antiforgery can see __RequestVerificationToken.
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "Invalid form data." });
        }

        if (!await antiforgery.IsRequestValidAsync(httpContext))
        {
            return Results.BadRequest(new { error = "Invalid antiforgery token." });
        }

        var optionIds = ForumPollVoteMapper.ParseOptionIds(form);
        var returnUrl = ResolveReturnUrl(form);

        try
        {
            await pollRepository.CastVoteAsync(pollId, memberId.Value, optionIds, cancellationToken);
            return Results.Redirect(returnUrl + "#poll");
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToFormResult(ex);
        }
    }

    internal static async Task<IResult> CloseAsync(
        Guid pollId,
        HttpContext httpContext,
        IForumPollRepository pollRepository,
        IAntiforgery antiforgery,
        AdminOptions adminOptions,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(httpContext.User);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        IFormCollection form;
        try
        {
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "Invalid form data." });
        }

        if (!await antiforgery.IsRequestValidAsync(httpContext))
        {
            return Results.BadRequest(new { error = "Invalid antiforgery token." });
        }

        var returnUrl = ResolveReturnUrl(form);
        var isAdmin = IsAdmin(httpContext.User, adminOptions);

        try
        {
            await pollRepository.ClosePollAsync(pollId, memberId.Value, isAdmin, cancellationToken);
            return Results.Redirect(returnUrl + "#poll");
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToFormResult(ex);
        }
    }

    internal static bool IsAdmin(ClaimsPrincipal user, AdminOptions adminOptions) =>
        AdminAllowlist.IsAllowed(user, adminOptions);

    private static string ResolveReturnUrl(IFormCollection form)
    {
        var returnUrl = form["returnUrl"].ToString();
        return string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/forum/", StringComparison.Ordinal)
            ? "/forum"
            : returnUrl;
    }
}
