using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminReviewerAccountServiceTests
{
    private const string AdminEmail = "admin@example.com";
    private const string MemberEmail = "reviewer@example.com";

    [Fact]
    public async Task Create_LogsFingerprintAndMemberIdWithoutCleartextEmails()
    {
        var logger = new RecordingLogger();
        var service = new AdminReviewerAccountService(new InMemoryMemberAccountRepository(), logger);

        var result = await service.CreateAsync(
            MemberEmail,
            "Store Reviewer",
            "reviewer-password-12",
            AdminEmail);

        Assert.True(result.Succeeded);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(LogRedaction.EmailFingerprint(AdminEmail), entry.Message, StringComparison.Ordinal);
        Assert.Contains(result.Account!.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MemberEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_LogsFingerprintMemberIdAndPasswordResetWithoutCleartextEmails()
    {
        var logger = new RecordingLogger();
        var members = new InMemoryMemberAccountRepository();
        var service = new AdminReviewerAccountService(members, logger);
        var created = await service.CreateAsync(
            MemberEmail,
            "Store Reviewer",
            "reviewer-password-12",
            AdminEmail);
        logger.Entries.Clear();

        var result = await service.UpdateAsync(
            created.Account!.Id,
            "updated-reviewer@example.com",
            "Updated Reviewer",
            "replacement-password-12",
            AdminEmail);

        Assert.True(result.Succeeded);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains(LogRedaction.EmailFingerprint(AdminEmail), entry.Message, StringComparison.Ordinal);
        Assert.Contains(created.Account.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains("password reset: True", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AdminEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MemberEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updated-reviewer@example.com", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemovePassword_LogsFingerprintAndMemberIdWithoutCleartextEmails()
    {
        var logger = new RecordingLogger();
        var service = new AdminReviewerAccountService(new InMemoryMemberAccountRepository(), logger);
        var created = await service.CreateAsync(
            MemberEmail,
            "Store Reviewer",
            "reviewer-password-12",
            AdminEmail);
        logger.Entries.Clear();

        var removed = await service.RemovePasswordAsync(created.Account!.Id, AdminEmail);

        Assert.True(removed);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains(LogRedaction.EmailFingerprint(AdminEmail), entry.Message, StringComparison.Ordinal);
        Assert.Contains(created.Account.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MemberEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingLogger : ILogger<AdminReviewerAccountService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
