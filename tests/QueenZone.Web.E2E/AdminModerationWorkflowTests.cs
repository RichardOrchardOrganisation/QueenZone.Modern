using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using QueenZone.Data;
using QueenZone.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.E2E;

/// <summary>
/// Admin moderation and editorial journeys (#548): photo/article submission approval and
/// rejection through to public visibility or the member's rejected-status view, news suggestion
/// triage, biography/photo editorial CRUD, the admin dashboard, search reindex, and an
/// antiforgery negative case. Runs against the SQL Express mirror
/// (<c>ASPNETCORE_ENVIRONMENT=E2E</c>); every row this fixture creates is tagged with the
/// <c>uie2e-{runId}-...</c> marker convention and deleted in <see cref="CleanupCreatedRowsAsync"/>,
/// including the promoted <c>PIC_FILES_T</c> row and its <c>PhotoAdminAuditLogs</c> entry. Member
/// submission mechanics (forms, validation) are covered by #546's
/// <see cref="CommunitySubmissionWorkflowTests"/>; this fixture submits only what each moderation
/// journey needs to reach an admin decision.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.RealData)]
public class AdminModerationWorkflowTests : RealDataPageTest
{
    private const string MemberIdHeader = "X-Test-Member-Id";
    private const string MemberNameHeader = "X-Test-Member-Name";
    private const string MemberEmailHeader = "X-Test-Member-Email";
    private const string AdminEmailHeader = "X-Test-User-Email";

    private static string AdminEmail =>
        Environment.GetEnvironmentVariable("E2E_ADMIN_EMAIL") ?? "admin@test.local";

    [Test]
    public async Task Photo_submission_approval_is_visible_in_public_gallery()
    {
        var member = await CreateMemberAsync("photo-approve");
        var title = $"{member.Marker} approve photo";
        await SubmitPhotoAsync(title);

        var category = await GetFirstPhotoCategoryAsync();

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await OpenSubmissionDetailAsync(adminPage, "/admin/photo-submissions", title);

        await adminPage.GetByLabel("Gallery category (must match an existing category)").FillAsync(category.Name);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync(new() { Timeout = 60_000 });
        try
        {
            await Expect(adminPage.GetByText("Photo approved and published to the gallery."))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
        }
        catch (TimeoutException ex)
        {
            var status = await adminPage.Locator("[role='status']").AllInnerTextsAsync();
            Assert.Fail(
                $"Photo approval did not confirm success. URL={adminPage.Url} status={string.Join(" | ", status)}. {ex.Message}");
        }

        var picId = await GetPromotedPicIdAsync(title);
        var slug = NewsSlug.Slugify(category.Name);

        var response = await Page.GotoAsync($"/photography/{slug}/{picId}");
        Assert.That(response?.Status, Is.EqualTo(200), $"Expected the promoted photo at /photography/{slug}/{picId} to be public.");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title, Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByText($"Submitted by {member.DisplayName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Photo_submission_rejection_shows_rejected_status_to_member()
    {
        var member = await CreateMemberAsync("photo-reject");
        var title = $"{member.Marker} reject photo";
        await SubmitPhotoAsync(title);

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await OpenSubmissionDetailAsync(adminPage, "/admin/photo-submissions", title);

        adminPage.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        const string rejectionReason = "Not a good fit for the gallery.";
        await adminPage.GetByLabel("Rejection reason (shown to submitter)").FillAsync(rejectionReason);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();
        await Expect(adminPage.GetByText("Photo rejected.")).ToBeVisibleAsync();

        await Page.GotoAsync("/account/my-submissions");
        var row = Page.Locator("table.admin-table tbody tr").Filter(new() { HasText = title });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.GetByText("Rejected")).ToBeVisibleAsync();
        await Expect(row.GetByText(rejectionReason)).ToBeVisibleAsync();
    }

    [Test]
    public async Task Article_submission_approval_and_publish_reaches_public_url()
    {
        var member = await CreateMemberAsync("article-approve");
        var title = $"{member.Marker} approve article";
        await SubmitArticleAsync(title, member.Marker);

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await OpenSubmissionDetailAsync(adminPage, "/admin/articles", title);

        var slug = await adminPage.GetByLabel("Final slug").InputValueAsync();

        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Approve for publishing" }).ClickAsync();
        await Expect(adminPage.GetByText("Approved for publishing.")).ToBeVisibleAsync();

        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Publish now" }).ClickAsync();
        await Expect(adminPage.GetByText("Article published.")).ToBeVisibleAsync();

        var response = await Page.GotoAsync($"/articles/{slug}");
        Assert.That(response?.Status, Is.EqualTo(200), $"Expected the published article at /articles/{slug} to be public.");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = title, Level = 1 })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Article_submission_rejection_shows_rejected_status_to_member()
    {
        var member = await CreateMemberAsync("article-reject");
        var title = $"{member.Marker} reject article";
        await SubmitArticleAsync(title, member.Marker);

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await OpenSubmissionDetailAsync(adminPage, "/admin/articles", title);

        const string rejectionReason = "Does not meet editorial guidelines.";
        await adminPage.GetByLabel("Reason (required for rejection; optional for revision)").FillAsync(rejectionReason);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();
        await Expect(adminPage.GetByText("Article rejected.")).ToBeVisibleAsync();

        await Page.GotoAsync("/account/my-submissions?tab=articles");
        var row = Page.Locator("table.admin-table tbody tr").Filter(new() { HasText = title });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.GetByText("Rejected")).ToBeVisibleAsync();
        await Expect(row.GetByText(rejectionReason)).ToBeVisibleAsync();
    }

    [Test]
    public async Task News_suggestion_promote_creates_admin_news_draft()
    {
        var member = await CreateMemberAsync("news-promote");
        var headline = $"{member.Marker} promote headline";
        await SubmitNewsSuggestionAsync(headline, member.Marker);

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        var suggestionId = await OpenSubmissionDetailAndGetIdAsync(adminPage, "/admin/news-suggestions", headline);

        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Promote to admin news draft" }).ClickAsync();
        await Expect(adminPage).ToHaveURLAsync(new Regex("/admin/news/\\d+/edit"));
        await Expect(adminPage.GetByLabel("Title")).ToHaveValueAsync(headline);

        await adminPage.GotoAsync($"/admin/news-suggestions/{suggestionId}");
        await Expect(adminPage.Locator("dl")).ToContainTextAsync("Promoted");
    }

    [Test]
    public async Task News_suggestion_reject_marks_status_rejected()
    {
        var member = await CreateMemberAsync("news-reject");
        var headline = $"{member.Marker} reject headline";
        await SubmitNewsSuggestionAsync(headline, member.Marker);

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await OpenSubmissionDetailAsync(adminPage, "/admin/news-suggestions", headline);

        adminPage.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();
        await Expect(adminPage.GetByText("Suggestion rejected.")).ToBeVisibleAsync();
        await Expect(adminPage.Locator("dl")).ToContainTextAsync("Rejected");
    }

    [Test]
    public async Task Biography_chapter_create_and_edit_persist_after_reload()
    {
        // Title max is 50 chars (input maxlength + BiographyValidation). Keep marker short.
        var marker = NextMarker("bio");
        var title = $"{marker} ch";
        Assert.That(title.Length + " updated".Length, Is.LessThanOrEqualTo(BiographyValidation.MaxTitleLength));

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();

        await adminPage.GotoAsync("/admin/biography/new");
        await adminPage.GetByLabel("Title").FillAsync(title);
        await adminPage.GetByLabel("Summary").FillAsync("Disposable E2E summary.");
        await adminPage.GetByLabel("Display sequence").FillAsync("250");
        await FillRichTextEditorAsync(adminPage, BuildLongText(marker, "Disposable E2E biography chapter body."));
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(adminPage).ToHaveURLAsync(new Regex("/admin/biography/\\d+/edit"));
        var createdStatus = adminPage.GetByText($"Created chapter \"{title}\".");
        await Expect(createdStatus).ToBeVisibleAsync();
        Assert.That(
            await createdStatus.InnerTextAsync(),
            Does.Contain($"Created chapter \"{title}\"."),
            $"Unexpected status after create. URL={adminPage.Url}");
        await Expect(adminPage.GetByLabel("Title")).ToHaveValueAsync(title);

        var updatedTitle = $"{title} updated";
        await adminPage.GetByLabel("Title").FillAsync(updatedTitle);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        var savedStatus = adminPage.GetByText($"Saved \"{updatedTitle}\".");
        await Expect(savedStatus).ToBeVisibleAsync();
        Assert.That(
            await savedStatus.InnerTextAsync(),
            Does.Contain($"Saved \"{updatedTitle}\"."),
            $"Unexpected status after save. URL={adminPage.Url}");

        await adminPage.ReloadAsync();
        await Expect(adminPage.GetByLabel("Title")).ToHaveValueAsync(updatedTitle);
    }

    [Test]
    public async Task Admin_photo_create_edit_and_hard_delete_round_trip()
    {
        var marker = NextMarker("admin-photo-crud");
        var title = $"{marker} admin photo";
        var category = await GetFirstPhotoCategoryAsync();

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();

        await adminPage.GotoAsync("/admin/photos/new");
        await adminPage.GetByLabel("Image file").SetInputFilesAsync(new FilePayload
        {
            Name = "e2e-admin-photo.png",
            MimeType = "image/png",
            Buffer = GeneratePngBytes(320, 240),
        });
        await adminPage.GetByLabel("Title").FillAsync(title);
        await adminPage.GetByLabel("Category").SelectOptionAsync(new SelectOptionValue { Value = category.CatId.ToString() });
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Upload and create" })
            .ClickAsync(new() { Timeout = 60_000 });

        await Expect(adminPage).ToHaveURLAsync(new Regex("/admin/photos/\\d+$"), new() { Timeout = 60_000 });
        await Expect(adminPage.GetByLabel("Title")).ToHaveValueAsync(title);

        var updatedTitle = $"{title} updated";
        await adminPage.GetByLabel("Title").FillAsync(updatedTitle);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(adminPage.GetByText("Photo updated.")).ToBeVisibleAsync();

        await adminPage.ReloadAsync();
        await Expect(adminPage.GetByLabel("Title")).ToHaveValueAsync(updatedTitle);

        adminPage.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Hard delete" })
            .ClickAsync(new() { Timeout = 60_000 });
        await Expect(adminPage).ToHaveURLAsync(new Regex("/admin/photos/?$"), new() { Timeout = 60_000 });
        await Expect(adminPage.GetByText("Deleted photo #").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_dashboard_renders_stat_tiles_and_submission_queue()
    {
        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        var response = await adminPage.GotoAsync("/admin");

        Assert.That(response?.Status, Is.EqualTo(200));
        await Expect(adminPage.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Level = 1 })).ToBeVisibleAsync();
        await Expect(adminPage.Locator(".admin-dashboard__stat-value").First).ToBeVisibleAsync();
        await Expect(adminPage.Locator(".admin-dashboard__queue-tile")).ToHaveCountAsync(9);
        await Expect(adminPage.Locator(".admin-dashboard__queue-tile-label")).ToHaveTextAsync(
            [
                "Help requests",
                "Reported messages",
                "Reported forum posts",
                "Fan performance reports",
                "Photos",
                "News suggestions",
                "Articles",
                "Trivia suggestions",
                "Fan performances",
            ]);
        await Expect(adminPage.Locator(".admin-dashboard__queue-tile-count").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_search_reindex_completes()
    {
        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await adminPage.GotoAsync("/admin/search");

        await adminPage.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Rebuild search index now|Reindex in progress") }).ClickAsync();

        var root = adminPage.Locator("#search-index-admin");
        var jobStatus = adminPage.Locator("#job-status");
        try
        {
            await Expect(root).ToHaveAttributeAsync("data-running", "false", new() { Timeout = 180_000 });
            await Expect(jobStatus).ToHaveAttributeAsync("data-phase", "Succeeded", new() { Timeout = 5_000 });
        }
        catch (TimeoutException ex)
        {
            var running = await root.GetAttributeAsync("data-running");
            var phase = await jobStatus.GetAttributeAsync("data-phase");
            var statusText = await jobStatus.InnerTextAsync();
            Assert.Fail(
                $"Search reindex did not finish. route=/admin/search running={running} phase={phase} " +
                $"status=\"{statusText}\". {ex.Message}");
        }
    }

    [Test]
    public async Task Admin_post_without_antiforgery_token_returns_400()
    {
        var member = await CreateMemberAsync("antiforgery-negative");
        var title = $"{member.Marker} antiforgery photo";
        await SubmitPhotoAsync(title);

        var adminContext = await NewAdminContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        var submissionId = await OpenSubmissionDetailAndGetIdAsync(adminPage, "/admin/photo-submissions", title);

        var form = adminContext.APIRequest.CreateFormData();
        form.Set("approvedCategory", "Nonexistent Category");
        var response = await adminContext.APIRequest.PostAsync(
            $"{BaseUrl}/admin/photo-submissions/{submissionId}/approve",
            new APIRequestContextOptions
            {
                Form = form,
                Headers = new Dictionary<string, string> { [AdminEmailHeader] = AdminEmail },
                FailOnStatusCode = false,
            });

        Assert.That(response.Status, Is.EqualTo(400), "A missing __RequestVerificationToken must be rejected by antiforgery validation.");
    }

    /// <summary>
    /// Seeds a real <c>MemberAccounts</c> row (submission forms look the member up via
    /// <c>MemberAccountService</c>/repositories) and impersonates it on the shared <see cref="Page"/>,
    /// mirroring <see cref="CommunitySubmissionWorkflowTests.CreateMemberAsync"/>.
    /// </summary>
    private async Task<MemberContext> CreateMemberAsync(string fixtureSlug)
    {
        var marker = NextMarker(fixtureSlug);
        var memberId = Guid.NewGuid();
        var email = $"{marker}@e2e.queenzone.local";
        var displayName = $"E2E {marker}";

        await using (var db = RealDataDb.CreateContext())
        {
            db.MemberAccounts.Add(new MemberAccount
            {
                Id = memberId,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            [MemberIdHeader] = memberId.ToString(),
            [MemberNameHeader] = displayName,
            [MemberEmailHeader] = email,
        });

        return new MemberContext(memberId, marker, displayName, email);
    }

    private Task<IBrowserContext> NewAdminContextAsync() =>
        CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { [AdminEmailHeader] = AdminEmail },
        });

    private async Task SubmitPhotoAsync(string title)
    {
        await Page.GotoAsync("/submit/photo");
        await Page.GetByLabel("Title").FillAsync(title);
        await Page.Locator("#PhotoFile").SetInputFilesAsync(new FilePayload
        {
            Name = "e2e-photo.png",
            MimeType = "image/png",
            Buffer = GeneratePngBytes(400, 300),
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit for review" })
            .ClickAsync(new() { Timeout = 60_000 });
        await Expect(Page).ToHaveURLAsync(new Regex(".*/submit/photo/confirmation/.+"), new() { Timeout = 60_000 });
    }

    private async Task SubmitArticleAsync(string title, string marker)
    {
        await Page.GotoAsync("/submit/article");
        await Page.GetByLabel("Title").FillAsync(title);
        await Page.GetByLabel("Excerpt").FillAsync("Disposable E2E article submission excerpt.");
        await FillRichTextEditorAsync(Page, BuildLongText(marker, "Disposable E2E article body."));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/submit/article/confirmation/.+"));
    }

    private async Task SubmitNewsSuggestionAsync(string headline, string marker)
    {
        await Page.GotoAsync("/submit/news");
        await Page.GetByLabel("News story URL").FillAsync($"https://example.com/{marker}-story");
        await Page.GetByLabel("Suggested headline").FillAsync(headline);
        await Page.GetByLabel("Notes for the editor").FillAsync(
            BuildLongText(marker, "Disposable E2E news suggestion notes.", minVisibleChars: 120));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit suggestion" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/submit/news/confirmation"));
    }

    private async Task OpenSubmissionDetailAsync(IPage page, string indexPath, string title)
    {
        await page.GotoAsync(indexPath);
        var row = page.Locator("table.admin-table tbody tr").Filter(new() { HasText = title });
        await Expect(row).ToBeVisibleAsync();
        await row.GetByRole(AriaRole.Link, new() { Name = "Review" }).ClickAsync();
    }

    private async Task<Guid> OpenSubmissionDetailAndGetIdAsync(IPage page, string indexPath, string title)
    {
        await OpenSubmissionDetailAsync(page, indexPath, title);
        var segments = new Uri(page.Url).AbsolutePath.TrimEnd('/').Split('/');
        return Guid.Parse(segments[^1]);
    }

    private async Task<AdminPhotoCategory> GetFirstPhotoCategoryAsync()
    {
        await using var db = RealDataDb.CreateContext();
        var repository = new EfAdminPhotoRepository(db);
        var categories = await repository.GetCategoriesAsync();
        Assert.That(categories, Is.Not.Empty, "Expected at least one PIC_CAT_T category in the SQL Express mirror.");
        return categories[0];
    }

    private static async Task<int> GetPromotedPicIdAsync(string submissionTitle)
    {
        await using var db = RealDataDb.CreateContext();
        var picId = await db.PhotoSubmissions
            .Where(s => s.Title == submissionTitle)
            .Select(s => s.PromotedPicId)
            .FirstOrDefaultAsync();

        if (picId is not int id)
        {
            throw new InvalidOperationException($"Photo submission \"{submissionTitle}\" was not promoted.");
        }

        return id;
    }

    private async Task FillRichTextEditorAsync(IPage page, string text)
    {
        var editor = page.Locator("[data-testid='rich-text-editor']").Last;
        await Expect(editor).ToBeVisibleAsync();
        await editor.ClickAsync();
        await page.Keyboard.InsertTextAsync(text);
    }

    private static string BuildLongText(string marker, string sentencePrefix, int minVisibleChars = 320)
    {
        var sentence = $"{sentencePrefix} Marker {marker}. ";
        var builder = new StringBuilder();
        while (builder.Length < minVisibleChars)
        {
            builder.Append(sentence);
        }

        return builder.ToString();
    }

    private static byte[] GeneratePngBytes(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(100, 149, 237, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Deletes every row this fixture created, matched by the <c>uie2e-{runId}-...</c> marker.
    /// <c>SearchDocument</c> copies are removed by <c>SourceKey</c> (<c>news:{id}</c>,
    /// <c>article:{slug}</c>, <c>biography:{id}</c>) so publish/reindex cannot leave an index
    /// row after the source is gone. <c>PIC_FILES_T</c> is deleted (and its
    /// <see cref="QueenZoneDbContext.PhotoAdminAuditLogs"/> rows) before the owning
    /// <see cref="PhotoSubmissionEntity"/>, mirroring
    /// <c>EfContentSubmissionLiveProbeTests</c>'s self-cleaning promotion probe in
    /// <c>QueenZone.Web.Tests</c> — the same repository path <c>PhotoSubmissionPromotionService</c>
    /// uses, and the same one <c>Admin_photo_create_edit_and_hard_delete_round_trip</c> writes to
    /// directly (hence matching by <c>Name LIKE marker%</c> rather than a tracked PicId list).
    /// </summary>
    protected override async Task CleanupCreatedRowsAsync(IReadOnlyList<string> markers)
    {
        await using var db = RealDataDb.CreateContext();
        foreach (var marker in markers)
        {
            var picIds = await db.Database
                .SqlQueryRaw<int>("SELECT PIC_ID AS [Value] FROM dbo.PIC_FILES_T WHERE Name LIKE {0}", marker + "%")
                .ToListAsync();

            // Hard-delete removes PIC_FILES_T but keeps PhotoAdminAuditLog rows (create/edit/delete).
            // Match those leftover audits by Details containing the marker title.
            await db.PhotoAdminAuditLogs
                .Where(a => (a.Details != null && a.Details.Contains(marker))
                    || picIds.Contains(a.PicId))
                .ExecuteDeleteAsync();

            if (picIds.Count > 0)
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.PIC_FILES_T WHERE Name LIKE {0}", marker + "%");
            }

            var submissionIds = await db.PhotoSubmissions
                .Where(s => s.Title.Contains(marker))
                .Select(s => s.Id)
                .ToListAsync();
            if (submissionIds.Count > 0)
            {
                await db.PhotoSubmissionAuditLogs.Where(a => submissionIds.Contains(a.PhotoSubmissionId)).ExecuteDeleteAsync();
                await db.PhotoSubmissions.Where(s => submissionIds.Contains(s.Id)).ExecuteDeleteAsync();
            }

            var articleSlugs = await db.ArticleSubmissions
                .Where(s => s.Title.Contains(marker))
                .Select(s => s.Slug)
                .ToListAsync();
            await db.ArticleSubmissions.Where(s => s.Title.Contains(marker)).ExecuteDeleteAsync();
            await db.NewsSuggestions
                .Where(s => s.Url.Contains(marker) || (s.Title != null && s.Title.Contains(marker)))
                .ExecuteDeleteAsync();

            var newsIds = await db.NewsRows.Where(r => r.Title.Contains(marker)).Select(r => r.NewsId).ToListAsync();
            if (newsIds.Count > 0)
            {
                await db.NewsAuditLogs.Where(a => newsIds.Contains(a.NewsId)).ExecuteDeleteAsync();
                await db.NewsRows.Where(r => newsIds.Contains(r.NewsId)).ExecuteDeleteAsync();
            }

            var biographyIds = await db.Database
                .SqlQueryRaw<int>(
                    "SELECT CAST(Q_BIO_ID AS int) AS [Value] FROM dbo.Q_BIO_T WHERE TITLE LIKE {0}",
                    marker + "%")
                .ToListAsync();

            var searchSourceKeys = newsIds.Select(SearchDocumentSourceKey.ForNews)
                .Concat(articleSlugs.Where(slug => slug.Length > 0).Select(SearchDocumentSourceKey.ForArticle))
                .Concat(biographyIds.Select(SearchDocumentSourceKey.ForBiography));
            await SearchDocumentTeardown.DeleteBySourceKeysAsync(db, searchSourceKeys);

            await db.SearchDocuments
                .Where(d => d.Title.Contains(marker) || d.SourceKey.Contains(marker))
                .ExecuteDeleteAsync();

            await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Q_BIO_T WHERE TITLE LIKE {0}", marker + "%");

            await db.MemberAccounts.Where(m => m.Email.Contains(marker)).ExecuteDeleteAsync();
        }
    }

    private sealed record MemberContext(Guid Id, string Marker, string DisplayName, string Email);
}
