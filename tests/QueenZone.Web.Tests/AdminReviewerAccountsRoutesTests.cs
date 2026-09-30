using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminReviewerAccountsRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public AdminReviewerAccountsRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Page_RequiresAdminAuthentication()
    {
        var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/reviewer-accounts")).StatusCode);

        var stranger = factory.CreateAdminClient("stranger@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/admin/reviewer-accounts")).StatusCode);
    }

    [Fact]
    public async Task Create_AddsPasswordAccountWithoutRenderingPassword()
    {
        var email = UniqueEmail("create");
        const string password = "create-reviewer-password";
        var admin = factory.CreateAdminClient();
        var page = await admin.GetStringAsync("/admin/reviewer-accounts");
        Assert.Contains("for=\"Input_Password\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"Input_Password\"", page, StringComparison.Ordinal);
        Assert.Contains("for=\"Input_ConfirmPassword\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"Input_ConfirmPassword\"", page, StringComparison.Ordinal);

        var response = await admin.PostAsync(
            "/admin/reviewer-accounts",
            Form(page, new()
            {
                ["Input.Email"] = email,
                ["Input.DisplayName"] = "Store Reviewer",
                ["Input.Password"] = password,
                ["Input.ConfirmPassword"] = password,
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await admin.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.Contains(email, list);
        Assert.Contains("Set — hidden", list);
        Assert.DoesNotContain(password, list);

        using var scope = factory.Services.CreateScope();
        var memberService = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        Assert.True((await memberService.SignInAsync(email, password)).Succeeded);
    }

    [Fact]
    public async Task Edit_UpdatesDetailsAndResetsPassword()
    {
        var originalEmail = UniqueEmail("edit-old");
        var newEmail = UniqueEmail("edit-new");
        const string oldPassword = "old-reviewer-password";
        const string newPassword = "new-reviewer-password";
        var account = await CreateAccountAsync(originalEmail, oldPassword);
        var admin = factory.CreateAdminClient();
        var pagePath = $"/admin/reviewer-accounts/{account.Id}";
        var page = await admin.GetStringAsync(pagePath);
        Assert.Contains("for=\"Input_NewPassword\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"Input_NewPassword\"", page, StringComparison.Ordinal);
        Assert.Contains("for=\"Input_ConfirmNewPassword\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"Input_ConfirmNewPassword\"", page, StringComparison.Ordinal);

        var response = await admin.PostAsync(
            pagePath,
            Form(page, new()
            {
                ["Input.Email"] = newEmail,
                ["Input.DisplayName"] = "Updated Reviewer",
                ["Input.NewPassword"] = newPassword,
                ["Input.ConfirmNewPassword"] = newPassword,
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var memberService = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        Assert.False((await memberService.SignInAsync(originalEmail, oldPassword)).Succeeded);
        var signIn = await memberService.SignInAsync(newEmail, newPassword);
        Assert.True(signIn.Succeeded);
        Assert.Equal("Updated Reviewer", signIn.Account!.DisplayName);
    }

    [Fact]
    public async Task InvalidEdit_DoesNotEchoSubmittedPassword()
    {
        var account = await CreateAccountAsync(UniqueEmail("invalid"), "existing-reviewer-password");
        var admin = factory.CreateAdminClient();
        var pagePath = $"/admin/reviewer-accounts/{account.Id}";
        var page = await admin.GetStringAsync(pagePath);
        const string submittedPassword = "submitted-secret-password";

        var response = await admin.PostAsync(
            pagePath,
            Form(page, new()
            {
                ["Input.Email"] = account.Email,
                ["Input.DisplayName"] = account.DisplayName,
                ["Input.NewPassword"] = submittedPassword,
                ["Input.ConfirmNewPassword"] = "different-secret-password",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Passwords do not match.", body);
        Assert.DoesNotContain(submittedPassword, body);
    }

    [Fact]
    public async Task Delete_RemovesPasswordButRetainsMemberRecord()
    {
        var email = UniqueEmail("remove");
        var account = await CreateAccountAsync(email, "remove-reviewer-password");
        var admin = factory.CreateAdminClient();
        var pagePath = $"/admin/reviewer-accounts/{account.Id}";
        var page = await admin.GetStringAsync(pagePath);

        var response = await admin.PostAsync(
            $"{pagePath}?handler=Delete",
            Form(page, []));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var repository = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var retained = await repository.FindByIdAsync(account.Id);
        Assert.NotNull(retained);
        Assert.Null(retained.PasswordHash);

        var list = await admin.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(email, list);
    }

    private async Task<QueenZone.Data.Entities.MemberAccount> CreateAccountAsync(string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        var result = await service.RegisterAsync(email, password, "Reviewer Account");
        Assert.True(result.Succeeded, result.Error);
        return result.Account!;
    }

    private static FormUrlEncodedContent Form(string html, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = AdminHttpTestHelpers.ExtractAntiforgeryToken(html);
        return new FormUrlEncodedContent(fields);
    }

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.com";
}
