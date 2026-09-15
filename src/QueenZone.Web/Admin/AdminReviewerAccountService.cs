using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

public sealed class AdminReviewerAccountService(
    IMemberAccountRepository memberAccounts,
    ILogger<AdminReviewerAccountService> logger)
{
    private readonly PasswordHasher<MemberAccount> passwordHasher = new();

    public Task<IReadOnlyList<LocalPasswordAccountSummary>> ListAsync(
        CancellationToken cancellationToken = default) =>
        memberAccounts.ListLocalPasswordAccountsAsync(cancellationToken);

    public async Task<LocalPasswordAccountSummary?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccounts.FindByIdAsync(id, cancellationToken);
        return account?.PasswordHash is null
            ? null
            : ToSummary(account);
    }

    public async Task<ReviewerAccountResult> CreateAsync(
        string email,
        string displayName,
        string password,
        string adminEmail,
        CancellationToken cancellationToken = default)
    {
        var validated = Validate(email, displayName, password, passwordRequired: true);
        if (validated.Error is not null)
        {
            return ReviewerAccountResult.Failure(validated.Error);
        }

        if (await memberAccounts.FindByEmailAsync(validated.Email, cancellationToken) is not null)
        {
            return ReviewerAccountResult.Failure("An account with that email already exists.");
        }

        var account = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = validated.Email,
            DisplayName = validated.DisplayName,
            CreatedAt = DateTime.UtcNow,
        };
        account.PasswordHash = passwordHasher.HashPassword(account, password);
        var created = await memberAccounts.CreateAsync(account, cancellationToken);

        logger.LogInformation(
            "Admin {AdminEmailFingerprint} created local-password account {MemberId}.",
            LogRedaction.EmailFingerprint(adminEmail),
            created.Id);
        return ReviewerAccountResult.Success(ToSummary(created));
    }

    public async Task<ReviewerAccountResult> UpdateAsync(
        Guid id,
        string email,
        string displayName,
        string? newPassword,
        string adminEmail,
        CancellationToken cancellationToken = default)
    {
        var validated = Validate(email, displayName, newPassword, passwordRequired: false);
        if (validated.Error is not null)
        {
            return ReviewerAccountResult.Failure(validated.Error);
        }

        var account = await memberAccounts.FindByIdAsync(id, cancellationToken);
        if (account?.PasswordHash is null)
        {
            return ReviewerAccountResult.NotFound();
        }

        var duplicate = await memberAccounts.FindByEmailAsync(validated.Email, cancellationToken);
        if (duplicate is not null && duplicate.Id != id)
        {
            return ReviewerAccountResult.Failure("An account with that email already exists.");
        }

        string? passwordHash = null;
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            passwordHash = passwordHasher.HashPassword(account, newPassword);
        }

        var updated = await memberAccounts.UpdateLocalPasswordAccountAsync(
            id,
            validated.Email,
            validated.DisplayName,
            passwordHash,
            cancellationToken);
        if (updated is null)
        {
            return ReviewerAccountResult.NotFound();
        }

        logger.LogInformation(
            "Admin {AdminEmailFingerprint} updated local-password account {MemberId}; password reset: {PasswordReset}.",
            LogRedaction.EmailFingerprint(adminEmail),
            id,
            passwordHash is not null);
        return ReviewerAccountResult.Success(ToSummary(updated));
    }

    public async Task<bool> RemovePasswordAsync(
        Guid id,
        string adminEmail,
        CancellationToken cancellationToken = default)
    {
        var removed = await memberAccounts.RemoveLocalPasswordAsync(id, cancellationToken);
        if (removed)
        {
            logger.LogInformation(
                "Admin {AdminEmailFingerprint} removed local-password access from member {MemberId}.",
                LogRedaction.EmailFingerprint(adminEmail),
                id);
        }

        return removed;
    }

    private static ValidatedInput Validate(
        string email,
        string displayName,
        string? password,
        bool passwordRequired)
    {
        var trimmedEmail = email?.Trim() ?? string.Empty;
        var trimmedDisplayName = displayName?.Trim() ?? string.Empty;
        if (trimmedEmail.Length == 0 || trimmedEmail.Length > 256 || !new EmailAddressAttribute().IsValid(trimmedEmail))
        {
            return ValidatedInput.Invalid("Enter a valid email address of at most 256 characters.");
        }

        if (trimmedDisplayName.Length is < MemberAccountService.MinDisplayNameLength or > MemberAccountService.MaxDisplayNameLength)
        {
            return ValidatedInput.Invalid(
                $"Display name must be between {MemberAccountService.MinDisplayNameLength} and {MemberAccountService.MaxDisplayNameLength} characters.");
        }

        if (passwordRequired && string.IsNullOrWhiteSpace(password))
        {
            return ValidatedInput.Invalid("Password is required.");
        }

        if (!string.IsNullOrEmpty(password) && password.Length < 12)
        {
            return ValidatedInput.Invalid("Password must be at least 12 characters.");
        }

        if (password?.Length > 256)
        {
            return ValidatedInput.Invalid("Password must be at most 256 characters.");
        }

        return new ValidatedInput(trimmedEmail, trimmedDisplayName, null);
    }

    private static LocalPasswordAccountSummary ToSummary(MemberAccount account) =>
        new(account.Id, account.Email, account.DisplayName, account.CreatedAt, account.LastLoginAt, account.IsSuspended);

    private sealed record ValidatedInput(string Email, string DisplayName, string? Error)
    {
        public static ValidatedInput Invalid(string error) => new(string.Empty, string.Empty, error);
    }
}

public sealed record ReviewerAccountResult(
    bool Succeeded,
    bool WasNotFound,
    LocalPasswordAccountSummary? Account,
    string? Error)
{
    public static ReviewerAccountResult Success(LocalPasswordAccountSummary account) =>
        new(true, false, account, null);

    public static ReviewerAccountResult Failure(string error) =>
        new(false, false, null, error);

    public static ReviewerAccountResult NotFound() =>
        new(false, true, null, "Reviewer account not found.");
}
