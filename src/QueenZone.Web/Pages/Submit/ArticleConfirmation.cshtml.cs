using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class ArticleConfirmationModel(IArticleSubmissionRepository articleSubmissionRepository) : SubmissionConfirmationPageModel<ArticleSubmission>
{
    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        LoadSubmissionAsync(
            id,
            articleSubmissionRepository.GetByIdAsync,
            submission => submission.AuthorMemberId,
            "Article submitted",
            cancellationToken);
}
