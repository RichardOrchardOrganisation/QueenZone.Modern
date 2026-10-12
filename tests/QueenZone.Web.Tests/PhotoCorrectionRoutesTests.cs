using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed partial class PhotoCorrectionRoutesTests : IClassFixture<ExternalCookieWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly ExternalCookieWebApplicationFactory factory;

    public PhotoCorrectionRoutesTests(ExternalCookieWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_form_redirects_signed_out_visitors_to_login()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/submit/photo-correction/201");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_form_is_not_found_for_unknown_and_hidden_photos()
    {
        var member = await SignInAsync("pc-hidden@example.com", "Hidden Fan", "google-pc-hidden");
        var photos = factory.Services.GetRequiredService<IAdminPhotoRepository>();
        await photos.SetVisibilityAsync(204, false, AdminEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/submit/photo-correction/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/submit/photo-correction/204")).StatusCode);
        var post = await PostProposalAsync(member, 204, Fields("Hyde Park 1976"), tokenFromPicId: 201);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task Post_creates_a_pending_proposal_that_never_reaches_the_public_page()
    {
        var member = await SignInAsync("pc-submit@example.com", "Submit Fan", "google-pc-submit");

        var response = await PostProposalAsync(member, 201, Fields("Live Aid, Wembley Stadium, 13 July 1985"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/submit/photo-correction/confirmation/", location);
        var confirmation = await member.GetStringAsync(location);
        Assert.Contains("Pending review", confirmation);
        Assert.Contains("Live Aid, Wembley Stadium, 13 July 1985", confirmation);

        var anonymous = factory.CreateClient();
        var publicPage = await anonymous.GetStringAsync("/photography/queen/201");
        Assert.DoesNotContain("Wembley Stadium, 13 July 1985", publicPage);
        Assert.Contains("Live Aid, Wembley", publicPage);
    }

    [Fact]
    public async Task Post_rejects_script_evidence_links_and_double_submits_cleanly()
    {
        var member = await SignInAsync("pc-evidence@example.com", "Evidence Fan", "google-pc-evidence");
        var bad = Fields("Hyde Park, 1976");
        bad["EvidenceUrl"] = "javascript:alert(1)";

        var rejected = await PostProposalAsync(member, 203, bad);

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("http:// or https://", await rejected.Content.ReadAsStringAsync());

        var fields = Fields("Hyde Park, London 1976");
        var first = await PostProposalAsync(member, 203, fields);
        var retry = await PostProposalAsync(member, 203, fields);
        Assert.Equal(first.Headers.Location, retry.Headers.Location);

        var different = Fields("Hyde Park Free Concert");
        var duplicate = await PostProposalAsync(member, 203, different);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already have an open proposal", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Confirmation_is_visible_only_to_the_submitter()
    {
        var owner = await SignInAsync("pc-owner@example.com", "Owner Fan", "google-pc-owner");
        var other = await SignInAsync("pc-other@example.com", "Other Fan", "google-pc-other");
        var response = await PostProposalAsync(owner, 102, Fields("Soundcheck at Wembley"));
        var location = response.Headers.Location!.OriginalString;

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(location)).StatusCode);
        var signedOut = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await signedOut.GetAsync(location)).StatusCode);
    }

    [Fact]
    public async Task Admin_queue_requires_an_admin()
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/photo-corrections")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAdminClient("stranger@example.com").GetAsync("/admin/photo-corrections")).StatusCode);
        Assert.Contains("Photo corrections", await CreateAdminClient(AdminEmail).GetStringAsync("/admin/photo-corrections"));
    }

    [Fact]
    public async Task Admin_approval_updates_the_public_caption_and_revert_restores_it()
    {
        var member = await SignInAsync("pc-approve@example.com", "Approve Fan", "google-pc-approve");
        var id = await SubmitAsync(member, 301, "Freddie at the piano, Wembley 1986");
        var admin = CreateAdminClient(AdminEmail);
        var anonymous = factory.CreateClient();

        var queue = await admin.GetStringAsync("/admin/photo-corrections");
        Assert.Contains("Approve Fan", queue);
        var detail = await admin.GetStringAsync($"/admin/photo-corrections/{id:D}");
        Assert.Contains("Confidence", detail);

        var approve = await PostAdminAsync(admin, id, "approve", new() { ["reviewNotes"] = "Checked programme" }, detail);
        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
        Assert.Contains(
            "Freddie at the piano, Wembley 1986",
            await anonymous.GetStringAsync("/photography/freddie-mercury/301"));

        var repository = factory.Services.GetRequiredService<IPhotoCorrectionRepository>();
        var approved = await repository.GetByIdAsync(id);
        Assert.Equal(PhotoCorrectionStatus.Approved, approved!.Status);
        Assert.Equal("Freddie at the piano", approved.AppliedFromValue);
        Assert.Contains(await repository.GetAuditAsync(id), entry => entry.Action == "Approved");

        var revertDetail = await admin.GetStringAsync($"/admin/photo-corrections/{id:D}");
        await PostAdminAsync(admin, id, "revert", new() { ["reviewNotes"] = "Wrong tour" }, revertDetail);

        var afterRevert = await anonymous.GetStringAsync("/photography/freddie-mercury/301");
        Assert.DoesNotContain("Wembley 1986", afterRevert);
        Assert.Contains("Freddie at the piano", afterRevert);
        Assert.Equal(PhotoCorrectionStatus.Reverted, (await repository.GetByIdAsync(id))!.Status);
    }

    [Fact]
    public async Task Admin_approval_is_blocked_when_the_caption_changed_since_the_proposal()
    {
        var member = await SignInAsync("pc-stale@example.com", "Stale Fan", "google-pc-stale");
        var id = await SubmitAsync(member, 302, "Freddie on stage at Live Aid");
        var photos = factory.Services.GetRequiredService<IAdminPhotoRepository>();
        var photo = (await photos.GetByIdAsync(302))!;
        await photos.UpdateAsync(
            302,
            new AdminPhotoUpdateRequest("Freddie on stage (edited)", photo.Keywords, photo.Year, photo.DateTime, photo.CatId),
            AdminEmail);
        var admin = CreateAdminClient(AdminEmail);

        var detail = await admin.GetStringAsync($"/admin/photo-corrections/{id:D}");
        Assert.Contains("not what the member saw", detail);
        await PostAdminAsync(admin, id, "approve", new(), detail);

        var after = await admin.GetStringAsync($"/admin/photo-corrections/{id:D}");
        Assert.Contains("caption has changed since this was proposed", after);
        var repository = factory.Services.GetRequiredService<IPhotoCorrectionRepository>();
        Assert.Equal(PhotoCorrectionStatus.Pending, (await repository.GetByIdAsync(id))!.Status);
        Assert.Equal("Freddie on stage (edited)", (await photos.GetByIdAsync(302))!.Title);
    }

    [Fact]
    public async Task Revert_does_not_clobber_a_caption_edited_after_approval()
    {
        var member = await SignInAsync("pc-clobber@example.com", "Clobber Fan", "google-pc-clobber");
        var id = await SubmitAsync(member, 303, "Freddie portrait, Montreux");
        var admin = CreateAdminClient(AdminEmail);
        await PostAdminAsync(admin, id, "approve", new(), await admin.GetStringAsync($"/admin/photo-corrections/{id:D}"));
        var photos = factory.Services.GetRequiredService<IAdminPhotoRepository>();
        var photo = (await photos.GetByIdAsync(303))!;
        await photos.UpdateAsync(
            303,
            new AdminPhotoUpdateRequest("Editor rewrote this", photo.Keywords, photo.Year, photo.DateTime, photo.CatId),
            AdminEmail);

        await PostAdminAsync(admin, id, "revert", new(), await admin.GetStringAsync($"/admin/photo-corrections/{id:D}"));

        Assert.Equal("Editor rewrote this", (await photos.GetByIdAsync(303))!.Title);
        var repository = factory.Services.GetRequiredService<IPhotoCorrectionRepository>();
        Assert.Equal(PhotoCorrectionStatus.Approved, (await repository.GetByIdAsync(id))!.Status);
    }

    [Fact]
    public async Task Admin_can_accept_a_date_without_changing_the_photo_and_can_ask_and_reject()
    {
        var member = await SignInAsync("pc-flow@example.com", "Flow Fan", "google-pc-flow");
        var dateId = await SubmitAsync(member, 304, "12 March 1980", PhotoCorrectionKind.EventDate);
        var captionId = await SubmitAsync(member, 304, "Freddie, Hammersmith Odeon");
        var admin = CreateAdminClient(AdminEmail);
        var repository = factory.Services.GetRequiredService<IPhotoCorrectionRepository>();
        var photos = factory.Services.GetRequiredService<IAdminPhotoRepository>();

        await PostAdminAsync(admin, dateId, "approve", new(), await admin.GetStringAsync($"/admin/photo-corrections/{dateId:D}"));
        Assert.Equal(PhotoCorrectionStatus.Approved, (await repository.GetByIdAsync(dateId))!.Status);
        Assert.Equal("Freddie in rehearsal", (await photos.GetByIdAsync(304))!.Title);

        var detail = await admin.GetStringAsync($"/admin/photo-corrections/{captionId:D}");
        await PostAdminAsync(admin, captionId, "requestinfo", new() { ["messageToMember"] = "" }, detail);
        Assert.Equal(PhotoCorrectionStatus.Pending, (await repository.GetByIdAsync(captionId))!.Status);

        await PostAdminAsync(admin, captionId, "requestinfo", new() { ["messageToMember"] = "Which night?" }, detail);
        var asked = await member.GetStringAsync($"/submit/photo-correction/confirmation/{captionId:D}");
        Assert.Contains("Which night?", asked);
        Assert.DoesNotContain("internal", asked);

        await PostAdminAsync(admin, captionId, "reject", new() { ["messageToMember"] = "Not enough to go on", ["reviewNotes"] = "internal only" }, detail);
        var rejected = await member.GetStringAsync($"/submit/photo-correction/confirmation/{captionId:D}");
        Assert.Contains("Not enough to go on", rejected);
        Assert.DoesNotContain("internal only", rejected);
    }

    [Fact]
    public async Task Approved_date_appears_on_the_public_photo_with_credit_and_disappears_on_revert()
    {
        var member = await SignInAsync("pc-notes@example.com", "Notes Fan", "google-pc-notes");
        var fields = Fields("12 July 1986");
        fields["Kind"] = PhotoCorrectionKind.EventDate;
        fields["PublicCreditName"] = "Notes Fan Credit";
        fields["SubmissionId"] = Guid.NewGuid().ToString("D");
        var response = await PostProposalAsync(member, 101, fields);
        var id = Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
        var admin = CreateAdminClient(AdminEmail);
        var anonymous = factory.CreateClient();

        Assert.DoesNotContain("12 July 1986", await anonymous.GetStringAsync("/photography/brian-may/101"));

        await PostAdminAsync(admin, id, "approve", new() { ["reviewNotes"] = "internal-secret" }, await admin.GetStringAsync($"/admin/photo-corrections/{id:D}"));
        var page = await anonymous.GetStringAsync("/photography/brian-may/101");
        Assert.Contains("Event date: 12 July 1986 (thanks to Notes Fan Credit)", page);
        Assert.DoesNotContain("example.com/programme", page);
        Assert.DoesNotContain("internal-secret", page);
        Assert.DoesNotContain("pc-notes@example.com", page);

        await PostAdminAsync(admin, id, "revert", new(), await admin.GetStringAsync($"/admin/photo-corrections/{id:D}"));
        Assert.DoesNotContain("12 July 1986", await anonymous.GetStringAsync("/photography/brian-may/101"));
    }

    [Fact]
    public async Task Approved_date_without_credit_shows_no_name()
    {
        var member = await SignInAsync("pc-nocredit@example.com", "Private Fan", "google-pc-nocredit");
        var id = await SubmitAsync(member, 103, "2 March 1980", PhotoCorrectionKind.EventDate);
        var admin = CreateAdminClient(AdminEmail);
        await PostAdminAsync(admin, id, "approve", new(), await admin.GetStringAsync($"/admin/photo-corrections/{id:D}"));

        var page = await factory.CreateClient().GetStringAsync("/photography/brian-may/103");

        Assert.Contains("Event date: 2 March 1980", page);
        Assert.DoesNotContain("thanks to", page);
        Assert.DoesNotContain("Private Fan", page);
    }

    [Fact]
    public async Task My_submissions_corrections_tab_shows_own_status_and_editor_message_only()
    {
        var owner = await SignInAsync("pc-mine@example.com", "Mine Fan", "google-pc-mine");
        var other = await SignInAsync("pc-notmine@example.com", "Not Mine Fan", "google-pc-notmine");
        var ownerId = await SubmitAsync(owner, 202, "Owner's unique correction text");
        await SubmitAsync(other, 202, "Someone else's correction text");
        var admin = CreateAdminClient(AdminEmail);
        await PostAdminAsync(
            admin,
            ownerId,
            "requestinfo",
            new() { ["messageToMember"] = "Which night was it?", ["reviewNotes"] = "editor-only-note" },
            await admin.GetStringAsync($"/admin/photo-corrections/{ownerId:D}"));

        var mine = await owner.GetStringAsync("/account/my-submissions?tab=corrections");

        Assert.Contains("Owner&#x27;s unique correction text", mine);
        Assert.Contains("Which night was it?", mine);
        Assert.Contains("Needs", mine);
        Assert.DoesNotContain("Someone else", mine);
        Assert.DoesNotContain("editor-only-note", mine);
        var empty = await SignInAsync("pc-empty@example.com", "Empty Fan", "google-pc-empty");
        Assert.Contains("have not suggested any photo corrections", await empty.GetStringAsync("/account/my-submissions?tab=corrections"));
    }

    [Fact]
    public async Task Member_can_reply_to_an_editor_question_and_the_editor_sees_it()
    {
        var member = await SignInAsync("pc-reply@example.com", "Reply Fan", "google-pc-reply");
        var other = await SignInAsync("pc-reply-other@example.com", "Reply Other", "google-pc-reply-other");
        var id = await SubmitAsync(member, 202, "Knebworth, second night");
        var admin = CreateAdminClient(AdminEmail);
        var url = $"/submit/photo-correction/confirmation/{id:D}";

        Assert.DoesNotContain("Send reply", await member.GetStringAsync(url));
        await PostAdminAsync(admin, id, "requestinfo", new() { ["messageToMember"] = "Which night?" }, await admin.GetStringAsync($"/admin/photo-corrections/{id:D}"));

        var page = await member.GetStringAsync(url);
        Assert.Contains("Send reply", page);
        Assert.Contains("Reply", await member.GetStringAsync("/account/my-submissions?tab=corrections"));

        var blank = await member.PostAsync($"{url}?handler=Reply", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page),
            ["Reply"] = "   ",
        }));
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        Assert.Contains("Write your reply first.", await blank.Content.ReadAsStringAsync());

        var strangerPost = await other.PostAsync($"{url}?handler=Reply", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(await other.GetStringAsync("/submit/photo-correction/101")),
            ["Reply"] = "Hijack",
        }));
        Assert.Equal(HttpStatusCode.NotFound, strangerPost.StatusCode);

        var ok = await member.PostAsync($"{url}?handler=Reply", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page),
            ["Reply"] = "The second night, 9 August.",
        }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        var after = await member.GetStringAsync(url);
        Assert.Contains("Pending review", after);
        Assert.Contains("Your reply was sent", after);
        Assert.DoesNotContain("Send reply", after);
        var detail = await admin.GetStringAsync($"/admin/photo-corrections/{id:D}");
        Assert.Contains("The second night, 9 August.", detail);
        Assert.Contains("MemberReplied", detail);
    }

    [Fact]
    public async Task Member_can_withdraw_an_open_proposal_and_it_leaves_the_editor_queue()
    {
        var member = await SignInAsync("pc-withdraw@example.com", "Withdraw Fan", "google-pc-withdraw");
        var other = await SignInAsync("pc-withdraw-other@example.com", "Withdraw Other", "google-pc-withdraw-other");
        var id = await SubmitAsync(member, 201, "Live Aid proposal to withdraw");
        var admin = CreateAdminClient(AdminEmail);
        var url = $"/submit/photo-correction/confirmation/{id:D}";
        Assert.Contains("Live Aid proposal to withdraw", await admin.GetStringAsync("/admin/photo-corrections"));

        var page = await member.GetStringAsync(url);
        Assert.Contains("Withdraw this proposal", page);
        var token = ExtractAntiforgeryToken(page);
        var stranger = await other.PostAsync($"{url}?handler=Withdraw", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(await other.GetStringAsync("/submit/photo-correction/101")),
        }));
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);

        var ok = await member.PostAsync($"{url}?handler=Withdraw", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        var after = await member.GetStringAsync(url);
        Assert.Contains("Withdrawn", after);
        Assert.DoesNotContain("Withdraw this proposal", after);
        Assert.DoesNotContain("Live Aid proposal to withdraw", await admin.GetStringAsync("/admin/photo-corrections"));
        var again = await member.PostAsync($"{url}?handler=Withdraw", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Admin_actions_return_not_found_for_unknown_ids_and_redirect_on_get()
    {
        var member = await SignInAsync("pc-404@example.com", "Missing Fan", "google-pc-404");
        var id = await SubmitAsync(member, 202, "Magic Tour, Knebworth, 9 August 1986");
        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/photo-corrections/{id:D}");
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/photo-corrections/{missing:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAdminAsync(admin, missing, "reject", new() { ["messageToMember"] = "x" }, detail)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAdminAsync(admin, missing, "requestinfo", new() { ["messageToMember"] = "x" }, detail)).StatusCode);
        var approveMissing = await PostAdminAsync(admin, missing, "approve", new(), detail);
        Assert.Equal(HttpStatusCode.Redirect, approveMissing.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync($"/admin/photo-corrections/{id:D}/approve")).StatusCode);
    }

    private static Dictionary<string, string> Fields(string proposed) => new()
    {
        ["Kind"] = PhotoCorrectionKind.Caption,
        ["ProposedValue"] = proposed,
        ["Explanation"] = "I was in the audience and kept the programme.",
        ["Confidence"] = PhotoCorrectionConfidence.Probable,
        ["EvidenceUrl"] = "https://example.com/programme",
        ["EvidenceType"] = PhotoCorrectionEvidenceType.WebPage,
        ["SubmissionId"] = Guid.Empty.ToString("D"),
    };

    private async Task<Guid> SubmitAsync(
        HttpClient member,
        int picId,
        string proposed,
        string kind = PhotoCorrectionKind.Caption)
    {
        var fields = Fields(proposed);
        fields["Kind"] = kind;
        fields["SubmissionId"] = Guid.NewGuid().ToString("D");
        var response = await PostProposalAsync(member, picId, fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
    }

    private static async Task<HttpResponseMessage> PostProposalAsync(
        HttpClient member,
        int picId,
        Dictionary<string, string> fields,
        int? tokenFromPicId = null)
    {
        var form = await member.GetStringAsync($"/submit/photo-correction/{tokenFromPicId ?? picId}");
        var body = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(form),
        };
        if (body["SubmissionId"] == Guid.Empty.ToString("D"))
        {
            // A stable id per (member, photo) lets the test model a double-click retry.
            body["SubmissionId"] = StableId(picId, body["ProposedValue"]).ToString("D");
        }

        return await member.PostAsync($"/submit/photo-correction/{picId}", new FormUrlEncodedContent(body));
    }

    private static Guid StableId(int picId, string proposed)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(picId).CopyTo(bytes, 0);
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proposed)).AsSpan(0, 12).CopyTo(bytes.AsSpan(4));
        return new Guid(bytes);
    }

    private static async Task<HttpResponseMessage> PostAdminAsync(
        HttpClient admin,
        Guid id,
        string handler,
        Dictionary<string, string> fields,
        string detailHtml)
    {
        var body = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detailHtml),
        };
        return await admin.PostAsync($"/admin/photo-corrections/{id:D}/{handler}", new FormUrlEncodedContent(body));
    }

    private HttpClient CreateAdminClient(string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, email);
        return client;
    }

    private async Task<HttpClient> SignInAsync(string email, string displayName, string subject)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);
        var callback = await client.GetAsync("/account/external-login-callback");
        Assert.True(callback.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
