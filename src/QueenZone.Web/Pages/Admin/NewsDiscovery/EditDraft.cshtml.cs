using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Pages.Admin.NewsDiscovery;

public sealed class EditDraftModel(
    INewsDiscoveryRepository discoveryRepository,
    UgcHtml ugcHtml) : AdminNewsDiscoveryPageModel
{
    public int Id { get; private set; }

    public NewsCandidate? Candidate { get; private set; }

    [BindProperty]
    public AgentDraftForm Form { get; set; } = new();

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (Candidate is null)
        {
            return NotFound();
        }

        Id = id;
        var draft = await discoveryRepository.GetDraftByCandidateIdAsync(id, cancellationToken);
        if (draft is null)
        {
            return NotFound();
        }

        Form = AgentDraftForm.FromDraft(draft);
        ViewData["Title"] = $"Edit draft for candidate #{id}";
        Breadcrumbs = AdminBreadcrumbs.Page(
            "AI discovery", "/admin/news-discovery",
            $"Review candidate #{id}", $"/admin/news-discovery/{id}",
            "Edit draft");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        Candidate = await discoveryRepository.GetCandidateByIdAsync(id, cancellationToken);
        if (Candidate is null)
        {
            return NotFound();
        }

        Id = id;
        var sanitizedBody = ugcHtml.Sanitize(Form.Body);
        Errors = Validate(Form, sanitizedBody);
        if (Errors.Count > 0)
        {
            Form.Body = sanitizedBody;
            ViewData["Title"] = $"Edit draft for candidate #{id}";
            Breadcrumbs = AdminBreadcrumbs.Page(
                "AI discovery", "/admin/news-discovery",
                $"Review candidate #{id}", $"/admin/news-discovery/{id}",
                "Edit draft");
            return Page();
        }

        var existing = await discoveryRepository.GetDraftByCandidateIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Draft for candidate {id} was not found.");

        await discoveryRepository.UpsertDraftAsync(
            id,
            new NewsAgentDraftUpsert(
                Form.Title.Trim(),
                string.IsNullOrWhiteSpace(Form.Slug) ? null : Form.Slug.Trim(),
                Form.Excerpt.Trim(),
                sanitizedBody.Trim(),
                string.IsNullOrWhiteSpace(Form.AttributionText) ? null : Form.AttributionText.Trim(),
                string.IsNullOrWhiteSpace(Form.SourceNotes) ? null : Form.SourceNotes.Trim(),
                string.IsNullOrWhiteSpace(Form.ConfidenceNotes) ? null : Form.ConfidenceNotes.Trim(),
                Form.SuggestedPublishAt,
                existing.AiRunId),
            cancellationToken);

        if (NewsCandidateWorkflow.CanMarkDrafted(Candidate.Status))
        {
            await discoveryRepository.TryUpdateCandidateStatusAsync(
                id,
                new NewsCandidateStatusUpdate(
                    NewsCandidateStatus.Drafted,
                    ReviewNotes: $"Draft saved by {EditorEmail}."),
                cancellationToken);
        }

        return Redirect($"/admin/news-discovery/{id}");
    }

    private static IReadOnlyList<string> Validate(AgentDraftForm form, string sanitizedBody)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(form.Title))
        {
            errors.Add("Title is required.");
        }
        else if (form.Title.Length > NewsValidation.MaxTitleLength)
        {
            errors.Add($"Title must be {NewsValidation.MaxTitleLength} characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(form.Excerpt))
        {
            errors.Add("Excerpt is required.");
        }
        else if (form.Excerpt.Length > NewsValidation.MaxExcerptLength)
        {
            errors.Add($"Excerpt must be {NewsValidation.MaxExcerptLength} characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            errors.Add("Body is required.");
        }

        return errors;
    }
}

public sealed class AgentDraftForm
{
    public string Title { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string Excerpt { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? AttributionText { get; set; }

    public string? SourceNotes { get; set; }

    public string? ConfidenceNotes { get; set; }

    public DateTime? SuggestedPublishAt { get; set; }

    public static AgentDraftForm FromDraft(NewsAgentDraft draft) =>
        new()
        {
            Title = draft.ProposedTitle,
            Slug = draft.ProposedSlug,
            Excerpt = draft.ProposedExcerpt,
            Body = draft.ProposedBody,
            AttributionText = draft.AttributionText,
            SourceNotes = draft.SourceNotes,
            ConfidenceNotes = draft.ConfidenceNotes,
            SuggestedPublishAt = draft.SuggestedPublishAt
        };
}
