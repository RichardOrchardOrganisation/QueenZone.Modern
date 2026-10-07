using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.Photos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class AdminPhotoRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public AdminPhotoRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AdminPhotosIndex_RendersSeedPhotos()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var response = await client.GetAsync("/admin/photos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Brian in action with his guitar", body);
        Assert.Contains("href=\"/admin/photos/new\"", body);
    }

    [Fact]
    public async Task AdminPhotos_CreateHideAndShow_RoundTrip()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var newPage = await client.GetStringAsync("/admin/photos/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(newPage);

        await using var imageStream = await CreateJpegAsync(320, 240);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), AdminPhotosPageModel.AntiforgeryTokenFieldName);
        content.Add(new StringContent("9"), "catId");
        content.Add(new StringContent("Route upload photo"), "title");
        content.Add(new StringContent("2024"), "year");
        content.Add(new StringContent("true"), "isVisible");
        content.Add(new StringContent(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm")), "dateTime");

        var fileContent = new StreamContent(imageStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "route-upload.jpg");

        var createResponse = await client.PostAsync("/admin/photos/create", content);
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var editPath = createResponse.Headers.Location!.OriginalString;
        Assert.StartsWith("/admin/photos/", editPath, StringComparison.Ordinal);

        var picId = int.Parse(editPath.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

        var hideResponse = await PostActionAsync(client, $"/admin/photos/{picId}/hide");
        Assert.Equal(HttpStatusCode.Redirect, hideResponse.StatusCode);

        var publicResponse = await client.GetAsync($"/photography/brian-may/{picId}");
        Assert.Equal(HttpStatusCode.NotFound, publicResponse.StatusCode);

        var showResponse = await PostActionAsync(client, $"/admin/photos/{picId}/show");
        Assert.Equal(HttpStatusCode.Redirect, showResponse.StatusCode);

        var editPage = await client.GetAsync($"/admin/photos/{picId}");
        Assert.Equal(HttpStatusCode.OK, editPage.StatusCode);
        var editBody = await editPage.Content.ReadAsStringAsync();
        Assert.Contains("Route upload photo", editBody);
        Assert.Contains("Hide from gallery", editBody);
        Assert.Contains("admin-photo-preview", editBody);
        Assert.Contains("Full image: 320 &times; 240", editBody);
        Assert.DoesNotContain("object-fit: cover", editBody);
    }

    [Fact]
    public async Task AdminPhotos_RegenerateThumb_SucceedsForUploadedPhoto()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var newPage = await client.GetStringAsync("/admin/photos/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(newPage);

        await using var imageStream = await CreateJpegAsync(400, 400);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), AdminPhotosPageModel.AntiforgeryTokenFieldName);
        content.Add(new StringContent("9"), "catId");
        content.Add(new StringContent("Thumb regen photo"), "title");
        content.Add(new StringContent("true"), "isVisible");
        var fileContent = new StreamContent(imageStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "thumb-regen.jpg");

        var createResponse = await client.PostAsync("/admin/photos/create", content);
        var picId = int.Parse(
            createResponse.Headers.Location!.OriginalString.Split('/')[^1],
            System.Globalization.CultureInfo.InvariantCulture);

        var regenResponse = await PostActionAsync(client, $"/admin/photos/{picId}/regeneratethumb");
        Assert.Equal(HttpStatusCode.Redirect, regenResponse.StatusCode);

        var editBody = await client.GetStringAsync($"/admin/photos/{picId}");
        Assert.Contains("Thumbnail regenerated", editBody);
    }

    [Fact]
    public async Task AdminPhotos_HardDelete_RemovesPhotoFromAdminEdit()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var newPage = await client.GetStringAsync("/admin/photos/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(newPage);

        await using var imageStream = await CreateJpegAsync(280, 200);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), AdminPhotosPageModel.AntiforgeryTokenFieldName);
        content.Add(new StringContent("9"), "catId");
        content.Add(new StringContent("Hard delete photo"), "title");
        content.Add(new StringContent("true"), "isVisible");
        var fileContent = new StreamContent(imageStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "hard-delete.jpg");

        var createResponse = await client.PostAsync("/admin/photos/create", content);
        var picId = int.Parse(
            createResponse.Headers.Location!.OriginalString.Split('/')[^1],
            System.Globalization.CultureInfo.InvariantCulture);

        // The CSP blocks inline handlers, so the confirmation must be the styled dialog.
        var editBeforeDelete = await client.GetStringAsync($"/admin/photos/{picId}");
        Assert.DoesNotContain("onsubmit=", editBeforeDelete, StringComparison.OrdinalIgnoreCase);
        var deleteForm = Regex.Match(
            editBeforeDelete,
            $"<form[^>]*action=\"/admin/photos/{picId}/delete\"[^>]*>(?<body>.*?)</form>",
            RegexOptions.Singleline);
        Assert.True(deleteForm.Success);
        var dialogId = Regex.Match(
            deleteForm.Groups["body"].Value,
            "<button type=\"button\"[^>]*data-confirm-dialog-open=\"(?<id>[^\"]+)\"[^>]*>Hard delete</button>");
        Assert.True(dialogId.Success);
        var dialog = Regex.Match(
            deleteForm.Groups["body"].Value,
            $"<dialog class=\"qz-dialog\" id=\"{dialogId.Groups["id"].Value}\" data-confirm-dialog>(?<body>.*?)</dialog>",
            RegexOptions.Singleline);
        Assert.True(dialog.Success);
        Assert.Contains("Hard-delete this photo?", dialog.Groups["body"].Value);
        Assert.Contains("data-confirm-dialog-cancel", dialog.Groups["body"].Value);
        Assert.Matches("<button type=\"submit\"[^>]*>Hard delete</button>", dialog.Groups["body"].Value);

        var deleteResponse = await PostActionAsync(client, $"/admin/photos/{picId}/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);
        Assert.Equal("/admin/photos", deleteResponse.Headers.Location!.OriginalString);

        var editPage = await client.GetAsync($"/admin/photos/{picId}");
        Assert.Equal(HttpStatusCode.NotFound, editPage.StatusCode);

        var indexBody = await client.GetStringAsync("/admin/photos");
        Assert.Contains("associated gallery blobs", indexBody);
    }

    [Fact]
    public async Task AdminPhotos_CreateRejectsMissingFileAndTitle()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var page = await client.GetStringAsync("/admin/photos/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(page);
        var missingFile = await client.PostAsync("/admin/photos/create", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [AdminPhotosPageModel.AntiforgeryTokenFieldName] = token,
                ["catId"] = "9",
                ["title"] = "Photo",
            }));
        Assert.Equal(HttpStatusCode.Redirect, missingFile.StatusCode);
        Assert.Contains("A photo file is required.", await client.GetStringAsync("/admin/photos/new"));

        await using var image = await CreateJpegAsync(80, 80);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), AdminPhotosPageModel.AntiforgeryTokenFieldName);
        content.Add(new StringContent("9"), "catId");
        content.Add(new StringContent(" "), "title");
        content.Add(new StreamContent(image), "file", "photo.jpg");
        var missingTitle = await client.PostAsync("/admin/photos/create", content);
        Assert.Equal(HttpStatusCode.Redirect, missingTitle.StatusCode);
        Assert.Contains("Title is required.", await client.GetStringAsync("/admin/photos/new"));
    }

    [Fact]
    public async Task AdminPhotos_EditAndActionsReportMissingPhoto()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/photos/999999")).StatusCode);

        var page = await client.GetStringAsync("/admin/photos/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(page);
        var fields = new Dictionary<string, string>
        {
            [AdminPhotosPageModel.AntiforgeryTokenFieldName] = token,
            ["title"] = "Changed",
            ["year"] = "2024",
            ["catId"] = "9",
            ["dateTime"] = "2024-01-01",
        };
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync("/admin/photos/999999/save", new FormUrlEncodedContent(fields))).StatusCode);

        var regenerate = await client.PostAsync("/admin/photos/999999/regeneratethumb",
            new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, regenerate.StatusCode);
        var delete = await client.PostAsync("/admin/photos/999999/delete",
            new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal("/admin/photos/999999", delete.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AdminPhotos_SaveReplacesImageAndReportsValidationErrors()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var newPage = await client.GetStringAsync("/admin/photos/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(newPage);
        await using var original = await CreateJpegAsync(80, 80);
        using var create = new MultipartFormDataContent();
        create.Add(new StringContent(token), AdminPhotosPageModel.AntiforgeryTokenFieldName);
        create.Add(new StringContent("9"), "catId");
        create.Add(new StringContent("Before edit"), "title");
        create.Add(new StreamContent(original), "file", "original.jpg");
        var created = await client.PostAsync("/admin/photos/create", create);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var path = created.Headers.Location!.OriginalString;
        var editPage = await client.GetStringAsync(path);
        token = AdminHttpTestHelpers.ExtractAntiforgeryToken(editPage);

        var fields = new Dictionary<string, string>
        {
            [AdminPhotosPageModel.AntiforgeryTokenFieldName] = token,
            ["title"] = " ",
            ["catId"] = "9",
            ["year"] = "2024",
            ["dateTime"] = "2024-01-01",
        };
        var invalid = await client.PostAsync($"{path}/save", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        Assert.Contains("Title is required.", await client.GetStringAsync(path));

        await using var replacement = await CreateJpegAsync(120, 90);
        using var save = new MultipartFormDataContent();
        save.Add(new StringContent(token), AdminPhotosPageModel.AntiforgeryTokenFieldName);
        save.Add(new StringContent("After edit"), "title");
        save.Add(new StringContent("9"), "catId");
        save.Add(new StringContent("2024"), "year");
        save.Add(new StringContent("2024-01-01"), "dateTime");
        save.Add(new StreamContent(replacement), "replaceFile", "replacement.jpg");
        var saved = await client.PostAsync($"{path}/save", save);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var updated = await client.GetStringAsync(path);
        Assert.Contains("After edit", updated);
        Assert.Contains("Full image: 120 &times; 90", updated);
        Assert.Contains("Photo updated.", updated);
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string actionPath)
    {
        var editPath = string.Join('/', actionPath.Split('/')[..^1]);
        var editPage = await client.GetStringAsync(editPath);
        var fields = new Dictionary<string, string>
        {
            [AdminPhotosPageModel.AntiforgeryTokenFieldName] = AdminHttpTestHelpers.ExtractAntiforgeryToken(editPage),
        };
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(fields));
    }

    private static async Task<MemoryStream> CreateJpegAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 80 });
        stream.Position = 0;
        return stream;
    }
}
