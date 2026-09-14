using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Account;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class MySubmissionsModel(
    IPhotoSubmissionRepository photoSubmissionRepository,
    INewsSuggestionRepository newsSuggestionRepository,
    IArticleSubmissionRepository articleSubmissionRepository,
    ITriviaFactSubmissionRepository triviaFactSubmissionRepository,
    IFanPerformanceSubmissionRepository fanPerformanceSubmissionRepository,
    FanPerformanceSubmissionService fanPerformanceSubmissionService,
    INewsRepository newsRepository) : PageModel
{
    public const int PageSize = 10;

    public const string TabPhotos = "photos";
    public const string TabNews = "news";
    public const string TabArticles = "articles";
    public const string TabTrivia = "trivia";
    public const string TabPerformances = "performances";

    public string ActiveTab { get; private set; } = TabPhotos;

    public int CurrentPage { get; private set; } = 1;

    public IReadOnlyList<PhotoSubmission> PhotoSubmissions { get; private set; } = [];

    public IReadOnlyList<NewsSuggestionRow> NewsSuggestions { get; private set; } = [];

    public IReadOnlyList<ArticleSubmission> ArticleSubmissions { get; private set; } = [];

    public IReadOnlyList<TriviaFactSubmission> TriviaSubmissions { get; private set; } = [];

    public IReadOnlyList<FanPerformanceSubmission> FanPerformanceSubmissions { get; private set; } = [];

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? tab, int? page, CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        return await LoadAsync(memberId.Value, tab, page, cancellationToken);
    }

    public async Task<IActionResult> OnPostWithdrawFanPerformanceAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        var result = await fanPerformanceSubmissionService.WithdrawAsync(
            memberId.Value,
            id,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not withdraw the submission.");
            return await LoadAsync(memberId.Value, TabPerformances, page: 1, cancellationToken);
        }

        return Redirect(GetTabHref(TabPerformances));
    }

    public async Task<IActionResult> OnPostReplyFanPerformanceAsync(
        Guid id,
        string? reply,
        CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        var result = await fanPerformanceSubmissionService.ReplyNeedsInfoAsync(
            memberId.Value,
            id,
            reply,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not send the reply.");
            return await LoadAsync(memberId.Value, TabPerformances, page: 1, cancellationToken);
        }

        return Redirect(GetTabHref(TabPerformances));
    }

    public string GetTabHref(string tab) =>
        $"/account/my-submissions?tab={Uri.EscapeDataString(tab)}";

    public string GetArticleEditPath(Guid id) => $"/submit/article/{id:D}";

    private ArchivePaginationViewModel? BuildPagination(int totalCount)
    {
        var totalPages = ArchivePagination.GetTotalPages(totalCount, PageSize);
        return ArchivePagination.BuildViewModel(
            "My submissions pagination",
            CurrentPage,
            totalPages,
            pageNumber => pageNumber <= 1
                ? GetTabHref(ActiveTab)
                : $"{GetTabHref(ActiveTab)}&page={pageNumber}");
    }

    private async Task<IReadOnlyList<NewsSuggestionRow>> MapNewsRowsAsync(
        IReadOnlyList<NewsSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        var promotedNewsIds = suggestions
            .Where(suggestion => suggestion.Status == NewsSuggestionStatus.Promoted)
            .Select(suggestion => suggestion.PromotedNewsId)
            .OfType<int>()
            .Distinct()
            .ToArray();
        var publishedNewsById = promotedNewsIds.Length == 0
            ? new Dictionary<int, NewsItem>()
            : (await newsRepository.GetByIdsAsync(promotedNewsIds, cancellationToken))
                .Where(news => news.IsPublished)
                .ToDictionary(news => news.Id);

        return suggestions.Select(suggestion =>
        {
            string? publishedPath = null;
            if (suggestion.Status == NewsSuggestionStatus.Promoted
                && suggestion.PromotedNewsId is int newsId
                && publishedNewsById.TryGetValue(newsId, out var news))
            {
                publishedPath = NewsRoutes.GetNewsDetailPath(news);
            }

            return new NewsSuggestionRow(suggestion, publishedPath);
        }).ToList();
    }

    private async Task<IActionResult> LoadAsync(
        Guid memberId,
        string? tab,
        int? page,
        CancellationToken cancellationToken)
    {
        ActiveTab = NormalizeTab(tab);
        CurrentPage = Math.Max(1, page ?? 1);

        switch (ActiveTab)
        {
            case TabNews:
                {
                    var result = await newsSuggestionRepository.GetBySubmitterAsync(
                        memberId, CurrentPage, PageSize, cancellationToken);
                    NewsSuggestions = await MapNewsRowsAsync(result.Items, cancellationToken);
                    Pagination = BuildPagination(result.TotalCount);
                    break;
                }
            case TabArticles:
                {
                    var result = await articleSubmissionRepository.GetDraftsForMemberAsync(
                        memberId, CurrentPage, PageSize, cancellationToken);
                    ArticleSubmissions = result.Items;
                    Pagination = BuildPagination(result.TotalCount);
                    break;
                }
            case TabTrivia:
                {
                    var result = await triviaFactSubmissionRepository.GetBySubmitterAsync(
                        memberId, CurrentPage, PageSize, cancellationToken);
                    TriviaSubmissions = result.Items;
                    Pagination = BuildPagination(result.TotalCount);
                    break;
                }
            case TabPerformances:
                {
                    var result = await fanPerformanceSubmissionRepository.GetBySubmitterAsync(
                        memberId, CurrentPage, PageSize, cancellationToken);
                    FanPerformanceSubmissions = result.Items;
                    Pagination = BuildPagination(result.TotalCount);
                    break;
                }
            default:
                {
                    var result = await photoSubmissionRepository.GetBySubmitterAsync(
                        memberId, CurrentPage, PageSize, cancellationToken);
                    PhotoSubmissions = result.Items;
                    Pagination = BuildPagination(result.TotalCount);
                    break;
                }
        }

        ViewData["Title"] = "My submissions";
        return Page();
    }

    private static string NormalizeTab(string? tab) =>
        tab?.Trim().ToLowerInvariant() switch
        {
            TabNews => TabNews,
            TabArticles => TabArticles,
            TabTrivia => TabTrivia,
            TabPerformances => TabPerformances,
            _ => TabPhotos,
        };

    private async Task<Guid?> GetCurrentMemberIdAsync()
    {
        var authResult = await HttpContext.AuthenticateMemberAsync();
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return null;
        }

        var idValue = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idValue, out var id) ? id : null;
    }

    public sealed record NewsSuggestionRow(NewsSuggestion Suggestion, string? PublishedArticlePath);
}
