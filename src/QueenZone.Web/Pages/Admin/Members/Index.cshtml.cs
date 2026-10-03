using System.Text.Json;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Pages.Admin.Members;

public sealed class IndexModel(
    IMemberAccountRepository memberAccountRepository,
    IForumWriteRepository forumWriteRepository) : AdminMembersPageModel
{
    private const int PageSize = 50;

    public IReadOnlyList<MemberAccount> Members { get; private set; } = [];

    public string? Query { get; private set; }

    public int PageNumber { get; private set; } = 1;

    public int TotalCount { get; private set; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public ForumAuthorContentSummary? NoAccountAuthor { get; private set; }

    public string? HideAuthorConfirmMessageJson { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(string? query, int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        Query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        PageNumber = Math.Max(1, pageNumber);

        var result = await memberAccountRepository.SearchMembersAsync(Query, PageNumber, PageSize, cancellationToken);
        Members = result.Members;
        TotalCount = result.TotalCount;
        if (TotalCount == 0 && Query is not null)
        {
            NoAccountAuthor = await forumWriteRepository.FindNoAccountForumAuthorAsync(Query, cancellationToken);
            if (NoAccountAuthor is not null)
            {
                HideAuthorConfirmMessageJson = JsonSerializer.Serialize(
                    $"Hide all posts and threads started by {NoAccountAuthor.DisplayName}? Other people's posts stay.");
            }
        }
        ViewData["Title"] = "Members";
        Breadcrumbs = AdminBreadcrumbs.Section("Members", "/admin/members");
    }
}
