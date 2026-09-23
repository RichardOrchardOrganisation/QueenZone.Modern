using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Trivia;

public sealed class IndexModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public TriviaFactItem? Fact { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
    [
        BreadcrumbItem.Home,
        new BreadcrumbItem("Trivia", "/trivia"),
    ];

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public Task OnPostNextAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Fact = await publicQueryCache.GetRandomPublishedTriviaAsync(cancellationToken);

        ViewData["Title"] = "Queen Trivia | QueenZone";
        ViewData["CanonicalPath"] = "/trivia";
        ViewData["Description"] = "A random Queen trivia fact from the Queenzone archive.";
    }
}
