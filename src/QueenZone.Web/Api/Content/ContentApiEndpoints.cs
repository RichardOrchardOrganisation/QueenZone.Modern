using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Public <c>/api/v1/content/*</c> routes for the mobile app
/// (issues #726 / #743 / #747 / #1100 / #1186). <see cref="MapContentApiEndpoints"/>
/// creates the content group and registers news, articles, timeline, quotes,
/// trivia, the Home poll, quizzes, biography, discography, Freddie Tribute,
/// photos, and fan performances from sibling endpoint types. Paths and route
/// names are unchanged.
/// </summary>
public static class ContentApiEndpoints
{
    public const string RootPath = "/api/v1/content";

    public static void MapContentApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(RootPath)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Content")
            .DisableAntiforgery();

        group.MapContentNewsApiEndpoints();
        group.MapContentArticleApiEndpoints();
        group.MapContentTimelineApiEndpoints();
        group.MapContentLiveActivityApiEndpoints();
        group.MapContentQuoteApiEndpoints();
        group.MapContentHomePollApiEndpoints();
        group.MapContentQuizApiEndpoints();
        group.MapContentBiographyApiEndpoints();
        group.MapContentDiscographyApiEndpoints();
        group.MapContentFreddieTributeApiEndpoints();
        group.MapContentPhotoApiEndpoints();
        group.MapContentFanPerformanceApiEndpoints();
    }

    internal static async Task<Guid?> TryGetViewerMemberIdAsync(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.ContainsKey("Authorization"))
        {
            var bearer = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersBearer);
            if (bearer.Succeeded)
            {
                return ForumMember.GetMemberId(bearer.Principal);
            }
        }

        var member = await httpContext.AuthenticateMemberAsync();
        return member.Succeeded ? ForumMember.GetMemberId(member.Principal) : null;
    }
}
