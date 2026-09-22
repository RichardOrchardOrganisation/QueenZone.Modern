using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;

namespace QueenZone.Web;

public static class ArticleAutosaveEndpoint
{
    public const string Route = "/submit/article/autosave";

    public static IEndpointRouteBuilder MapArticleAutosaveEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, HandleAsync)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IArticleSubmissionRepository repository,
        IAntiforgery antiforgery,
        UgcHtml ugcHtml,
        CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var authResult = await context.AuthenticateAsync(MemberAuthenticationSchemes.MembersCookie);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return Results.Unauthorized();
        }

        var idValue = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var memberId))
        {
            return Results.Unauthorized();
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var title = form["Title"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest(new { error = "Title is required." });
        }

        Guid? draftId = Guid.TryParse(form["DraftId"].FirstOrDefault(), out var parsedId) && parsedId != Guid.Empty
            ? parsedId
            : null;

        var body = ugcHtml.Sanitize(form["Body"].FirstOrDefault());
        var excerpt = form["Excerpt"].FirstOrDefault();
        var tags = form["Tags"].FirstOrDefault();

        try
        {
            var saved = await repository.UpsertDraftAsync(
                new ArticleSubmissionDraft(draftId, memberId, title, excerpt, body, null, tags),
                cancellationToken);

            return Results.Ok(new { draftId = saved.Id });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
