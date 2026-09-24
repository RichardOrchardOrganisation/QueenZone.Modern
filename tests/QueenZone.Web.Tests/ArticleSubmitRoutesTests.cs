using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.Articles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed partial class ArticleSubmitRoutesTests : IClassFixture<ExternalCookieWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;

    public ArticleSubmitRoutesTests(ExternalCookieWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetSubmitArticle_RedirectsAnonymousUser()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/submit/article");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task GetAdminArticles_Returns401_ForAnonymousUser()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/admin/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminArticles_Returns200_ForAdminUser()
    {
        var client = CreateAdminClient(AdminEmail);

        var response = await client.GetAsync("/admin/articles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminArticles_shows_unified_editor_submission_and_archive_sections()
    {
        var body = await CreateAdminClient(AdminEmail).GetStringAsync("/admin/articles");
        Assert.Contains("Create article", body);
        Assert.Contains("Editorial articles and edits", body);
        Assert.Contains("Member submissions", body);
        Assert.Contains("Published archive", body);
    }

    [Fact]
    public async Task Admin_editor_reuses_rich_text_gallery_and_three_by_two_crop()
    {
        var body = await CreateAdminClient(AdminEmail).GetStringAsync("/admin/articles/editor");
        Assert.Contains("class=\"qz-rte\"", body);
        Assert.Contains("Choose from gallery", body);
        Assert.Contains("data-aspect-width=\"3\"", body);
        Assert.Contains("data-aspect-height=\"2\"", body);
        Assert.Contains("data-article-image-dialog", body);
        Assert.Contains("Drag the photo to position it. Zoom to tighten the crop.", body);
        Assert.Contains("/js/admin/article-image-crop.js", body);
        Assert.Contains("data-container=\"ugc-articles\"", body);
        Assert.Contains("action=\"/admin/articles/editor\"", body);
        Assert.Contains("Author", body);
        Assert.Contains("Category", body);
        Assert.Contains("Tags", body);
    }

    [Fact]
    public void Articles_editor_form_value_length_limit_is_aligned_with_page_cap()
    {
        var options = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Features.FormOptions>>().Value;
        Assert.Equal(16 * 1024 * 1024, options.ValueLengthLimit);
    }

    [Fact]
    public async Task Legacy_editor_form_posts_to_action_without_legacyId_query()
    {
        var body = await CreateAdminClient(AdminEmail).GetStringAsync("/admin/articles/editor?legacyId=101");
        Assert.Contains("action=\"/admin/articles/editor\"", body);
        Assert.DoesNotContain("action=\"/admin/articles/editor?legacyId=", body);
        Assert.Contains("name=\"Form.LegacyArticleId\"", body);
        Assert.Contains("value=\"101\"", body);
    }

    [Fact]
    public async Task GetEditor_missing_legacy_returns_404()
    {
        var client = CreateAdminClient(AdminEmail);
        var response = await client.GetAsync("/admin/articles/editor?legacyId=999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_save_cropped_image_on_legacy_editor_url()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var formPath = "/admin/articles/editor?legacyId=101";
        var response = await PostEditorialImageAsync(
            admin,
            formPath,
            "/admin/articles/editor",
            new()
            {
                ["Form.LegacyArticleId"] = "101",
                ["Form.Title"] = "Cropped archive feature",
                ["Form.Slug"] = "cropped-archive-feature",
                ["Form.Excerpt"] = "Updated archive excerpt with image.",
                ["Form.Body"] = "<p>Updated archive article body with a card image.</p>",
                ["Form.AuthorName"] = "Archive Editor",
                ["Form.Category"] = "Features",
                ["Form.PublishedAt"] = "2026-08-31",
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var editPath = response.Headers.Location!.OriginalString;
        Assert.Matches("^/admin/articles/editor/[0-9a-fA-F-]{36}$", editPath);

        var editorial = isolated.Services.GetRequiredService<IEditorialArticleRepository>();
        var saved = await editorial.GetAsync(Guid.Parse(editPath.Split('/').Last()));
        Assert.NotNull(saved);
        Assert.Equal(101, saved.LegacyArticleId);
        Assert.False(string.IsNullOrWhiteSpace(saved.ImageBlobKey));
        Assert.Equal(EditorialArticleStatus.Draft, saved.Status);

        var blobs = isolated.Services.GetRequiredService<IBlobUploadService>();
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, saved.ImageBlobKey!));

        var beforePublish = await isolated.CreateClient().GetStringAsync("/articles/101/inside-the-making-of-bohemian-rhapsody");
        Assert.DoesNotContain("Updated archive article body with a card image", beforePublish);
    }

    [Fact]
    public async Task Admin_can_save_cropped_image_on_create_and_guid_editor()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var create = await PostEditorialImageAsync(
            admin,
            "/admin/articles/editor",
            "/admin/articles/editor",
            new()
            {
                ["Form.Title"] = "New featured article",
                ["Form.Slug"] = "new-featured-article",
                ["Form.Excerpt"] = "A new editorial with a card image.",
                ["Form.Body"] = "<p>Create path body with an attached card image.</p>",
                ["Form.AuthorName"] = "QueenZone Editorial",
                ["Form.Category"] = "Feature",
            });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var editPath = create.Headers.Location!.OriginalString;
        Assert.StartsWith("/admin/articles/editor/", editPath);
        var editHtml = await admin.GetStringAsync(editPath);
        Assert.Contains($"action=\"{editPath}\"", editHtml);

        var save = await PostEditorialImageAsync(
            admin,
            editPath,
            editPath,
            new()
            {
                ["Form.Title"] = "New featured article",
                ["Form.Slug"] = "new-featured-article",
                ["Form.Excerpt"] = "Edited excerpt after the first image save.",
                ["Form.Body"] = "<p>Guid editor path body with a replacement card image.</p>",
                ["Form.AuthorName"] = "QueenZone Editorial",
                ["Form.Category"] = "Feature",
                ["Form.PublishedAt"] = "2026-09-01",
            });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal(editPath, save.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Admin_save_draft_invalid_image_returns_200_with_field_errors()
    {
        var client = CreateAdminClient(AdminEmail);
        var response = await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            "/admin/articles/editor",
            "/admin/articles/editor",
            new()
            {
                ["Form.Title"] = "Broken image draft",
                ["Form.Excerpt"] = "Should stay on the form.",
                ["Form.Body"] = "<p>Body is valid but the image is not.</p>",
                ["Form.AuthorName"] = "QueenZone Editorial",
                ["Form.Category"] = "Feature",
                ["Form.PublishedAt"] = "2026-09-01",
            },
            "not-an-image"u8.ToArray(),
            "notes.txt",
            "text/plain",
            fileFieldName: "Form.ArticleImage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin-errors", body);
        Assert.DoesNotContain("Broken image draft", await CreateAdminClient(AdminEmail).GetStringAsync("/admin/articles"));
    }

    [Fact]
    public async Task Admin_can_save_large_legacy_body_and_cropped_image_on_form_action()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var largeBody = LargeLegacyBody();
        var fields = LegacyImageFields(largeBody);

        var response = await PostEditorialImageAsync(
            admin,
            "/admin/articles/editor?legacyId=101",
            "/admin/articles/editor",
            fields);

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Unexpected status {response.StatusCode}");
        Assert.NotEqual("/error/400", response.Headers.Location?.OriginalString);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            var editPath = response.Headers.Location!.OriginalString;
            Assert.Matches("^/admin/articles/editor/[0-9a-fA-F-]{36}$", editPath);
            var editorial = isolated.Services.GetRequiredService<IEditorialArticleRepository>();
            var saved = await editorial.GetAsync(Guid.Parse(editPath.Split('/').Last()));
            Assert.NotNull(saved);
            Assert.Equal(101, saved.LegacyArticleId);
            Assert.False(string.IsNullOrWhiteSpace(saved.ImageBlobKey));
            Assert.Equal(EditorialArticleStatus.Draft, saved.Status);
        }
        else
        {
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("admin-errors", html);
            Assert.DoesNotContain("Something went wrong", html);
        }
    }

    [Fact]
    public async Task Admin_save_large_legacy_body_to_legacyId_query_is_not_400()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var response = await PostEditorialImageAsync(
            admin,
            "/admin/articles/editor?legacyId=101",
            "/admin/articles/editor?legacyId=101",
            LegacyImageFields(LargeLegacyBody()));

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Unexpected status {response.StatusCode}");
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual("/error/400", response.Headers.Location?.OriginalString);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("admin-errors", html);
            Assert.DoesNotContain("Something went wrong", html);
        }
    }

    [Fact]
    public async Task Admin_save_draft_without_antiforgery_returns_200_with_in_page_error()
    {
        var client = CreateAdminClient(AdminEmail);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("101"), "Form.LegacyArticleId");
        content.Add(new StringContent("Missing token archive"), "Form.Title");
        content.Add(new StringContent("Updated archive excerpt."), "Form.Excerpt");
        content.Add(new StringContent("<p>Body with a card image.</p>"), "Form.Body");
        content.Add(new StringContent("Archive Editor"), "Form.AuthorName");
        content.Add(new StringContent("Features"), "Form.Category");
        var file = new ByteArrayContent(await CreateCardPngAsync());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "Form.ArticleImage", "hero.png");

        var response = await client.PostAsync("/admin/articles/editor", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin-errors", html);
        Assert.Contains(EditorialArticleEditorRequestGuardFilter.AntiforgeryError, html);
        Assert.DoesNotContain("Something went wrong", html);
    }

    [Fact]
    public async Task Admin_save_draft_bind_failure_returns_200_with_in_page_error()
    {
        var client = CreateAdminClient(AdminEmail);
        var response = await PostEditorialImageAsync(
            client,
            "/admin/articles/editor?legacyId=101",
            "/admin/articles/editor",
            new()
            {
                ["Form.LegacyArticleId"] = "101",
                ["Form.Title"] = "Bound poorly",
                ["Form.Excerpt"] = "Should stay on the form.",
                ["Form.Body"] = "<p>Valid body with an unreadable publication date.</p>",
                ["Form.AuthorName"] = "Archive Editor",
                ["Form.Category"] = "Features",
                ["Form.PublishedAt"] = "not-a-date",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("admin-errors", html);
        Assert.DoesNotContain("Something went wrong", html);
        Assert.DoesNotContain("Bound poorly", await CreateAdminClient(AdminEmail).GetStringAsync("/admin/articles"));
    }

    [Fact]
    public async Task Admin_can_create_preview_publish_and_read_editorial_article()
    {
        var client = CreateAdminClient(AdminEmail);
        var response = await AdminHttpTestHelpers.PostArticleAsync(client, "/admin/articles/editor", "/admin/articles/editor", new()
        {
            ["Form.Title"] = "A night at the opera",
            ["Form.Slug"] = "a-night-at-the-opera-editorial",
            ["Form.Excerpt"] = "A detailed editorial feature.",
            ["Form.Body"] = "<p>This is the complete article body.</p>",
            ["Form.AuthorName"] = "Richard Orchard",
            ["Form.Category"] = "Features",
            ["Form.Tags"] = "queen,albums",
            ["Form.PublishedAt"] = "2026-09-01",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var editPath = response.Headers.Location!.OriginalString;
        var id = Guid.Parse(editPath.Split('/').Last());

        var preview = await client.GetAsync($"/admin/articles/editor/{id}/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("A night at the opera", await preview.Content.ReadAsStringAsync());

        var editBody = await client.GetStringAsync(editPath);
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(editBody);
        var publish = await client.PostAsync($"/admin/articles/editor/{id}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["status"] = EditorialArticleStatus.Published,
        }));
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);

        var publicBody = await factory.CreateClient().GetStringAsync("/articles/a-night-at-the-opera-editorial");
        Assert.Contains("A night at the opera", publicBody);
        Assert.Contains("Richard Orchard", publicBody);
        Assert.Contains("This is the complete article body", publicBody);
        var homeBody = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("A night at the opera", homeBody);
        Assert.Contains("/articles/a-night-at-the-opera-editorial", homeBody);

        token = AdminHttpTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync(editPath));
        await client.PostAsync($"/admin/articles/editor/{id}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["status"] = EditorialArticleStatus.Unpublished,
        }));
        var afterUnpublishSave = await AdminHttpTestHelpers.PostArticleAsync(client, editPath, editPath, new()
        {
            ["Form.Title"] = "Working copy after unpublish",
            ["Form.Slug"] = "working-copy-after-unpublish",
            ["Form.Excerpt"] = "A detailed editorial feature.",
            ["Form.Body"] = "<p>This is the complete article body.</p>",
            ["Form.AuthorName"] = "Richard Orchard",
            ["Form.Category"] = "Features",
            ["Form.Tags"] = "queen,albums",
            ["Form.PublishedAt"] = "2026-09-01",
        });
        Assert.Equal(HttpStatusCode.Redirect, afterUnpublishSave.StatusCode);
        var hiddenAfterSave = await factory.CreateClient().GetAsync("/articles/a-night-at-the-opera-editorial");
        var hiddenWorkingCopy = await factory.CreateClient().GetAsync("/articles/working-copy-after-unpublish");
        Assert.Equal(HttpStatusCode.NotFound, hiddenAfterSave.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenWorkingCopy.StatusCode);
    }

    [Fact]
    public async Task Admin_can_draft_and_publish_legacy_article_without_changing_its_id()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var formPath = "/admin/articles/editor?legacyId=101";
        var response = await AdminHttpTestHelpers.PostArticleAsync(admin, formPath, "/admin/articles/editor", new()
        {
            ["Form.LegacyArticleId"] = "101",
            ["Form.Title"] = "Edited Bohemian Rhapsody feature",
            ["Form.Slug"] = "edited-bohemian-rhapsody-feature",
            ["Form.Excerpt"] = "Updated archive excerpt.",
            ["Form.Body"] = "<p>Updated archive article body.</p>",
            ["Form.AuthorName"] = "Archive Editor",
            ["Form.Category"] = "Features",
            ["Form.Source"] = "https://example.test/archive-source",
            ["Form.PublishedAt"] = "2026-08-31",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var editPath = response.Headers.Location!.OriginalString;
        var id = Guid.Parse(editPath.Split('/').Last());

        var beforePublish = await isolated.CreateClient().GetStringAsync("/articles/101/inside-the-making-of-bohemian-rhapsody");
        Assert.DoesNotContain("Updated archive article body", beforePublish);

        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(await admin.GetStringAsync(editPath));
        await admin.PostAsync($"/admin/articles/editor/{id}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["status"] = EditorialArticleStatus.Published,
        }));

        var afterPublish = await isolated.CreateClient().GetStringAsync("/articles/101/edited-bohemian-rhapsody-feature");
        Assert.Contains("Updated archive article body", afterPublish);
        Assert.Contains("https://example.test/archive-source", afterPublish);
        Assert.Contains("Archive Editor", afterPublish);

        var searchStore = isolated.Services.GetRequiredService<SharedSearchIndexStore>();
        Assert.Contains(
            searchStore.GetAll(),
            document => document.SourceKey == "legacy-article:101"
                && document.Title == "Edited Bohemian Rhapsody feature");

        token = AdminHttpTestHelpers.ExtractAntiforgeryToken(await admin.GetStringAsync(editPath));
        var unpublish = await admin.PostAsync($"/admin/articles/editor/{id}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["status"] = EditorialArticleStatus.Unpublished,
        }));
        Assert.Equal(HttpStatusCode.Redirect, unpublish.StatusCode);
        var hidden = await isolated.CreateClient().GetAsync("/articles/101/edited-bohemian-rhapsody-feature");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain(searchStore.GetAll(), document => document.SourceKey == "legacy-article:101");
    }

    [Fact]
    public async Task Admin_prepare_edit_and_publish_member_submission()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var member = await CreateSignedInMemberClientAsync(
            email: "article-prepare@example.com",
            displayName: "Prepare Author",
            subject: "google-article-prepare",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            },
            sourceFactory: isolated);
        var submissionId = await SubmitArticleAsync(member, "Prepared member feature", "prepared-member-feature", isolated);

        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var review = await admin.GetStringAsync($"/admin/articles/{submissionId:D}");
        Assert.Contains("Edit and prepare article", review);

        var prepare = await admin.PostAsync(
            $"/admin/articles/{submissionId:D}?handler=Prepare",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(review),
            }));
        Assert.Equal(HttpStatusCode.Redirect, prepare.StatusCode);
        var editPath = prepare.Headers.Location!.OriginalString;
        Assert.StartsWith("/admin/articles/editor/", editPath);

        var save = await AdminHttpTestHelpers.PostArticleAsync(admin, editPath, editPath, new()
        {
            ["Form.Title"] = "Prepared member feature",
            ["Form.Slug"] = "prepared-member-feature",
            ["Form.Excerpt"] = "Edited excerpt from the unified editor.",
            ["Form.Body"] = "<p>" + MinBody() + "</p>",
            ["Form.AuthorName"] = "Prepare Author",
            ["Form.Category"] = "Feature",
            ["Form.Tags"] = "prepared",
            ["Form.PublishedAt"] = "2026-09-01",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var editorId = Guid.Parse(editPath.Split('/').Last());
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(await admin.GetStringAsync(editPath));
        var publish = await admin.PostAsync($"/admin/articles/editor/{editorId}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["status"] = EditorialArticleStatus.Published,
        }));
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);

        var publicBody = await isolated.CreateClient().GetStringAsync("/articles/prepared-member-feature");
        Assert.Contains("Prepared member feature", publicBody);
        Assert.Contains("Prepare Author", publicBody);
        Assert.Contains("Edited excerpt from the unified editor.", publicBody);
    }

    [Fact]
    public async Task Admin_prepare_with_stale_antiforgery_token_redisplays_review_instead_of_400()
    {
        using var isolated = factory.WithWebHostBuilder(_ => { });
        var member = await CreateSignedInMemberClientAsync(
            email: "article-prepare-stale@example.com",
            displayName: "Stale Prepare Author",
            subject: "google-article-prepare-stale",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            },
            sourceFactory: isolated);
        var submissionId = await SubmitArticleAsync(member, "Stale prepare feature", "stale-prepare-feature", isolated);

        var admin = AdminHttpTestHelpers.CreateClient(isolated, AdminEmail);
        var review = await admin.GetStringAsync($"/admin/articles/{submissionId:D}");
        Assert.Contains("Edit and prepare article", review);

        var prepare = await admin.PostAsync(
            $"/admin/articles/{submissionId:D}?handler=Prepare",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = "not-a-real-token",
            }));

        Assert.Equal(HttpStatusCode.Redirect, prepare.StatusCode);
        Assert.Equal($"/admin/articles/{submissionId:D}", prepare.Headers.Location!.OriginalString);

        var redisplay = await admin.GetStringAsync($"/admin/articles/{submissionId:D}");
        Assert.Contains("could not be verified", redisplay);
    }

    [Fact]
    public async Task PostAutosave_Returns403_WithoutAntiforgery()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Test",
            ["Body"] = "Hello",
        });

        var response = await client.PostAsync("/submit/article/autosave", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Member_CanSaveDraftSubmitAndSeeConfirmation()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "article-submit@example.com",
            displayName: "Article Author",
            subject: "google-article-submit",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/article");
        using var saveContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Title"] = "Wembley retrospective",
            ["Excerpt"] = "A look back at the 1986 show",
            ["Body"] = MinBody(),
            ["Tags"] = "Wembley, Live",
            ["action"] = "save",
        });

        var saveResponse = await client.PostAsync("/submit/article", saveContent);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var formPageAfterSave = await saveResponse.Content.ReadAsStringAsync();
        Assert.Contains("Draft saved.", formPageAfterSave);

        var draftId = ExtractDraftId(formPageAfterSave);

        using var submitContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPageAfterSave),
            ["DraftId"] = draftId,
            ["Title"] = "Wembley retrospective",
            ["Excerpt"] = "A look back at the 1986 show",
            ["Body"] = MinBody(),
            ["Tags"] = "Wembley, Live",
            ["action"] = "submit",
        });

        var submitResponse = await client.PostAsync("/submit/article", submitContent);
        Assert.Equal(HttpStatusCode.Redirect, submitResponse.StatusCode);
        Assert.StartsWith("/submit/article/confirmation/", submitResponse.Headers.Location!.OriginalString);

        var confirmation = await client.GetStringAsync(submitResponse.Headers.Location!.OriginalString);
        Assert.Contains("Wembley retrospective", confirmation);
        Assert.Contains(ArticleSubmissionStatus.Submitted, confirmation);

        var mySubmissions = await client.GetStringAsync("/account/my-submissions?tab=articles");
        Assert.Contains("Wembley retrospective", mySubmissions);
        Assert.Contains(ArticleSubmissionStatus.Submitted, mySubmissions);
    }

    [Fact]
    public async Task PostSubmit_WithShortBody_ShowsValidationError()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "article-short@example.com",
            displayName: "Short Body",
            subject: "google-article-short",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/article");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Title"] = "Too short",
            ["Body"] = "Not enough text.",
            ["action"] = "submit",
        });

        var response = await client.PostAsync("/submit/article", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("300", body);
    }

    [Fact]
    public async Task PostAutosave_ReturnsDraftId_ForSignedInMember()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "article-autosave@example.com",
            displayName: "Autosave Author",
            subject: "google-article-autosave",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var formPage = await client.GetStringAsync("/submit/article");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Title"] = "Autosaved draft",
            ["Body"] = "Draft body text.",
        });

        var response = await client.PostAsync("/submit/article/autosave", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("draftId", body);
    }

    [Fact]
    public async Task Admin_CanReviewApprovePublishAndReject()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            email: "article-admin-flow@example.com",
            displayName: "Admin Flow Author",
            subject: "google-article-admin-flow",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var publishId = await SubmitArticleAsync(memberClient, "Publish story", "publish-story");
        var rejectId = await SubmitArticleAsync(memberClient, "Reject story", "reject-story");
        var reviseId = await SubmitArticleAsync(memberClient, "Revise story", "revise-story");

        var admin = CreateAdminClient(AdminEmail);

        var queue = await admin.GetStringAsync("/admin/articles");
        Assert.Contains("Publish story", queue);
        Assert.Contains("Admin Flow Author", queue);

        var detail = await admin.GetStringAsync($"/admin/articles/{publishId}");
        Assert.Contains("Publish story", detail);

        await PostAdminActionAsync(admin, publishId, new Dictionary<string, string>
        {
            ["submitAction"] = "underreview",
            ["ReviewNotes"] = "Starting review",
        });

        var repository = factory.Services.GetRequiredService<IArticleSubmissionRepository>();
        Assert.Equal(ArticleSubmissionStatus.UnderReview, (await repository.GetByIdAsync(publishId))!.Status);

        await PostAdminActionAsync(admin, publishId, new Dictionary<string, string>
        {
            ["submitAction"] = "approve",
            ["Slug"] = "publish-story",
            ["ReviewNotes"] = "Approved",
        });
        Assert.Equal(ArticleSubmissionStatus.ApprovedForPublishing, (await repository.GetByIdAsync(publishId))!.Status);

        var publishResponse = await PostAdminActionAsync(admin, publishId, new Dictionary<string, string>
        {
            ["submitAction"] = "publish",
            ["Slug"] = "publish-story",
        });
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);
        Assert.Equal(ArticleSubmissionStatus.Published, (await repository.GetByIdAsync(publishId))!.Status);

        await PostAdminActionAsync(admin, reviseId, new Dictionary<string, string>
        {
            ["submitAction"] = "revise",
            ["RejectionReason"] = "Needs more detail",
            ["ReviewNotes"] = "Expand the outro",
        });

        var rejectWithoutReason = await PostAdminActionAsync(admin, rejectId, new Dictionary<string, string>
        {
            ["submitAction"] = "reject",
        });
        Assert.Equal(HttpStatusCode.Redirect, rejectWithoutReason.StatusCode);

        await PostAdminActionAsync(admin, rejectId, new Dictionary<string, string>
        {
            ["submitAction"] = "reject",
            ["RejectionReason"] = "Off topic",
        });

        Assert.Equal(ArticleSubmissionStatus.RequiresRevision, (await repository.GetByIdAsync(reviseId))!.Status);
        Assert.Equal(ArticleSubmissionStatus.Rejected, (await repository.GetByIdAsync(rejectId))!.Status);

        var articlesIndex = await factory.CreateClient().GetStringAsync("/articles");
        Assert.Contains("Publish story", articlesIndex);
        Assert.Contains("Community article", articlesIndex);

        var publicDetail = await factory.CreateClient().GetStringAsync("/articles/publish-story");
        Assert.Contains("Publish story", publicDetail);
    }

    [Fact]
    public async Task Admin_ApproveAndPublish_StillSucceeds_WhenSearchIndexFails()
    {
        var failingFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISearchIndexService>();
                services.AddSingleton<ISearchIndexService>(new ThrowingSearchIndexService());
            });
        });

        var memberClient = await CreateSignedInMemberClientAsync(
            email: "article-search-failure@example.com",
            displayName: "Search Failure Author",
            subject: "google-article-search-failure",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            },
            sourceFactory: failingFactory);

        var publishId = await SubmitArticleAsync(
            memberClient, "Search failure story", "search-failure-story", sourceFactory: failingFactory);

        var admin = CreateAdminClient(AdminEmail, sourceFactory: failingFactory);

        await PostAdminActionAsync(admin, publishId, new Dictionary<string, string>
        {
            ["submitAction"] = "approve",
            ["Slug"] = "search-failure-story",
            ["ReviewNotes"] = "Approved",
        });

        var publishResponse = await PostAdminActionAsync(admin, publishId, new Dictionary<string, string>
        {
            ["submitAction"] = "publish",
            ["Slug"] = "search-failure-story",
        });

        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);
        var repository = failingFactory.Services.GetRequiredService<IArticleSubmissionRepository>();
        Assert.Equal(ArticleSubmissionStatus.Published, (await repository.GetByIdAsync(publishId))!.Status);
    }

    private sealed class ThrowingSearchIndexService : ISearchIndexService
    {
        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");
    }

    [Fact]
    public async Task GetEditDraft_LoadsExistingDraftForMember()
    {
        var client = await CreateSignedInMemberClientAsync(
            email: "article-edit@example.com",
            displayName: "Edit Author",
            subject: "google-article-edit",
            options: new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                AllowAutoRedirect = false,
            });

        var draftId = await SaveDraftAsync(client, "Editable draft", "Draft body content.");

        var editPage = await client.GetStringAsync($"/submit/article?handler=Edit&id={draftId:D}");
        Assert.Contains("Editable draft", editPage);
        Assert.Contains("Edit draft", editPage);
    }

    [Fact]
    public async Task ArticlesIndexRendersSuccessfully()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/articles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Articles", body);
    }

    [Fact]
    public async Task CommunityDetail_Returns404_WhenSlugNotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/articles/nonexistent-community-slug-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminDetail_Returns404_ForUnknownSubmission()
    {
        var admin = CreateAdminClient(AdminEmail);
        var response = await admin.GetAsync($"/admin/articles/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SubmitArticleAsync(
        HttpClient client,
        string title,
        string slug,
        WebApplicationFactory<Program>? sourceFactory = null)
    {
        var draftId = await SaveDraftAsync(client, title, MinBody());
        var formPage = await client.GetStringAsync("/submit/article");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["DraftId"] = draftId.ToString("D"),
            ["Title"] = title,
            ["Body"] = MinBody(),
            ["action"] = "submit",
        });

        var response = await client.PostAsync("/submit/article", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var repository = (sourceFactory ?? factory).Services.GetRequiredService<IArticleSubmissionRepository>();
        var pending = await repository.GetPendingAsync(1, 50);
        return pending.Single(item => item.Title == title).Id;
    }

    private async Task<Guid> SaveDraftAsync(HttpClient client, string title, string body)
    {
        var formPage = await client.GetStringAsync("/submit/article");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(formPage),
            ["Title"] = title,
            ["Body"] = body,
            ["action"] = "save",
        });

        var response = await client.PostAsync("/submit/article", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        return Guid.Parse(ExtractDraftId(html));
    }

    private async Task<HttpResponseMessage> PostAdminActionAsync(
        HttpClient client,
        Guid id,
        Dictionary<string, string> fields)
    {
        var detail = await client.GetStringAsync($"/admin/articles/{id:D}");
        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
            ["Slug"] = fields.GetValueOrDefault("Slug") ?? "article-slug",
        };
        return await client.PostAsync($"/admin/articles/{id:D}/action", new FormUrlEncodedContent(form));
    }

    private HttpClient CreateAdminClient(string? email = null, WebApplicationFactory<Program>? sourceFactory = null)
    {
        var client = (sourceFactory ?? factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, email);
        }

        return client;
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(
        string email,
        string displayName,
        string subject,
        WebApplicationFactoryClientOptions? options = null,
        WebApplicationFactory<Program>? sourceFactory = null)
    {
        var client = (sourceFactory ?? factory).CreateClient(options ?? new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callbackResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callbackResponse.StatusCode}");

        return client;
    }

    private static async Task<HttpResponseMessage> PostEditorialImageAsync(
        HttpClient client,
        string formPath,
        string postPath,
        Dictionary<string, string> fields)
    {
        return await AdminHttpTestHelpers.PostArticleMultipartAsync(
            client,
            formPath,
            postPath,
            fields,
            await CreateCardPngAsync(),
            "hero.png",
            "image/png",
            new Dictionary<string, string>
            {
                ["Form.CropX"] = "0",
                ["Form.CropY"] = "0",
                ["Form.CropWidth"] = "600",
                ["Form.CropHeight"] = "400",
            },
            fileFieldName: "Form.ArticleImage");
    }

    private static Dictionary<string, string> LegacyImageFields(string body) => new()
    {
        ["Form.LegacyArticleId"] = "101",
        ["Form.Title"] = "Cropped archive feature",
        ["Form.Slug"] = "cropped-archive-feature",
        ["Form.Excerpt"] = "Updated archive excerpt with image.",
        ["Form.Body"] = body,
        ["Form.AuthorName"] = "Archive Editor",
        ["Form.Category"] = "Features",
        ["Form.PublishedAt"] = "2026-08-31",
    };

    private static string LargeLegacyBody() =>
        "<p>" + new string('x', (4 * 1024 * 1024) + 8192) + "</p>";

    private static async Task<byte[]> CreateCardPngAsync(int width = 600, int height = 400)
    {
        using var image = new Image<Rgba32>(width, height);
        await using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static string MinBody() => new('x', EfArticleSubmissionRepository.MinBodyVisibleChars);

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    private static string ExtractDraftId(string html)
    {
        var match = DraftIdRegex().Match(html);
        Assert.True(match.Success, "Draft ID was not found in the form.");
        return match.Groups["id"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();

    [GeneratedRegex("""name="DraftId"[^>]*value="(?<id>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex DraftIdRegex();
}
