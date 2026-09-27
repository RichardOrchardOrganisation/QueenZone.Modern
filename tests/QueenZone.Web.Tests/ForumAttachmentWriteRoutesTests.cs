using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class ForumAttachmentWriteRoutesTests : IClassFixture<WebHostVariantCache>
{
    private readonly VariantWebApplicationFactory factory;

    public ForumAttachmentWriteRoutesTests(WebHostVariantCache variants)
    {
        factory = variants.Get(WebHostVariants.TestingForumAttachmentMemoryBlob);
    }

    [Fact]
    public async Task ReplyPost_WithValidAttachment_PersistsForumPostAttachmentRow()
    {
        var memberId = Guid.NewGuid();
        var client = CreateMemberClient(factory, memberId);
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("<p>Reply with attachment</p>"), "Body");
        var fileBytes = Encoding.UTF8.GetBytes("%PDF-1.4 test");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "Attachments", "setlist.pdf");

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("#post-", response.Headers.Location!.OriginalString);

        using var scope = factory.Services.CreateScope();
        var attachments = scope.ServiceProvider.GetRequiredService<IForumAttachmentRepository>();
        // In-memory write creates post ids from 2_000_000 upward; locate by filename.
        if (attachments is InMemoryForumAttachmentRepository memory)
        {
            var all = memory.GetAll();
            Assert.Contains(all, item => item.OriginalFileName == "setlist.pdf");
        }
        else
        {
            Assert.Fail("Expected in-memory attachment repository in Testing environment.");
        }
    }

    [Fact]
    public async Task ReplyPost_WithDisallowedType_RerendersWithError()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("<p>Reply</p>"), "Body");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("MZ executable"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-msdownload");
        content.Add(fileContent, "Attachments", "payload.exe");

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("not allowed", body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateMemberClient(
        WebApplicationFactory<Program> sourceFactory,
        Guid memberId,
        string displayName = "Forum Fan")
    {
        var client = sourceFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, displayName);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var input = Regex.Match(
            html,
            """<input[^>]*name="__RequestVerificationToken"[^>]*>""",
            RegexOptions.IgnoreCase);
        Assert.True(input.Success, "Antiforgery token input was not found in the form.");

        var value = Regex.Match(input.Value, "value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, "Antiforgery token value was not found in the form.");
        return value.Groups["token"].Value;
    }
}
