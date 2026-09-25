using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
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

        // Authors close their own poll with the member cookie. Closing someone else's poll
        // requires the admin scheme (see CloseAsync). MemberPolicy alone would block an
        // Entra admin who has no member cookie; Admin policy alone would block authors.
        var closePoll = app.MapPost("/forum/poll/{pollId:guid}/close", async (
                Guid pollId,
                HttpContext httpContext,
                IForumPollRepository pollRepository,
                IAntiforgery antiforgery,
                IOptions<AdminOptions> adminOptions,
                CancellationToken cancellationToken) =>
            await CloseAsync(pollId, httpContext, pollRepository, antiforgery, adminOptions.Value, cancellationToken))
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery()
            .WithName("CloseForumPoll");

        closePoll.RequireAuthorization(policy =>
        {
            policy.AuthenticationSchemes.Add(AdminAuthenticationSchemes.CompositeScheme);
            policy.AuthenticationSchemes.Add(MemberAuthenticationSchemes.MembersCookie);
            if (QueenZoneEnvironments.UsesTestAuth(app.Environment))
            {
                policy.AuthenticationSchemes.Add(TestMemberAuthHandler.SchemeName);
            }

            policy.RequireAuthenticatedUser();
        });
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
        var isAdmin = false;
        // Direct unit calls have no authentication service and stay unauthorized.
        if (httpContext.RequestServices.GetService<IAuthenticationService>() is not null)
        {
            isAdmin = await ForumAdminAccess.IsAdminAsync(httpContext, adminOptions);
            if (memberId is null)
            {
                var memberAuth = await httpContext.AuthenticateMemberAsync();
                if (memberAuth.Succeeded)
                {
                    memberId = ForumMember.GetMemberId(memberAuth.Principal);
                }
            }
        }

        if (!isAdmin && memberId is null)
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

        try
        {
            await pollRepository.ClosePollAsync(pollId, memberId ?? Guid.Empty, isAdmin, cancellationToken);
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
