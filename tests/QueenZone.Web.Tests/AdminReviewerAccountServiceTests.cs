using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminReviewerAccountServiceTests
{
    private const string AdminEmail = "admin@example.com";
    private const string MemberEmail = "reviewer@example.com";

    [Fact]
    public async Task Create_LogsMemberIdWithoutEmailOrFingerprint()
    {
        var logger = new RecordingLogger();
        var service = new AdminReviewerAccountService(new InMemoryMemberAccountRepository(), logger);

        var result = await service.CreateAsync(
            MemberEmail,
            "Store Reviewer",
            "reviewer-password-12");

        Assert.True(result.Succeeded);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(result.Account!.Id.ToString(), entry.Message, StringComparison.Ordinal);
        AssertNoEmailOrFingerprint(entry.Message, AdminEmail, MemberEmail);
    }

    [Fact]
    public async Task Update_LogsMemberIdAndPasswordResetWithoutEmailOrFingerprint()
    {
        var logger = new RecordingLogger();
        var members = new InMemoryMemberAccountRepository();
        var service = new AdminReviewerAccountService(members, logger);
        var created = await service.CreateAsync(
            MemberEmail,
            "Store Reviewer",
            "reviewer-password-12");
        logger.Entries.Clear();

        var result = await service.UpdateAsync(
            created.Account!.Id,
            "updated-reviewer@example.com",
            "Updated Reviewer",
            "replacement-password-12");

        Assert.True(result.Succeeded);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains(created.Account.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains("password reset: True", entry.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoEmailOrFingerprint(
            entry.Message,
            AdminEmail,
            MemberEmail,
            "updated-reviewer@example.com");
    }

    [Fact]
    public async Task RemovePassword_LogsMemberIdWithoutEmailOrFingerprint()
    {
        var logger = new RecordingLogger();
        var service = new AdminReviewerAccountService(new InMemoryMemberAccountRepository(), logger);
        var created = await service.CreateAsync(
            MemberEmail,
            "Store Reviewer",
            "reviewer-password-12");
        logger.Entries.Clear();

        var removed = await service.RemovePasswordAsync(created.Account!.Id);

        Assert.True(removed);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains(created.Account.Id.ToString(), entry.Message, StringComparison.Ordinal);
        AssertNoEmailOrFingerprint(entry.Message, AdminEmail, MemberEmail);
    }

    private static void AssertNoEmailOrFingerprint(string message, params string[] emails)
    {
        foreach (var email in emails)
        {
            Assert.DoesNotContain(email, message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(EmailFingerprint(email), message, StringComparison.Ordinal);
        }
    }

    private static string EmailFingerprint(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash)[..12];
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
