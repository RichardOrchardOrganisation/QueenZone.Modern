using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminReviewerAccountValidationTests
{
    public static TheoryData<string, string, string, string> InvalidInputs => new()
    {
        { "bad-email", "Reviewer", "abcdefghijkl", "Enter a valid email address of at most 256 characters." },
        { "   ", "Reviewer", "abcdefghijkl", "Enter a valid email address of at most 256 characters." },
        { new string('a', 245) + "@example.com", "Reviewer", "abcdefghijkl", "Enter a valid email address of at most 256 characters." },
        { "reviewer@example.com", new string('n', MemberAccountService.MinDisplayNameLength - 1), "abcdefghijkl", DisplayNameError },
        { "reviewer@example.com", new string('n', MemberAccountService.MaxDisplayNameLength + 1), "abcdefghijkl", DisplayNameError },
        { "reviewer@example.com", "   ", "abcdefghijkl", DisplayNameError },
        { "reviewer@example.com", "Reviewer", "", "Password is required." },
        { "reviewer@example.com", "Reviewer", "            ", "Password is required." },
        { "reviewer@example.com", "Reviewer", new string('p', 11), "Password must be at least 12 characters." },
        { "reviewer@example.com", "Reviewer", new string('p', 257), "Password must be at most 256 characters." },
    };

    private static string DisplayNameError =>
        $"Display name must be between {MemberAccountService.MinDisplayNameLength} and {MemberAccountService.MaxDisplayNameLength} characters.";

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Create_RejectsInvalidInputWithoutPersisting(string email, string name, string password, string error)
    {
        var members = new InMemoryMemberAccountRepository();
        var result = await Service(members).CreateAsync(email, name, password);
        Assert.False(result.Succeeded);
        Assert.False(result.WasNotFound);
        Assert.Equal(error, result.Error);
        Assert.Empty(await members.ListLocalPasswordAccountsAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Update_RejectsInvalidInputWithoutChangingAccount(string email, string name, string password, string error)
    {
        // Blank passwords are optional for updates; the other invalid fields still reject.
        if (error == "Password is required.")
        {
            password = new string('p', 11);
            error = "Password must be at least 12 characters.";
        }
        var members = new InMemoryMemberAccountRepository();
        var service = Service(members);
        var created = await service.CreateAsync("original@example.com", "Original Reviewer", "original-password");
        var original = await members.FindByIdAsync(created.Account!.Id);
        var originalHash = original!.PasswordHash;
        var result = await service.UpdateAsync(original.Id, email, name, password);
        Assert.Equal(error, result.Error);
        var stored = await members.FindByIdAsync(original.Id);
        Assert.Equal("original@example.com", stored!.Email);
        Assert.Equal("Original Reviewer", stored.DisplayName);
        Assert.Equal(originalHash, stored.PasswordHash);
        Assert.Single(await members.ListLocalPasswordAccountsAsync());
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(256, false)]
    public async Task Create_AcceptsBoundaryLengthsAndTrimsIdentityWithoutTrimmingPassword(int passwordLength, bool minimumName)
    {
        var members = new InMemoryMemberAccountRepository();
        var email = new string('a', 244) + "@example.com";
        var name = new string('n', minimumName ? MemberAccountService.MinDisplayNameLength : MemberAccountService.MaxDisplayNameLength);
        var password = " " + new string('p', passwordLength - 2) + " ";
        var result = await Service(members).CreateAsync(" " + email + " ", " " + name + " ", password);
        Assert.True(result.Succeeded);
        var stored = await members.FindByIdAsync(result.Account!.Id);
        Assert.Equal(email, stored!.Email);
        Assert.Equal(name, stored.DisplayName);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            new PasswordHasher<MemberAccount>().VerifyHashedPassword(stored, stored.PasswordHash!, password));
        Assert.Equal(PasswordVerificationResult.Failed,
            new PasswordHasher<MemberAccount>().VerifyHashedPassword(stored, stored.PasswordHash!, password.Trim()));
    }

    [Fact]
    public async Task DuplicateCreateAndUpdate_PreserveBothAccounts()
    {
        var members = new InMemoryMemberAccountRepository();
        var service = Service(members);
        var first = await service.CreateAsync("first@example.com", "First Reviewer", "first-password");
        var second = await service.CreateAsync("second@example.com", "Second Reviewer", "second-password");
        var firstHash = (await members.FindByIdAsync(first.Account!.Id))!.PasswordHash;
        var duplicate = await service.CreateAsync(" first@example.com ", "Duplicate Reviewer", "duplicate-password");
        Assert.Equal("An account with that email already exists.", duplicate.Error);
        var update = await service.UpdateAsync(first.Account.Id, "second@example.com", "Changed Reviewer", "changed-password");
        Assert.Equal(duplicate.Error, update.Error);
        Assert.Equal(2, (await service.ListAsync()).Count);
        var stored = await members.FindByIdAsync(first.Account.Id);
        Assert.Equal("first@example.com", stored!.Email);
        Assert.Equal("First Reviewer", stored.DisplayName);
        Assert.Equal(firstHash, stored.PasswordHash);
        Assert.Equal("second@example.com", (await service.FindAsync(second.Account!.Id))!.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("            ")]
    public async Task Update_WithOptionalPasswordAndOwnEmail_PreservesHash(string? password)
    {
        var members = new InMemoryMemberAccountRepository();
        var service = Service(members);
        var first = await service.CreateAsync("same@example.com", "Reviewer", "original-password");
        var hash = (await members.FindByIdAsync(first.Account!.Id))!.PasswordHash;
        Assert.True((await service.UpdateAsync(first.Account.Id, " same@example.com ", " Renamed Reviewer ", password)).Succeeded);
        Assert.Equal(hash, (await members.FindByIdAsync(first.Account.Id))!.PasswordHash);
        Assert.Equal("Renamed Reviewer", (await service.FindAsync(first.Account.Id))!.DisplayName);
    }

    [Fact]
    public async Task MissingOrNonPasswordAccount_CannotBeUpdatedOrRemovedAsReviewer()
    {
        var members = new InMemoryMemberAccountRepository();
        var service = Service(members);
        var missing = Guid.NewGuid();
        Assert.True((await service.UpdateAsync(missing, "missing@example.com", "Missing Reviewer", null)).WasNotFound);
        Assert.False(await service.RemovePasswordAsync(missing));
        Assert.Null(await service.FindAsync(missing));
        var social = await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "social@example.com",
            DisplayName = "Social Member",
            CreatedAt = DateTime.UtcNow,
        });
        Assert.True((await service.UpdateAsync(social.Id, "changed@example.com", "Changed Member", null)).WasNotFound);
        Assert.Null(await service.FindAsync(social.Id));
        Assert.Equal("social@example.com", (await members.FindByIdAsync(social.Id))!.Email);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task Update_WhenAccountDisappearsDuringWrite_ReturnsNotFoundWithoutChangingData()
    {
        var members = new InMemoryMemberAccountRepository();
        var created = await Service(members).CreateAsync("race@example.com", "Race Reviewer", "original-password");
        var result = await Service(MissingReviewerUpdateRepository.Wrap(members))
            .UpdateAsync(created.Account!.Id, "updated@example.com", "Updated Reviewer", null);
        Assert.True(result.WasNotFound);
        Assert.False(result.Succeeded);
        Assert.Equal("Reviewer account not found.", result.Error);
        Assert.Equal("race@example.com", (await members.FindByIdAsync(created.Account.Id))!.Email);
    }

    private static AdminReviewerAccountService Service(IMemberAccountRepository members) =>
        new(members, NullLogger<AdminReviewerAccountService>.Instance);
}

public class MissingReviewerUpdateRepository : DispatchProxy
{
    private IMemberAccountRepository inner = null!;

    public static IMemberAccountRepository Wrap(IMemberAccountRepository inner)
    {
        var proxy = Create<IMemberAccountRepository, MissingReviewerUpdateRepository>();
        ((MissingReviewerUpdateRepository)(object)proxy).inner = inner;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(IMemberAccountRepository.UpdateLocalPasswordAccountAsync)
            ? Task.FromResult<MemberAccount?>(null)
            : targetMethod!.Invoke(inner, args);
}
