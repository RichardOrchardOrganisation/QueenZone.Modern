using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.News;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

[Collection(AdminNewsDeleteErrorCollection.Name)]
public sealed class AdminNewsRoutesTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>,
    IAsyncLifetime
{
    private const string AdminEmail = AdminHttpTestHelpers.AdminEmail;
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory isolated;

    public AdminNewsRoutesTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        isolated = variants.Get(WebHostVariants.IsolatedAdminNews);
    }

    public Task InitializeAsync() => isolated.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AnonymousUserCannotAccessAdminRoutes()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/admin/news");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedNonAdminCannotAccessAdminRoutes()
    {
        var client = CreateClient("stranger@example.com");

        var response = await client.GetAsync("/admin/news");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminRootRendersDashboard()
    {
        var client = CreateClient(AdminEmail);

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Dashboard", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanCreatePreviewPublishAndUnpublishArticle()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var createResponse = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Admin created article",
                ["excerpt"] = "Created from the admin workflow.",
                ["body"] = "Plain text body for the new article.",
                ["publishedAt"] = "2026-06-14",
                ["sourceUrl"] = "https://example.com/admin-source"
            });

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var editPath = createResponse.Headers.Location!.OriginalString;
        Assert.Matches("/admin/news/\\d+/edit", editPath);

        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);

        var previewBody = await client.GetStringAsync($"/admin/news/{articleId}/preview");
        Assert.Contains("Admin created article", previewBody);
        Assert.Contains("Plain text body for the new article.", previewBody);
        Assert.Contains("Source: <a href=\"https://example.com/admin-source\"", previewBody);
        Assert.Contains(">https://example.com/admin-source</a>", previewBody);
        Assert.Contains("This draft is hidden from the public archive.", previewBody);

        var publicBeforePublish = await client.GetAsync("/news");
        var publicBodyBeforePublish = await publicBeforePublish.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Admin created article", publicBodyBeforePublish);

        var beforePublishDate = DateTime.UtcNow.Date;
        var publishResponse = await PostActionAsync(client, $"/admin/news/{articleId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);

        var publishedArticle = IsolatedNews.GetArticle(articleId);
        Assert.NotNull(publishedArticle);
        Assert.True(publishedArticle.IsPublished);
        Assert.InRange(publishedArticle.PublishedAt.Date, beforePublishDate, DateTime.UtcNow.Date);

        var publicBodyAfterPublish = await client.GetStringAsync("/news");
        Assert.Contains("Admin created article", publicBodyAfterPublish);
        Assert.Contains($"/news/{articleId}/admin-created-article", publicBodyAfterPublish);

        var unpublishResponse = await PostActionAsync(client, $"/admin/news/{articleId}/unpublish");
        Assert.Equal(HttpStatusCode.Redirect, unpublishResponse.StatusCode);

        var publicBodyAfterUnpublish = await client.GetStringAsync("/news");
        Assert.DoesNotContain("Admin created article", publicBodyAfterUnpublish);
    }

    [Fact]
    public async Task ValidationFailuresAreReturnedForInvalidDraft()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "",
                ["excerpt"] = "",
                ["body"] = "<p>Rich text body is now allowed</p>",
                ["publishedAt"] = "",
                ["sourceUrl"] = "javascript:alert(1)"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required.", body);
        Assert.Contains("Excerpt is required.", body);
        Assert.DoesNotContain("Article body must be plain text.", body);
        Assert.Contains("Publication date is required.", body);
        Assert.Contains("Source URL must be a safe http or https link.", body);
    }

    [Fact]
    public async Task HtmlBodyIsSavedAndRenderedAsHtml()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var createResponse = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Rich text article",
                ["excerpt"] = "Article with bold text.",
                ["body"] = "<p><strong>bold content</strong></p>",
                ["publishedAt"] = "2026-06-14"
            });

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        Assert.DoesNotContain("Article body must be plain text.", createResponse.Headers.Location?.OriginalString ?? string.Empty);

        var editPath = createResponse.Headers.Location!.OriginalString;
        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);

        var editBody = await client.GetStringAsync($"/admin/news/{articleId}/edit");
        Assert.DoesNotContain("Article body must be plain text.", editBody);

        var publishResponse = await PostActionAsync(client, $"/admin/news/{articleId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);

        var detailSlug = "rich-text-article";
        var detailBody = await client.GetStringAsync($"/news/{articleId}/{detailSlug}");
        Assert.Contains("<strong>bold content</strong>", detailBody);
        Assert.DoesNotContain("&lt;strong&gt;", detailBody);
    }

    [Fact]
    public async Task CreatePostWithoutAntiforgeryTokenReturnsBadRequest()
    {
        var client = CreateClient(AdminEmail, new SharedNewsStore());

        var response = await client.PostAsync(
            "/admin/news",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["title"] = "Missing token article",
                ["excerpt"] = "Missing token excerpt",
                ["body"] = "Missing token body",
                ["publishedAt"] = "2026-06-14"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateSlugIsRejected()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                2001,
                "Existing article",
                "shared-slug",
                "Existing excerpt",
                "Existing body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);

        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Another article",
                ["slug"] = "shared-slug",
                ["excerpt"] = "Another excerpt",
                ["body"] = "Another body",
                ["publishedAt"] = "2026-06-15"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Slug is already in use by another article.", body);
    }

    [Fact]
    public async Task DuplicateSlugOnEditIsRejected()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                2002,
                "Existing article",
                "shared-edit-slug",
                "Existing excerpt",
                "Existing body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail),
            new AdminNewsArticle(
                2003,
                "Editable article",
                "editable-slug",
                "Editable excerpt",
                "Editable body",
                new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);

        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/2003/edit",
            "/admin/news/2003",
            new Dictionary<string, string>
            {
                ["title"] = "Editable article",
                ["slug"] = "shared-edit-slug",
                ["excerpt"] = "Editable excerpt",
                ["body"] = "Editable body",
                ["publishedAt"] = "2026-06-02"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Slug is already in use by another article.", body);
    }

    [Fact]
    public async Task DuplicateSlugOnPublishIsRejected()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                2004,
                "Published collision owner",
                "publish-collision-slug",
                "Owner excerpt",
                "Owner body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                true,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail),
            new AdminNewsArticle(
                2005,
                "Draft with colliding slug",
                "publish-collision-slug",
                "Draft excerpt",
                "Draft body",
                new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);

        var client = CreateClient(AdminEmail, store);

        var publishResponse = await PostActionAsync(client, "/admin/news/2005/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);
        Assert.Equal("/admin/news/2005/edit", publishResponse.Headers.Location!.OriginalString);

        var editBody = await client.GetStringAsync("/admin/news/2005/edit");
        Assert.Contains("Slug is already in use by another article.", editBody);
        Assert.Contains("admin-status--error", editBody);
    }

    [Fact]
    public async Task AuthorizedAdminCanOpenEditPage()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4001,
                "Editable article",
                "editable-article",
                "Editable excerpt",
                "Editable body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);

        var client = CreateClient(AdminEmail, store);

        var response = await client.GetAsync("/admin/news/4001/edit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit article", body);
        Assert.Contains("Editable article", body);
        Assert.Contains("admin-form-panel", body);
        Assert.DoesNotContain("<h1>Edit: Editable article</h1>", body);
    }

    [Fact]
    public async Task AuthorizedAdminCanSaveEditedArticle()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4002,
                "Before save",
                "before-save",
                "Original excerpt",
                "Original body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        var client = CreateClient(AdminEmail, store);

        var saveResponse = await PostArticleAsync(
            client,
            "/admin/news/4002/edit",
            "/admin/news/4002",
            new Dictionary<string, string>
            {
                ["title"] = "After save",
                ["slug"] = "after-save",
                ["excerpt"] = "Saved excerpt",
                ["body"] = "Saved body text",
                ["publishedAt"] = "2026-06-15"
            });

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        Assert.Equal("/admin/news/4002/edit", saveResponse.Headers.Location!.OriginalString);

        var editBody = await client.GetStringAsync("/admin/news/4002/edit");
        Assert.Contains("After save", editBody);
        Assert.Contains("Saved body text", editBody);
        Assert.DoesNotContain("Original body", editBody);
    }

    [Fact]
    public async Task OverlongTitleIsRejected()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = new string('x', NewsValidation.MaxTitleLength + 1),
                ["excerpt"] = "Excerpt",
                ["body"] = "Body",
                ["publishedAt"] = "2026-06-14"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"Title must be {NewsValidation.MaxTitleLength} characters or fewer.", body);
    }

    [Fact]
    public async Task DeleteForeignKeyViolation_showsErrorMessageOnAdminList()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                3101,
                "Linked article",
                "linked-article",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        using var _ = AdminNewsDeleteError.UseForeignKeyViolationClassifier(_ => true);
        isolated.AdminNewsMutations!.DeleteException =
            new DbUpdateException("FK violation", new InvalidOperationException("blocked"));
        var client = CreateClient(AdminEmail, store);

        var deleteResponse = await PostActionAsync(client, "/admin/news/3101/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var listBody = await client.GetStringAsync(deleteResponse.Headers.Location!.OriginalString);
        Assert.Contains("could not be deleted", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("admin-status--error", listBody);
        Assert.Contains("Linked article", listBody);
    }

    [Fact]
    public async Task DeleteNotFound_showsErrorMessageOnAdminList()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                3102,
                "Missing on delete",
                "missing-on-delete",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        isolated.AdminNewsMutations!.DeleteException =
            new InvalidOperationException("News article 3102 was not found.");
        var client = CreateClient(AdminEmail, store);

        var deleteResponse = await PostActionAsync(client, "/admin/news/3102/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var listBody = await client.GetStringAsync(deleteResponse.Headers.Location!.OriginalString);
        Assert.Contains("News article 3102 was not found.", listBody);
        Assert.Contains("admin-status--error", listBody);
    }

    [Fact]
    public async Task Delete_continues_when_discovery_link_cleanup_fails()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                3103,
                "Cleanup failure article",
                "cleanup-failure",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        isolated.ConfigurableDiscovery!.ClearPromotedNewsLinksHandler = (_, _) =>
            throw new InvalidOperationException("Discovery tables unavailable.");
        var client = CreateClient(AdminEmail, store);

        var deleteResponse = await PostActionAsync(client, "/admin/news/3103/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var listBody = await client.GetStringAsync("/admin/news");
        Assert.DoesNotContain("Cleanup failure article", listBody);
    }

    [Fact]
    public async Task EditPage_loads_when_provenance_lookup_fails()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4101,
                "Provenance failure article",
                "provenance-failure",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        isolated.ConfigurableDiscovery!.GetCandidateByPromotedNewsIdHandler = (_, _) =>
            throw new InvalidOperationException("Discovery lookup failed.");
        var client = CreateClient(AdminEmail, store);

        var response = await client.GetAsync("/admin/news/4101/edit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Edit article", body);
        Assert.Contains("Provenance failure article", body);
        Assert.DoesNotContain("Discovery provenance", body);
    }

    [Fact]
    public async Task EditPost_validation_rerenders_when_provenance_lookup_fails()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4103,
                "Edit post provenance failure",
                "edit-post-provenance-failure",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        isolated.ConfigurableDiscovery!.GetCandidateByPromotedNewsIdHandler = (_, _) =>
            throw new InvalidOperationException("Discovery lookup failed.");
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/4103/edit",
            "/admin/news/4103",
            new Dictionary<string, string>
            {
                ["title"] = "",
                ["excerpt"] = "Excerpt",
                ["body"] = "Body",
                ["publishedAt"] = "2026-06-14"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required.", body);
        Assert.Contains("Edit post provenance failure", body);
        Assert.DoesNotContain("Discovery provenance", body);
    }

    [Fact]
    public async Task PreviewPage_loads_when_provenance_lookup_fails()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4102,
                "Preview provenance failure",
                "preview-provenance-failure",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        isolated.ConfigurableDiscovery!.GetCandidateByPromotedNewsIdHandler = (_, _) =>
            throw new InvalidOperationException("Discovery lookup failed.");
        var client = CreateClient(AdminEmail, store);

        var response = await client.GetAsync("/admin/news/4102/preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Preview provenance failure", body);
        Assert.DoesNotContain("Discovery provenance", body);
    }

    [Fact]
    public async Task Placeholder_asset_is_served()
    {
        var client = CreateClient();

        var response = await client.GetAsync(NewsArticleImage.PlaceholderPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var svg = await response.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svg);
    }

    [Fact]
    public async Task ArticleFormAndPreview_show_placeholder_when_no_image()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var newBody = await client.GetStringAsync("/admin/news/new");
        Assert.Contains(NewsArticleImage.PlaceholderPath, newBody);
        Assert.Contains("alt=\"No article image\"", newBody);

        var createResponse = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Placeholder preview article",
                ["excerpt"] = "No image set.",
                ["body"] = "Body without a photo.",
                ["publishedAt"] = "2026-06-14"
            });

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var editPath = createResponse.Headers.Location!.OriginalString;
        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);

        var editBody = await client.GetStringAsync(editPath);
        Assert.Contains(NewsArticleImage.PlaceholderPath, editBody);
        Assert.Contains("alt=\"No article image\"", editBody);

        var previewBody = await client.GetStringAsync($"/admin/news/{articleId}/preview");
        Assert.Contains(NewsArticleImage.PlaceholderPath, previewBody);
        Assert.Contains("alt=\"No article image\"", previewBody);
    }

    [Fact]
    public async Task ArticleFormAndPreview_show_article_image_when_blob_key_is_set()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4301,
                "Article with photo",
                "article-with-photo",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail,
                "editors/me/hero.webp")
        ]);
        var client = CreateClient(AdminEmail, store);

        var editBody = await client.GetStringAsync("/admin/news/4301/edit");
        Assert.Contains("/ugc/articles/editors/me/hero.webp", editBody);
        Assert.Contains("alt=\"Article image\"", editBody);
        Assert.DoesNotContain(NewsArticleImage.PlaceholderPath, editBody);

        var previewBody = await client.GetStringAsync("/admin/news/4301/preview");
        Assert.Contains("/ugc/articles/editors/me/hero.webp", previewBody);
        Assert.Contains("alt=\"Article with photo\"", previewBody);
        Assert.DoesNotContain(NewsArticleImage.PlaceholderPath, previewBody);
    }

    [Fact]
    public async Task ArticleForm_includes_card_image_upload_and_crop_controls()
    {
        var client = CreateClient(AdminEmail);

        var body = await client.GetStringAsync("/admin/news/new");

        Assert.Contains("enctype=\"multipart/form-data\"", body);
        Assert.Contains("data-busy-submit", body);
        Assert.Contains("data-busy-label=\"Saving…\"", body);
        Assert.Contains("name=\"articleImage\"", body);
        Assert.Contains("data-article-image-crop", body);
        Assert.Contains($"data-min-crop-width=\"{NewsArticleImageProcessor.MinCropWidth}\"", body);
        Assert.Contains($"data-min-crop-height=\"{NewsArticleImageProcessor.MinCropHeight}\"", body);
        Assert.Contains("Crop article image", body);
        Assert.Contains("Drag the photo to position it. Zoom to tighten the crop.", body);
        Assert.Contains("3:2 news-card frame", body);
        Assert.Contains("/js/admin/cropper.min.js", body);
        Assert.Contains("/css/admin/cropper.min.css", body);
        Assert.Contains("/js/admin/article-image-crop.js", body);
        Assert.Contains("Choose from gallery", body);
        Assert.Contains("data-gallery-picker-open", body);
        Assert.Contains("data-gallery-picker-dialog", body);
        Assert.Contains("/admin/news/gallery-picker", body);
        Assert.Contains("/js/admin/article-gallery-picker.js", body);
        Assert.Contains("Gallery originals are never modified.", body);
    }

    [Fact]
    public async Task GalleryPickerScript_dispatches_same_origin_original_for_crop()
    {
        var script = await CreateClient().GetStringAsync("/js/admin/article-gallery-picker.js");

        Assert.Contains("queenzone:article-gallery-crop", script);
        Assert.Contains("/admin/news/gallery-original/", script);
        Assert.DoesNotContain("preview.src = imageUrl", script);
        Assert.DoesNotContain("galleryIdInput.value = picId", script);
    }

    [Fact]
    public async Task ArticleImageCropScript_loads_gallery_original_as_blob_object_url()
    {
        var script = await CreateClient().GetStringAsync("/js/admin/article-image-crop.js");

        Assert.Contains("queenzone:article-gallery-crop", script);
        Assert.Contains("asBlobObjectUrl", script);
        Assert.Contains("encodeURI", script);
        Assert.Contains("createObjectURL", script);
        Assert.Contains("/admin/news/gallery-original/", script);
        Assert.DoesNotContain("stageImg.src = originalUrl", script);
        Assert.DoesNotContain("preview.src = originalUrl", script);
    }

    [Fact]
    public async Task ArticleImageCropZoom_is_disabled_outside_the_crop_dialog()
    {
        var client = CreateClient(AdminEmail);
        var editBody = await client.GetStringAsync("/admin/news/new");
        var script = await client.GetStringAsync("/js/admin/article-image-crop.js");

        Assert.Contains("data-article-image-zoom disabled", editBody);
        Assert.Contains("zoomInput.disabled = false", script);
        Assert.Contains("zoomInput.disabled = true", script);
        Assert.Contains("}, true);", script);
    }

    [Fact]
    public async Task ArticleImageCropScript_pans_photo_under_fixed_three_by_two_card()
    {
        var client = CreateClient(AdminEmail);
        var script = await client.GetStringAsync("/js/admin/article-image-crop.js");
        var css = await client.GetStringAsync("/css/site.css");

        Assert.Contains("viewMode: 3", script);
        Assert.Contains("dragMode: \"move\"", script);
        Assert.Contains("cropBoxMovable: false", script);
        Assert.Contains("cropBoxResizable: false", script);
        Assert.Contains("toggleDragModeOnDblclick: false", script);
        Assert.Contains("fillStageCropBox", script);
        Assert.Contains("setDragMode(\"move\")", script);
        Assert.DoesNotContain("viewMode: 2", script);
        Assert.DoesNotContain("cropBoxMovable: true", script);
        Assert.Contains("pointer-events: none", css);
        Assert.Contains(".admin-article-crop .cropper-crop-box", css);
        Assert.Contains(".admin-article-crop .cropper-face", css);
    }

    [Fact]
    public async Task GalleryPicker_requires_admin()
    {
        var anonymous = CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/admin/news/gallery-picker");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var stranger = CreateClient("stranger@example.com");
        var forbidden = await stranger.GetAsync("/admin/news/gallery-picker");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task GalleryPicker_lists_seed_photos_with_thumb_filename_date_and_category()
    {
        var client = CreateClient(AdminEmail);

        var body = await client.GetStringAsync("/admin/news/gallery-picker");

        Assert.Contains("img-101.jpg", body);
        Assert.Contains("1986-07-12", body);
        Assert.Contains("Brian May", body);
        Assert.Contains("https://cdn.queenzone.org/brian-may/img-101-t.jpg", body);
        Assert.Contains("data-gallery-pick", body);
        Assert.Contains("data-pic-id=\"101\"", body);
        Assert.Contains("data-original-url=\"/admin/news/gallery-original/101\"", body);
        Assert.Contains("name=\"catId\"", body);
        Assert.Contains("name=\"q\"", body);
        Assert.Contains("Soundcheck, Wembley", body);
    }

    [Fact]
    public async Task GalleryPicker_includes_photos_with_unbackfilled_legacy_dimensions()
    {
        var client = CreateClient(AdminEmail);

        var category = await client.GetStringAsync("/admin/news/gallery-picker?catId=9");

        Assert.Contains("img-101.jpg", category);
        Assert.Contains("img-102.jpg", category);
        Assert.Contains("img-103.jpg", category);
        Assert.DoesNotContain("No photos matched", category);
    }

    [Fact]
    public async Task GalleryPicker_filters_by_category_and_search()
    {
        var client = CreateClient(AdminEmail);

        var category = await client.GetStringAsync("/admin/news/gallery-picker?catId=9");
        Assert.Contains("img-101.jpg", category);
        Assert.Contains("img-102.jpg", category);
        Assert.DoesNotContain("img-201.jpg", category);

        var search = await client.GetStringAsync("/admin/news/gallery-picker?q=Wembley");
        Assert.Contains("img-102.jpg", search);
        Assert.Contains("img-201.jpg", search);
        Assert.DoesNotContain("img-101.jpg", search);
    }

    [Fact]
    public async Task GalleryPicker_paginates_seed_gallery()
    {
        var client = CreateClient(AdminEmail);

        var first = await client.GetStringAsync("/admin/news/gallery-picker");
        Assert.Contains("Page 1 of 2", first);
        Assert.Contains("pageNumber=2", first);
        Assert.Contains("img-202.jpg", first);
        Assert.DoesNotContain("img-203.jpg", first);

        var second = await client.GetStringAsync("/admin/news/gallery-picker?pageNumber=2");
        Assert.Contains("Page 2 of 2", second);
        Assert.Contains("img-203.jpg", second);
        Assert.DoesNotContain("img-202.jpg", second);
    }

    [Fact]
    public async Task CreateArticle_with_gallery_pick_and_crop_copies_into_ugc_articles()
    {
        var store = new SharedNewsStore();
        var appFactory = CreateFactory(store);
        var client = CreateClientFromFactory(appFactory, AdminEmail);

        var createResponse = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Library photo article",
                ["excerpt"] = "Uses a gallery pick.",
                ["body"] = "Copied into ugc-articles.",
                ["publishedAt"] = "2026-06-14",
                ["imageBlobKey"] = "editors/me/should-be-replaced.webp",
                ["imageGalleryPicId"] = "101",
                ["cropX"] = "0",
                ["cropY"] = "0",
                ["cropWidth"] = "600",
                ["cropHeight"] = "400"
            });

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);
        var article = IsolatedNews.GetArticle(articleId);
        Assert.NotNull(article);
        Assert.False(string.IsNullOrWhiteSpace(article.ImageBlobKey));
        Assert.False(NewsArticleImage.IsGalleryReference(article.ImageBlobKey));
        Assert.Null(article.ImageGalleryPicId);

        var blobs = appFactory.Services.GetRequiredService<IBlobUploadService>();
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, article.ImageBlobKey!));
        Assert.NotNull(await blobs.OpenReadAsync(
            BlobUploadContainers.Articles,
            UgcProxyPaths.ToThumbBlobName(article.ImageBlobKey!)));
        Assert.Null(await blobs.OpenReadAsync(BlobUploadContainers.Articles, "editors/me/should-be-replaced.webp"));

        var gallery = appFactory.Services.GetRequiredService<IGalleryPhotoBlobService>();
        await using var original = await gallery.OpenReadAsync("brian-may", "img-101.jpg");
        Assert.NotNull(original);

        var previewUrl = NewsArticleImage.ResolveDisplayUrl(article.ImageBlobKey, article.ImageGalleryPicId);
        var editBody = await client.GetStringAsync($"/admin/news/{articleId}/edit");
        Assert.Contains(previewUrl, editBody);
        Assert.Contains("alt=\"Article image\"", editBody);
        Assert.DoesNotContain(NewsArticleImage.PlaceholderPath, editBody);
        Assert.DoesNotContain("value=\"gallery:101\"", editBody);
    }

    [Fact]
    public async Task CreateArticle_rejects_gallery_pick_without_crop()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Uncropped library photo",
                ["excerpt"] = "Should stay a draft form.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14",
                ["imageGalleryPicId"] = "101"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Apply a 3:2 crop", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task CreateArticle_rejects_unknown_gallery_pic()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Missing library photo",
                ["excerpt"] = "Should stay a draft form.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14",
                ["imageGalleryPicId"] = "99999"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("That gallery photo was not found.", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task EditArticle_replacing_upload_with_gallery_pick_orphans_ugc_not_pic()
    {
        var store = new SharedNewsStore();
        var appFactory = CreateFactory(store);
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var image = await CreateCardPngAsync();

        var createResponse = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Swap to library",
                ["excerpt"] = "Starts as an upload.",
                ["body"] = "Then becomes a gallery pick.",
                ["publishedAt"] = "2026-06-14"
            },
            image,
            "hero.png",
            "image/png");

        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);
        var previous = IsolatedNews.GetArticle(articleId)!.ImageBlobKey!;
        var blobs = appFactory.Services.GetRequiredService<IBlobUploadService>();
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, previous));

        var saveResponse = await PostArticleAsync(
            client,
            $"/admin/news/{articleId}/edit",
            $"/admin/news/{articleId}",
            new Dictionary<string, string>
            {
                ["title"] = "Swap to library",
                ["excerpt"] = "Now a cropped gallery copy.",
                ["body"] = "Then becomes a gallery pick.",
                ["publishedAt"] = "2026-06-14",
                ["imageBlobKey"] = previous,
                ["imageGalleryPicId"] = "102",
                ["cropX"] = "0",
                ["cropY"] = "0",
                ["cropWidth"] = "600",
                ["cropHeight"] = "400"
            });

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var updated = IsolatedNews.GetArticle(articleId);
        Assert.NotNull(updated);
        Assert.False(NewsArticleImage.IsGalleryReference(updated.ImageBlobKey));
        Assert.Null(updated.ImageGalleryPicId);
        Assert.NotEqual(previous, updated.ImageBlobKey);
        Assert.Null(await blobs.OpenReadAsync(BlobUploadContainers.Articles, previous));
        Assert.Null(await blobs.OpenReadAsync(BlobUploadContainers.Articles, UgcProxyPaths.ToThumbBlobName(previous)));
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, updated.ImageBlobKey!));

        var gallery = appFactory.Services.GetRequiredService<IGalleryPhotoBlobService>();
        await using var original = await gallery.OpenReadAsync("brian-may", "img-102.jpg");
        Assert.NotNull(original);
    }

    [Fact]
    public async Task CreateArticle_rejects_invalid_gallery_crop()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Bad gallery crop",
                ["excerpt"] = "Should stay a draft form.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14",
                ["imageGalleryPicId"] = "101",
                ["cropX"] = "0",
                ["cropY"] = "0",
                ["cropWidth"] = "100",
                ["cropHeight"] = "100"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("selected crop is invalid", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task CreateArticle_rejects_too_small_gallery_crop()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Tiny gallery crop",
                ["excerpt"] = "Should stay a draft form.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14",
                ["imageGalleryPicId"] = "101",
                ["cropX"] = "0",
                ["cropY"] = "0",
                ["cropWidth"] = "300",
                ["cropHeight"] = "200"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("selected crop is too small", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task EditArticle_keeps_existing_gallery_pointer_without_new_crop()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4402,
                "Legacy gallery pick",
                "legacy-gallery-pick",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail,
                "gallery:101",
                101)
        ]);
        var client = CreateClient(AdminEmail, store);

        var saveResponse = await PostArticleAsync(
            client,
            "/admin/news/4402/edit",
            "/admin/news/4402",
            new Dictionary<string, string>
            {
                ["title"] = "Legacy gallery pick",
                ["excerpt"] = "Title-only edit.",
                ["body"] = "Body",
                ["publishedAt"] = "2026-06-01",
                ["imageBlobKey"] = "gallery:101",
                ["imageGalleryPicId"] = "101"
            });

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var updated = IsolatedNews.GetArticle(4402);
        Assert.NotNull(updated);
        Assert.Equal("gallery:101", updated.ImageBlobKey);
        Assert.Equal(101, updated.ImageGalleryPicId);
    }

    [Fact]
    public async Task GalleryOriginal_requires_admin_and_streams_seed_original()
    {
        var anonymous = CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/admin/news/gallery-original/101");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var stranger = CreateClient("stranger@example.com");
        var forbidden = await stranger.GetAsync("/admin/news/gallery-original/101");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var missing = await CreateClient(AdminEmail).GetAsync("/admin/news/gallery-original/99999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var client = CreateClient(AdminEmail);
        var response = await client.GetAsync("/admin/news/gallery-original/101");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task CreateArticle_with_valid_image_stores_ugc_articles_key_and_shows_preview()
    {
        var store = new SharedNewsStore();
        var appFactory = CreateFactory(store);
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var image = await CreateCardPngAsync();

        var createResponse = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Uploaded card image",
                ["excerpt"] = "Has a cropped photo.",
                ["body"] = "Body after upload.",
                ["publishedAt"] = "2026-06-14"
            },
            image,
            "hero.png",
            "image/png",
            new Dictionary<string, string>
            {
                ["cropX"] = "0",
                ["cropY"] = "0",
                ["cropWidth"] = "600",
                ["cropHeight"] = "400"
            });

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);
        var article = IsolatedNews.GetArticle(articleId);
        Assert.NotNull(article);
        Assert.False(string.IsNullOrWhiteSpace(article.ImageBlobKey));
        Assert.False(NewsArticleImage.IsGalleryReference(article.ImageBlobKey));
        Assert.Null(article.ImageGalleryPicId);

        var blobs = appFactory.Services.GetRequiredService<IBlobUploadService>();
        var stored = await blobs.OpenReadAsync(BlobUploadContainers.Articles, article.ImageBlobKey!);
        Assert.NotNull(stored);
        var thumb = await blobs.OpenReadAsync(
            BlobUploadContainers.Articles,
            UgcProxyPaths.ToThumbBlobName(article.ImageBlobKey!));
        Assert.NotNull(thumb);

        var previewUrl = NewsArticleImage.ResolveDisplayUrl(article.ImageBlobKey, article.ImageGalleryPicId);
        var editBody = await client.GetStringAsync($"/admin/news/{articleId}/edit");
        Assert.Contains(previewUrl, editBody);
        Assert.Contains("alt=\"Article image\"", editBody);
        Assert.DoesNotContain(NewsArticleImage.PlaceholderPath, editBody);

        var previewBody = await client.GetStringAsync($"/admin/news/{articleId}/preview");
        Assert.Contains(previewUrl, previewBody);
    }

    [Fact]
    public async Task CreateArticle_rejects_unsupported_image_type()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var response = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Bad image type",
                ["excerpt"] = "Should stay a draft form.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14"
            },
            "not-an-image"u8.ToArray(),
            "notes.txt",
            "text/plain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("JPEG, PNG, or WebP", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task CreateArticle_rejects_oversized_image()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);
        var bytes = new byte[NewsArticleImageProcessor.MaxUploadBytes + 1];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;

        var response = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Huge image",
                ["excerpt"] = "Should be rejected.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14"
            },
            bytes,
            "huge.jpg",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("bytes or smaller", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task CreateArticle_rejects_in_bounds_card_crop_below_minimum()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);
        var image = await CreateCardPngAsync(1800, 600);

        var response = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Tight crop",
                ["excerpt"] = "Should be rejected.",
                ["body"] = "Plain text body.",
                ["publishedAt"] = "2026-06-14"
            },
            image,
            "wide.png",
            "image/png",
            new Dictionary<string, string>
            {
                ["cropX"] = "0",
                ["cropY"] = "0",
                ["cropWidth"] = "300",
                ["cropHeight"] = "200"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("selected crop is too small", body);
        Assert.Empty(IsolatedNews.GetAllArticles());
    }

    [Fact]
    public async Task EditArticle_replacing_uploaded_image_orphans_old_ugc_articles_blobs()
    {
        var store = new SharedNewsStore();
        var appFactory = CreateFactory(store);
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var first = await CreateCardPngAsync();

        var createResponse = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Replace my photo",
                ["excerpt"] = "First image.",
                ["body"] = "Body for replace.",
                ["publishedAt"] = "2026-06-14"
            },
            first,
            "first.png",
            "image/png");

        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);
        var previous = IsolatedNews.GetArticle(articleId)!.ImageBlobKey!;
        var blobs = appFactory.Services.GetRequiredService<IBlobUploadService>();
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, previous));

        var second = await CreateCardPngAsync(640, 420);
        var saveResponse = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            $"/admin/news/{articleId}/edit",
            $"/admin/news/{articleId}",
            new Dictionary<string, string>
            {
                ["title"] = "Replace my photo",
                ["excerpt"] = "Second image.",
                ["body"] = "Body for replace.",
                ["publishedAt"] = "2026-06-14",
                ["imageBlobKey"] = previous
            },
            second,
            "second.png",
            "image/png");

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var updated = IsolatedNews.GetArticle(articleId);
        Assert.NotNull(updated);
        Assert.NotEqual(previous, updated.ImageBlobKey);
        Assert.Null(await blobs.OpenReadAsync(BlobUploadContainers.Articles, previous));
        Assert.Null(await blobs.OpenReadAsync(BlobUploadContainers.Articles, UgcProxyPaths.ToThumbBlobName(previous)));
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, updated.ImageBlobKey!));

        var editBody = await client.GetStringAsync($"/admin/news/{articleId}/edit");
        Assert.Contains(NewsArticleImage.ResolveDisplayUrl(updated.ImageBlobKey, updated.ImageGalleryPicId), editBody);
    }

    [Fact]
    public async Task EditArticle_replacing_gallery_reference_stores_ugc_articles_key()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4401,
                "Gallery pick",
                "gallery-pick",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail,
                "gallery:3120",
                3120)
        ]);
        var client = CreateClient(AdminEmail, store);
        var image = await CreateCardPngAsync();

        var saveResponse = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/news/4401/edit",
            "/admin/news/4401",
            new Dictionary<string, string>
            {
                ["title"] = "Gallery pick",
                ["excerpt"] = "Excerpt",
                ["body"] = "Body",
                ["publishedAt"] = "2026-06-01",
                ["imageBlobKey"] = "gallery:3120",
                ["imageGalleryPicId"] = "3120"
            },
            image,
            "hero.png",
            "image/png");

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var updated = IsolatedNews.GetArticle(4401);
        Assert.NotNull(updated);
        Assert.False(NewsArticleImage.IsGalleryReference(updated.ImageBlobKey));
        Assert.Null(updated.ImageGalleryPicId);
        Assert.StartsWith("editors/", updated.ImageBlobKey);
        Assert.Contains(
            NewsArticleImage.ResolveDisplayUrl(updated.ImageBlobKey, updated.ImageGalleryPicId),
            await client.GetStringAsync("/admin/news/4401/edit"));
    }

    [Fact]
    public async Task GetDeleteUrl_redirectsToAdminList()
    {
        var client = CreateClient(AdminEmail, new SharedNewsStore());

        var response = await client.GetAsync("/admin/news/3001/delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/news", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AdminNewsList_is_paginated()
    {
        var store = new SharedNewsStore(CreateSeedArticles(55));
        var client = CreateClient(AdminEmail, store);

        var firstPage = await client.GetStringAsync("/admin/news");
        Assert.Contains("Showing 1&ndash;50 of 55 articles", firstPage);
        Assert.Contains("Article 055", firstPage);
        Assert.Contains("Article 006", firstPage);
        Assert.DoesNotContain("Article 005", firstPage);
        Assert.Contains("href=\"/admin/news/page/2\"", firstPage);

        var secondPage = await client.GetStringAsync("/admin/news/page/2");
        Assert.Contains("Showing 51&ndash;55 of 55 articles", secondPage);
        Assert.Contains("Article 005", secondPage);
        Assert.Contains("Article 001", secondPage);
        Assert.DoesNotContain("Article 006", secondPage);
        Assert.Contains("href=\"/admin/news\"", secondPage);

        var invalidPage = await client.GetAsync("/admin/news/page/99");
        Assert.Equal(HttpStatusCode.Redirect, invalidPage.StatusCode);
        Assert.Equal("/admin/news/page/2", invalidPage.Headers.Location!.OriginalString);

        var zeroPage = await client.GetAsync("/admin/news/page/0");
        Assert.Equal(HttpStatusCode.Redirect, zeroPage.StatusCode);
        Assert.Equal("/admin/news", zeroPage.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Delete_missingArticle_redirectsWithMessage()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                3002,
                "Still here",
                "still-here",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        var client = CreateClient(AdminEmail, store);

        var deleteResponse = await PostActionAsync(client, "/admin/news/6999/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var listBody = await client.GetStringAsync(deleteResponse.Headers.Location!.OriginalString);
        Assert.Contains("News article 6999 was not found.", listBody);
        Assert.Contains("admin-status--error", listBody);
        Assert.Contains("Still here", listBody);
    }

    [Fact]
    public async Task AuthorizedAdminCanDeleteArticle()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                3001,
                "Delete me",
                "delete-me",
                "Delete excerpt",
                "Delete body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);

        var client = CreateClient(AdminEmail, store);

        var deleteResponse = await PostActionAsync(client, "/admin/news/3001/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/admin/news");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Delete me", listBody);
    }

    [Fact]
    public async Task EditDraft_can_save_when_publish_action_is_on_page()
    {
        var store = new SharedNewsStore(
        [
            new AdminNewsArticle(
                4205,
                "Draft to edit",
                "draft-to-edit",
                "Draft excerpt",
                "Draft body",
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AdminEmail)
        ]);
        var client = CreateClient(AdminEmail, store);

        var saveResponse = await PostArticleAsync(
            client,
            "/admin/news/4205/edit",
            "/admin/news/4205",
            new Dictionary<string, string>
            {
                ["title"] = "Draft to edit updated",
                ["excerpt"] = "Updated excerpt",
                ["body"] = "Updated body",
                ["publishedAt"] = "2026-06-15"
            });

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        Assert.Equal("/admin/news/4205/edit", saveResponse.Headers.Location!.OriginalString);

        var editBody = await client.GetStringAsync("/admin/news/4205/edit");
        Assert.Contains("Draft to edit updated", editBody);
        Assert.Contains("Updated body", editBody);
        Assert.Contains("Publish", editBody);
    }

    [Fact]
    public async Task PublishAndEditActionsAreAudited()
    {
        var store = new SharedNewsStore();
        var appFactory = CreateFactory(store);
        var client = CreateClientFromFactory(appFactory, AdminEmail);

        var createResponse = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Audited article",
                ["excerpt"] = "Audit excerpt",
                ["body"] = "Audit body",
                ["publishedAt"] = "2026-06-14"
            });

        var editPath = createResponse.Headers.Location!.OriginalString;
        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);

        await using var scope = appFactory.Services.CreateAsyncScope();
        var auditRepository = scope.ServiceProvider.GetRequiredService<INewsAuditRepository>();
        var createAudit = await auditRepository.GetByNewsIdAsync(articleId);
        Assert.Contains(createAudit, entry => entry.Action == "create");

        await PostActionAsync(client, $"/admin/news/{articleId}/publish");

        var publishAudit = await auditRepository.GetByNewsIdAsync(articleId);
        Assert.Contains(publishAudit, entry => entry.Action == "publish");
        Assert.Contains(publishAudit, entry => entry.ActorEmail == AdminEmail);
    }

    private SharedNewsStore IsolatedNews => isolated.AdminNews!;

    private WebApplicationFactory<Program> CreateFactory(SharedNewsStore store)
    {
        IsolatedNews.Seed(store.GetAllArticles());
        return isolated;
    }

    private HttpClient CreateClient(
        string? email = null,
        SharedNewsStore? store = null,
        Action<IServiceCollection>? configureServices = null,
        INewsDiscoveryRepository? discoveryRepository = null)
    {
        _ = configureServices;
        _ = discoveryRepository;
        var appFactory = store is null ? factory : CreateFactory(store);
        return CreateClientFromFactory(appFactory, email);
    }

    private HttpClient CreateClient(string? email, SharedNewsStore store) =>
        CreateClient(email, store, null, null);

    private static HttpClient CreateClientFromFactory(WebApplicationFactory<Program> appFactory, string? email) =>
        AdminHttpTestHelpers.CreateClient(appFactory, email);

    private static Task<HttpResponseMessage> PostArticleAsync(
        HttpClient client,
        string formPath,
        string postPath,
        Dictionary<string, string> fields) =>
        AdminHttpTestHelpers.PostArticleAsync(client, formPath, postPath, fields);

    private static Task<HttpResponseMessage> PostActionAsync(HttpClient client, string actionPath) =>
        AdminHttpTestHelpers.PostNewsActionAsync(client, actionPath);

    private static async Task<byte[]> CreateCardPngAsync(int width = 600, int height = 400)
    {
        using var image = new Image<Rgba32>(width, height);
        await using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static IEnumerable<AdminNewsArticle> CreateSeedArticles(int count) =>
        Enumerable.Range(1, count).Select(index => new AdminNewsArticle(
            index,
            $"Article {index:D3}",
            $"article-{index}",
            "Excerpt",
            "Body",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            null,
            false,
            null,
            null,
            null));
}
