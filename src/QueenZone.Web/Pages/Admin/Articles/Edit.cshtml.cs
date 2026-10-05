using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin.News;

namespace QueenZone.Web.Pages.Admin.Articles;

[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024, ValueLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
[IgnoreAntiforgeryToken]
[TypeFilter(typeof(EditorialArticleEditorRequestGuardFilter))]
public sealed class EditModel(
    IEditorialArticleRepository editorialArticles,
    IArticlesRepository legacyArticles,
    NewsArticleImageService imageService,
    IAdminPhotoRepository adminPhotoRepository,
    UgcHtml ugcHtml,
    PublicQueryCacheService publicQueryCache,
    IAntiforgery antiforgery,
    ILogger<EditModel> logger) : AdminArticlesPageModel, IAsyncExceptionFilter
{
    [BindProperty] public EditorialArticleForm Form { get; set; } = new();
    public List<string> Errors { get; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? id, int? legacyId, CancellationToken ct)
    {
        if (id is Guid editorialId)
        {
            var article = await editorialArticles.GetAsync(editorialId, ct);
            if (article is null) return NotFound();
            Form = EditorialArticleForm.From(article);
        }
        else if (legacyId is int archiveId)
        {
            var existing = (await editorialArticles.GetAllAsync(ct)).SingleOrDefault(x => x.LegacyArticleId == archiveId);
            if (existing is not null) return Redirect($"/admin/articles/editor/{existing.Id}");
            var legacy = await legacyArticles.GetByIdAsync(archiveId, ct);
            if (legacy is null) return NotFound();
            Form = new EditorialArticleForm
            {
                LegacyArticleId = legacy.Id,
                Title = legacy.Title,
                Slug = NewsSlug.Slugify(legacy.Title),
                Excerpt = legacy.Excerpt,
                Body = legacy.Body,
                AuthorName = legacy.AuthorName ?? "QueenZone Editorial",
                Category = legacy.CategoryName ?? "Feature",
                Tags = legacy.Tags,
                Source = legacy.Source,
                ImageBlobKey = legacy.ImageBlobKey,
                PublishedAt = legacy.PublishedAt,
            };
        }
        else
        {
            Form.AuthorName = "QueenZone Editorial";
            Form.Category = "Feature";
        }
        ViewData["Title"] = Form.Id is null ? "Create article" : "Edit article";
        Breadcrumbs = AdminBreadcrumbs.Page("Articles", "/admin/articles", (string)ViewData["Title"]!);
        await LoadCategoriesAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id, CancellationToken ct)
    {
        Form.Id = id ?? Form.Id;
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            logger.LogWarning(ex, "Articles editor POST rejected: {Reason}", ex.Message);
            AddError(EditorialArticleEditorRequestGuardFilter.AntiforgeryError);
            await PrepareRedisplayAsync(ct);
            return Page();
        }

        AddBindingErrors();

        var existing = Form.Id is Guid existingId ? await editorialArticles.GetAsync(existingId, ct) : null;
        var sanitizedBody = ugcHtml.Sanitize(Form.Body);
        Validate(sanitizedBody);
        var draft = new AdminNewsDraft(Form.Title, Form.Slug, Form.Excerpt, sanitizedBody, Form.ResolvedPublishedAt, null, Form.ImageBlobKey, Form.ImageGalleryPicId);
        var galleryError = await NewsArticleGalleryPicker.ValidatePicAsync(adminPhotoRepository, Form.ArticleImage, Form.ImageGalleryPicId, ct);
        if (galleryError is not null) Errors.Add(galleryError);
        var applied = await imageService.TryApplyAsync(Form.ArticleImage, Form.ToCrop(), draft, User, Errors.Count == 0, ct);
        if (applied.Error is not null) Errors.Add(applied.Error);
        if (Errors.Count > 0) { await PrepareRedisplayAsync(ct); return Page(); }

        EditorialArticle saved;
        try { saved = await editorialArticles.SaveDraftAsync(Form.ToDraft(sanitizedBody, applied.Draft.ImageBlobKey), EditorEmail, ct); }
        catch (InvalidOperationException ex) { await imageService.TryDeletePreviousUgcArticlesAsync(applied.Draft.ImageBlobKey, Form.ImageBlobKey, ct); Errors.Add(ex.Message); await PrepareRedisplayAsync(ct); return Page(); }
        if (existing is not null && !string.Equals(existing.ImageBlobKey, existing.PublishedImageBlobKey, StringComparison.Ordinal))
        {
            await imageService.TryDeletePreviousUgcArticlesAsync(existing.ImageBlobKey, saved.ImageBlobKey, ct);
        }
        publicQueryCache.InvalidateArticlesCache();
        return Redirect($"/admin/articles/editor/{saved.Id}");
    }

    private void AddBindingErrors()
    {
        if (!ModelState.IsValid)
        {
            foreach (var entry in ModelState)
            {
                foreach (var error in entry.Value.Errors)
                {
                    AddError(string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? EditorialArticleEditorRequestGuardFilter.BindError
                        : error.ErrorMessage);
                }
            }

            logger.LogWarning(
                "Articles editor POST bind failed: {Keys}",
                string.Join(", ", ModelState.Where(entry => entry.Value is { Errors.Count: > 0 }).Select(entry => entry.Key)));
        }

    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (!EditorialArticleEditorRequestGuardFilter.IsHandled(context.Exception))
        {
            return;
        }

        logger.LogWarning(context.Exception, "Articles editor POST rejected: {Reason}", context.Exception.Message);
        AddError(EditorialArticleEditorRequestGuardFilter.MessageFor(context.Exception));
        await PrepareRedisplayAsync(context.HttpContext.RequestAborted);
        context.ExceptionHandled = true;
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.Result = Page();
    }

    internal void AddError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message) && !Errors.Contains(message, StringComparer.Ordinal))
        {
            Errors.Add(message);
        }
    }

    internal async Task PrepareRedisplayAsync(CancellationToken ct)
    {
        ViewData["Title"] = Form.Id is null ? "Create article" : "Edit article";
        Breadcrumbs = AdminBreadcrumbs.Page("Articles", "/admin/articles", (string)ViewData["Title"]!);
        await LoadCategoriesAsync(ct);
    }

    private void Validate(string body)
    {
        if (string.IsNullOrWhiteSpace(Form.Title)) Errors.Add("Title is required.");
        if (string.IsNullOrWhiteSpace(Form.Excerpt)) Errors.Add("Excerpt is required.");
        if (string.IsNullOrWhiteSpace(Form.AuthorName)) Errors.Add("Author is required.");
        if (string.IsNullOrWhiteSpace(Form.Category)) Errors.Add("Category is required.");
        if (string.IsNullOrWhiteSpace(body)) Errors.Add("Body is required.");
    }

    private async Task LoadCategoriesAsync(CancellationToken ct) => Categories = (await LoadAllLegacyArchiveAsync(legacyArticles, ct))
        .Select(x => x.CategoryName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
}
