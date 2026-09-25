using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Member submission status for the mobile app (issue #745). Reads the same
/// photo, news-suggestion, and article repositories as website
/// <c>/account/my-submissions</c>. Admin approve/reject is visible on the next
/// refresh; there is no extra sync channel.
/// </summary>
public static class SubmissionsApiEndpoints
{
    public const string RootPath = "/api/v1/me/submissions";

    public const string PhotosPath = RootPath + "/photos";

    public const string NewsPath = RootPath + "/news";

    public const string ArticlesPath = RootPath + "/articles";

    public const string FanPerformancesPath = RootPath + "/fan-performances";

    public static void MapSubmissionsApiEndpoints(this WebApplication app)
    {
        var group = app.MapApiV1Group(RootPath, "Submissions")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .DisableAntiforgery();

        group.MapAuthorizedPagedList<PhotoSubmissionItemDto>(
            "/photos",
            GetPhotosAsync,
            "GetMyPhotoSubmissions",
            "Paged list of the signed-in member's photo submissions and review status.");

        group.MapAuthorizedPagedList<NewsSuggestionItemDto>(
            "/news",
            GetNewsAsync,
            "GetMyNewsSuggestions",
            "Paged list of the signed-in member's news suggestions and review status.");

        group.MapAuthorizedPagedList<ArticleSubmissionItemDto>(
            "/articles",
            GetArticlesAsync,
            "GetMyArticleSubmissions",
            "Paged list of the signed-in member's article submissions and review status.");

        group.MapAuthorizedPagedList<FanPerformanceSubmissionItemDto>(
            "/fan-performances",
            GetFanPerformancesAsync,
            "GetMyFanPerformanceSubmissions",
            "Paged list of the signed-in member's fan-performance submissions and review status.");
    }

    internal static Task<IResult> GetPhotosAsync(
        HttpContext httpContext,
        [FromServices] IPhotoSubmissionRepository photoSubmissionRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        GetMemberSubmissionPageAsync(
            httpContext,
            page,
            pageSize,
            async (memberId, request, ct) =>
            {
                var result = await photoSubmissionRepository.GetBySubmitterAsync(
                    memberId, request.Page, request.PageSize, ct);
                return (SubmissionsApiMapper.ToPhotos(result.Items), result.TotalCount);
            },
            cancellationToken);

    internal static Task<IResult> GetNewsAsync(
        HttpContext httpContext,
        [FromServices] INewsSuggestionRepository newsSuggestionRepository,
        [FromServices] INewsRepository newsRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        GetMemberSubmissionPageAsync(
            httpContext,
            page,
            pageSize,
            async (memberId, request, ct) =>
            {
                var result = await newsSuggestionRepository.GetBySubmitterAsync(
                    memberId, request.Page, request.PageSize, ct);

                var promotedNewsIds = result.Items
                    .Where(suggestion => suggestion.Status == NewsSuggestionStatus.Promoted
                        && suggestion.PromotedNewsId is not null)
                    .Select(suggestion => suggestion.PromotedNewsId!.Value)
                    .Distinct()
                    .ToArray();

                IReadOnlyDictionary<int, NewsItem> newsById;
                if (promotedNewsIds.Length == 0)
                {
                    newsById = new Dictionary<int, NewsItem>();
                }
                else
                {
                    var newsItems = await newsRepository.GetByIdsAsync(promotedNewsIds, ct);
                    newsById = newsItems.ToDictionary(item => item.Id);
                }

                var items = new List<NewsSuggestionItemDto>(result.Items.Count);
                foreach (var suggestion in result.Items)
                {
                    NewsItem? news = null;
                    if (suggestion.PromotedNewsId is int newsId)
                    {
                        newsById.TryGetValue(newsId, out news);
                    }

                    items.Add(SubmissionsApiMapper.ToNews(
                        suggestion,
                        SubmissionsApiMapper.ResolvePublishedNewsPath(suggestion, news)));
                }

                return ((IReadOnlyList<NewsSuggestionItemDto>)items, result.TotalCount);
            },
            cancellationToken);

    internal static Task<IResult> GetArticlesAsync(
        HttpContext httpContext,
        [FromServices] IArticleSubmissionRepository articleSubmissionRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        GetMemberSubmissionPageAsync(
            httpContext,
            page,
            pageSize,
            async (memberId, request, ct) =>
            {
                var result = await articleSubmissionRepository.GetDraftsForMemberAsync(
                    memberId, request.Page, request.PageSize, ct);
                return (SubmissionsApiMapper.ToArticles(result.Items), result.TotalCount);
            },
            cancellationToken);

    internal static Task<IResult> GetFanPerformancesAsync(
        HttpContext httpContext,
        [FromServices] IFanPerformanceSubmissionRepository fanPerformanceSubmissionRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        GetMemberSubmissionPageAsync(
            httpContext,
            page,
            pageSize,
            async (memberId, request, ct) =>
            {
                var result = await fanPerformanceSubmissionRepository.GetBySubmitterAsync(
                    memberId, request.Page, request.PageSize, ct);
                return (SubmissionsApiMapper.ToFanPerformances(result.Items), result.TotalCount);
            },
            cancellationToken);

    private static async Task<IResult> GetMemberSubmissionPageAsync<TItem>(
        HttpContext httpContext,
        int? page,
        int? pageSize,
        Func<Guid, ApiPageRequest, CancellationToken, Task<(IReadOnlyList<TItem> Items, int TotalCount)>> load,
        CancellationToken cancellationToken)
    {
        var unauthorized = UnauthorizedIfMissingMember(httpContext, out var memberId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var request = ApiPagination.Normalize(page, pageSize);
        var (items, totalCount) = await load(memberId, request, cancellationToken);
        return ApiV1EndpointHelpers.OkNoStorePaged(
            httpContext,
            items,
            request.Page,
            request.PageSize,
            totalCount);
    }

    internal static IResult? UnauthorizedIfMissingMember(HttpContext httpContext, out Guid memberId)
    {
        var idValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(idValue, out memberId))
        {
            return null;
        }

        memberId = default;
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            detail: "The access token is invalid or expired.");
    }
}
