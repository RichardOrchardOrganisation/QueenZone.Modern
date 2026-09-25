using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Public contact form for the mobile app (issue #755). Same <see cref="HelpRequestService"/>
/// and admin inbox as website <c>/contact</c>. Optional mobile JWT; guests supply name and email.
/// </summary>
public static class ContactApiEndpoints
{
    public const string Path = "/api/v1/contact";

    public static void MapContactApiEndpoints(this WebApplication app)
    {
        var group = app.MapApiV1Group("/api/v1", "Contact")
            .DisableAntiforgery();

        group.MapGet("/contact", GetFormAsync)
            .WithName("GetContactForm")
            .WithSummary("Contact form metadata, topics, and a one-time form stamp.")
            .Produces<ContactFormDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/contact", SubmitAsync)
            .WithName("SubmitContactRequest")
            .WithSummary("Submit a contact message to the site admin inbox.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)
            .Accepts<ContactSubmitRequest>("application/json")
            .Produces<ContactSubmitResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> GetFormAsync(
        HttpContext httpContext,
        HelpRequestService helpRequestService)
    {
        var resolved = await ResolveMemberAsync(httpContext);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(CreateFormDto(helpRequestService, resolved.MemberId, resolved.DisplayName));
    }

    internal static async Task<IResult> SubmitAsync(
        HttpContext httpContext,
        HelpRequestService helpRequestService,
        ContactSubmitRequest? request,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveMemberAsync(httpContext);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        if (request is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "A JSON body is required.");
        }

        var result = await helpRequestService.SubmitAsync(
            resolved.MemberId,
            request.Topic ?? string.Empty,
            request.Subject ?? string.Empty,
            request.Message ?? string.Empty,
            request.Name,
            request.Email,
            request.Website,
            request.FormStamp,
            HelpRequestService.ResolveClientIp(httpContext),
            cancellationToken);

        if (result.SilentlyDropped || result.Succeeded)
        {
            return Results.Ok(new ContactSubmitResponse(
                Submitted: true,
                ContactCopy.ConfirmationTitle,
                ContactCopy.ConfirmationMessage));
        }

        var status = result.Error is not null
            && result.Error.StartsWith("Too many", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status429TooManyRequests
            : StatusCodes.Status400BadRequest;

        return Results.Problem(
            statusCode: status,
            title: status == StatusCodes.Status429TooManyRequests ? "Too Many Requests" : "Bad Request",
            detail: result.Error);
    }

    internal static ContactFormDto CreateFormDto(
        HelpRequestService helpRequestService,
        Guid? memberId,
        string? displayName)
    {
        var signedIn = memberId is not null;
        return new ContactFormDto(
            SignedIn: signedIn,
            SignedInDisplayName: signedIn ? displayName : null,
            RequiresContactDetails: !signedIn,
            FormStamp: helpRequestService.IssueFormStamp(),
            Intro: ContactCopy.Intro,
            ConfirmationTitle: ContactCopy.ConfirmationTitle,
            ConfirmationMessage: ContactCopy.ConfirmationMessage,
            Topics: HelpRequestTopic.All
                .Select(value => new ContactTopicDto(value, HelpRequestTopic.DisplayName(value)))
                .ToArray(),
            Limits: new ContactFieldLimitsDto(
                HelpRequestService.MinSubjectLength,
                HelpRequestService.MaxSubjectLength,
                HelpRequestService.MinMessageLength,
                HelpRequestService.MaxMessageLength,
                HelpRequestService.MaxNameLength,
                HelpRequestService.MaxEmailLength));
    }

    internal static async Task<(Guid? MemberId, string? DisplayName, IResult? Failure)> ResolveMemberAsync(
        HttpContext httpContext)
    {
        if (httpContext.Request.Headers.ContainsKey("Authorization"))
        {
            var bearer = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersBearer);
            if (!bearer.Succeeded || bearer.Principal is null)
            {
                return (null, null, Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "The access token is invalid or expired."));
            }

            return FromPrincipal(bearer.Principal);
        }

        var member = await httpContext.AuthenticateMemberAsync();
        if (!member.Succeeded || member.Principal is null)
        {
            return (null, null, null);
        }

        return FromPrincipal(member.Principal);
    }

    private static (Guid? MemberId, string? DisplayName, IResult? Failure) FromPrincipal(ClaimsPrincipal principal)
    {
        var idValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var memberId))
        {
            return (null, null, null);
        }

        return (memberId, principal.FindFirstValue(ClaimTypes.Name), null);
    }
}
