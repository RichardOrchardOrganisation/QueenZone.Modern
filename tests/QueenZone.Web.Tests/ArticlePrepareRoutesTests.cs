using System.Net;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Tests;

public sealed partial class ArticleSubmitRoutesTests
{
    [Fact]
    public async Task Admin_prepare_posts_every_rendered_form_field_and_reuses_the_editor()
    {
        var id = await CreatePrepareSubmissionAsync("browser-prepare");
        var admin = CreateAdminClient(AdminEmail);
        var response = await PostRenderedPrepareAsync(admin, id);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var editPath = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/admin/articles/editor/", editPath);
        var repeated = await PostRenderedPrepareAsync(admin, id);
        Assert.Equal(editPath, repeated.Headers.Location!.OriginalString);
        var articles = factory.Services.GetRequiredService<IEditorialArticleRepository>();
        Assert.Single((await articles.GetAllAsync()), article => article.SourceSubmissionId == id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task Admin_prepare_rejects_missing_or_invalid_token_without_creating_a_draft(string? token)
    {
        var id = await CreatePrepareSubmissionAsync("invalid-prepare");
        var admin = CreateAdminClient(AdminEmail);
        await admin.GetStringAsync($"/admin/articles/{id}");
        var fields = new List<KeyValuePair<string, string>>();
        if (token is not null) fields.Add(new("__RequestVerificationToken", token));
        var response = await admin.PostAsync($"/admin/articles/{id}?handler=Prepare", new FormUrlEncodedContent(fields));
        Assert.Equal($"/admin/articles/{id}", response.Headers.Location!.OriginalString);
        Assert.Contains("This action could not be verified", await admin.GetStringAsync($"/admin/articles/{id}"));
        var articles = factory.Services.GetRequiredService<IEditorialArticleRepository>();
        Assert.DoesNotContain(await articles.GetAllAsync(), article => article.SourceSubmissionId == id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-admin@example.test")]
    public async Task Prepare_and_editor_routes_require_admin_access(string? email)
    {
        var id = await CreatePrepareSubmissionAsync("unauthorized-prepare");
        var client = CreateAdminClient(email);
        var expected = email is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await client.GetAsync($"/admin/articles/{id}")).StatusCode);
        Assert.Equal(expected, (await client.PostAsync($"/admin/articles/{id}?handler=Prepare", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/admin/articles/editor")).StatusCode);
        var articles = factory.Services.GetRequiredService<IEditorialArticleRepository>();
        Assert.DoesNotContain(await articles.GetAllAsync(), article => article.SourceSubmissionId == id);
    }

    [Fact]
    public async Task Published_submission_keeps_origin_and_live_content_until_editor_photo_is_published()
    {
        var id = await CreatePrepareSubmissionAsync("published-prepare");
        var submissions = factory.Services.GetRequiredService<IArticleSubmissionRepository>();
        await submissions.UpdateStatusAsync(id, ArticleSubmissionStatus.ApprovedForPublishing,
            AdminEmail, "Original review notes", null, new("published-prepare", "Original excerpt", "queen"));
        var original = await submissions.UpdateStatusAsync(id, ArticleSubmissionStatus.Published,
            AdminEmail, "Original review notes", null);
        Assert.NotNull(original);
        var publicArticles = factory.Services.GetRequiredService<IArticleRepository>();
        var originalLive = await publicArticles.GetBySlugAsync(original.Slug);
        Assert.NotNull(originalLive);
        var publicPath = $"/articles/{original.Slug}";
        var publicClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync(publicPath)).StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var prepare = await PostRenderedPrepareAsync(admin, id);
        var editPath = prepare.Headers.Location!.OriginalString;
        var editorialId = Guid.Parse(editPath.Split('/').Last());
        var articles = factory.Services.GetRequiredService<IEditorialArticleRepository>();
        var prepared = await articles.GetAsync(editorialId);
        Assert.NotNull(prepared);
        Assert.Equal(original, await submissions.GetByIdAsync(id));
        Assert.Equal(original.Id, prepared.SourceSubmissionId);
        Assert.Equal(original.Body, prepared.Body);
        Assert.Equal(original.AuthorDisplayName, prepared.AuthorName);
        Assert.Equal(original.PublishedAt, prepared.PublishedAt);
        Assert.Equal(original.Slug, prepared.Slug);
        var repeat = await PostRenderedPrepareAsync(admin, id);
        Assert.Equal(editPath, repeat.Headers.Location!.OriginalString);

        var editorHtml = await admin.GetStringAsync(editPath);
        var sourceField = TestHtmlAssertions.SingleElement(editorHtml, "input[name='Form.SourceSubmissionId']");
        Assert.Equal(id.ToString(), sourceField.GetAttribute("value"));
        Assert.Single(TestHtmlAssertions.Select(editorHtml, ".qz-rte"));
        Assert.Contains("Choose from gallery", editorHtml);
        Assert.Single(TestHtmlAssertions.Select(editorHtml, "input[name='Form.ArticleImage']"));
        var editedBody = "<p>Edited published submission with a new cover photo.</p>";
        var save = await PostEditorialImageAsync(admin, editPath, editPath, new()
        {
            ["Form.Id"] = editorialId.ToString(),
            ["Form.SourceSubmissionId"] = sourceField.GetAttribute("value")!,
            ["Form.Title"] = original.Title,
            ["Form.Slug"] = original.Slug,
            ["Form.Excerpt"] = "Edited excerpt",
            ["Form.Body"] = editedBody,
            ["Form.AuthorName"] = prepared.AuthorName,
            ["Form.Category"] = prepared.Category,
            ["Form.Tags"] = prepared.Tags ?? string.Empty,
            ["Form.PublishedAt"] = prepared.PublishedAt.UtcDateTime.ToString("O"),
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var draft = await articles.GetAsync(editorialId);
        Assert.NotNull(draft);
        Assert.NotNull(draft.ImageBlobKey);
        Assert.Equal(editedBody, draft.Body);
        Assert.Equal(id, draft.SourceSubmissionId);
        Assert.Equal(original, await submissions.GetByIdAsync(id));
        Assert.Equal(originalLive, await publicArticles.GetBySlugAsync(original.Slug));
        var beforePublish = await publicClient.GetStringAsync(publicPath);
        Assert.DoesNotContain("Edited published submission", beforePublish);
        Assert.DoesNotContain(draft.ImageBlobKey, beforePublish);

        var token = ExtractAntiforgeryToken(await admin.GetStringAsync(editPath));
        var publish = await admin.PostAsync($"{editPath}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["status"] = EditorialArticleStatus.Published,
        }));
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        var live = await publicArticles.GetBySlugAsync(original.Slug);
        Assert.NotNull(live);
        Assert.Equal(editedBody, live.Body);
        Assert.Equal(draft.ImageBlobKey, live.CoverImageBlobPath);
        Assert.Equal(original.AuthorDisplayName, live.AuthorDisplayName);
        var afterPublish = await publicClient.GetStringAsync(publicPath);
        Assert.Contains("Edited published submission", afterPublish);
        Assert.Contains(UgcProxyPaths.GetPath(BlobUploadContainers.Articles, draft.ImageBlobKey), afterPublish);
        var hero = TestHtmlAssertions.SingleElement(afterPublish, ".qz-article-hero");
        Assert.Equal(UgcProxyPaths.GetPath(BlobUploadContainers.Articles, draft.ImageBlobKey),
            Assert.Single(hero.QuerySelectorAll("img")).GetAttribute("src"));
        Assert.Empty(TestHtmlAssertions.Select(afterPublish, ".qz-article-cover"));
        var preservedSubmission = await submissions.GetByIdAsync(id);
        Assert.NotNull(preservedSubmission);
        Assert.Equal(original.AuthorMemberId, preservedSubmission.AuthorMemberId);
        Assert.Equal(original.Body, preservedSubmission.Body);
        Assert.Equal(original.Slug, preservedSubmission.Slug);
        Assert.Equal(ArticleSubmissionStatus.Published, preservedSubmission.Status);
    }

    private async Task<Guid> CreatePrepareSubmissionAsync(string slug)
    {
        slug = $"{slug}-{Guid.NewGuid():N}";
        var member = await CreateSignedInMemberClientAsync($"{slug}@example.test", "Prepare Author", slug,
            new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        return await SubmitArticleAsync(member, $"Prepare feature {slug}", slug);
    }

    private static async Task<HttpResponseMessage> PostRenderedPrepareAsync(HttpClient client, Guid id)
    {
        var html = await client.GetStringAsync($"/admin/articles/{id}");
        var form = Assert.IsAssignableFrom<IHtmlFormElement>(
            TestHtmlAssertions.SingleElement(html, "form[action$='?handler=Prepare']"));
        // Keep duplicate names, just as a browser does, rather than collapsing fields into a dictionary.
        var fields = form.Elements.OfType<IHtmlInputElement>()
            .Where(input => !input.IsDisabled && !string.IsNullOrEmpty(input.Name))
            .Select(input => new KeyValuePair<string, string>(input.Name!, input.Value));
        return await client.PostAsync(form.Action, new FormUrlEncodedContent(fields));
    }
}
