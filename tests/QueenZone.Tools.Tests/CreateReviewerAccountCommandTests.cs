using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class CreateReviewerAccountCommandTests
{
    [Fact]
    public void Parse_WithAllRequiredArguments_Succeeds()
    {
        var options = CreateReviewerAccountOptions.Parse([
            "create-reviewer-account",
            "--email", "reviewer@example.com",
            "--password", "a-long-enough-password",
            "--display-name", "App Reviewer",
            "--connection-string", "Server=.;Database=QueenZone;",
        ]);

        Assert.True(options.IsValid);
        Assert.Equal("reviewer@example.com", options.Email);
        Assert.Equal("a-long-enough-password", options.Password);
        Assert.Equal("App Reviewer", options.DisplayName);
        Assert.Equal("Server=.;Database=QueenZone;", options.ConnectionString);
    }

    [Fact]
    public void Parse_WithoutDisplayName_DefaultsToAppReviewer()
    {
        var options = CreateReviewerAccountOptions.Parse([
            "create-reviewer-account",
            "--email", "reviewer@example.com",
            "--password", "a-long-enough-password",
            "--connection-string", "Server=.;Database=QueenZone;",
        ]);

        Assert.True(options.IsValid);
        Assert.Equal("App Reviewer", options.DisplayName);
    }

    [Fact]
    public void Parse_WithoutEmail_IsInvalid()
    {
        var options = CreateReviewerAccountOptions.Parse([
            "create-reviewer-account",
            "--password", "a-long-enough-password",
            "--connection-string", "Server=.;Database=QueenZone;",
        ]);

        Assert.False(options.IsValid);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    public void Parse_WithTooShortPassword_IsInvalid(string password)
    {
        var options = CreateReviewerAccountOptions.Parse([
            "create-reviewer-account",
            "--email", "reviewer@example.com",
            "--password", password,
            "--connection-string", "Server=.;Database=QueenZone;",
        ]);

        Assert.False(options.IsValid);
    }

    [Fact]
    public void Parse_WithoutConnectionString_IsInvalid()
    {
        var originalEnv = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var options = CreateReviewerAccountOptions.Parse([
                "create-reviewer-account",
                "--email", "reviewer@example.com",
                "--password", "a-long-enough-password",
            ]);

            Assert.False(options.IsValid);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", originalEnv);
        }
    }

    [Fact]
    public async Task ToolsApp_default_usage_includes_create_reviewer_account_command()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync([]);

            Assert.Equal(2, exitCode);
            Assert.Contains("create-reviewer-account", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ToolsApp_routes_create_reviewer_account_command_to_usage_error_on_invalid_options()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await ToolsApp.RunAsync(["create-reviewer-account"]);

            Assert.Equal(2, exitCode);
            Assert.Contains("--email is required.", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunCoreAsync_WithNewEmail_CreatesAccountWithHashedPassword()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await using var dbContext = CreateSqliteDbContext(connection);
        var options = CreateReviewerAccountOptions.Parse([
            "create-reviewer-account",
            "--email", "new-reviewer@example.com",
            "--password", "a-long-enough-password",
            "--display-name", "App Reviewer",
            "--connection-string", "unused",
        ]);
        Assert.True(options.IsValid);

        var exitCode = await CreateReviewerAccountCommand.RunCoreAsync(options, dbContext);

        Assert.Equal(0, exitCode);
        var account = await dbContext.MemberAccounts.SingleAsync(a => a.Email == "new-reviewer@example.com");
        Assert.Equal("App Reviewer", account.DisplayName);
        Assert.NotNull(account.PasswordHash);
        var hasher = new PasswordHasher<MemberAccount>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(account, account.PasswordHash!, "a-long-enough-password"));
    }

    [Fact]
    public async Task RunCoreAsync_WithExistingSuspendedAccount_ResetsPasswordAndUnsuspends()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await using var dbContext = CreateSqliteDbContext(connection);
        var hasher = new PasswordHasher<MemberAccount>();
        var existing = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "existing-reviewer@example.com",
            NormalizedEmail = "EXISTING-REVIEWER@EXAMPLE.COM",
            DisplayName = "Old Name",
            CreatedAt = DateTime.UtcNow,
            IsSuspended = true,
            SuspendedAt = DateTime.UtcNow,
            SuspendedReason = "bot suspicion",
            SuspendedByAdminEmail = "admin@example.com",
        };
        existing.PasswordHash = hasher.HashPassword(existing, "old-password-value");
        dbContext.MemberAccounts.Add(existing);
        await dbContext.SaveChangesAsync();

        var options = CreateReviewerAccountOptions.Parse([
            "create-reviewer-account",
            "--email", "existing-reviewer@example.com",
            "--password", "brand-new-password",
            "--display-name", "App Reviewer",
            "--connection-string", "unused",
        ]);
        Assert.True(options.IsValid);

        var exitCode = await CreateReviewerAccountCommand.RunCoreAsync(options, dbContext);

        Assert.Equal(0, exitCode);
        var account = await dbContext.MemberAccounts.SingleAsync(a => a.Id == existing.Id);
        Assert.False(account.IsSuspended);
        Assert.Null(account.SuspendedAt);
        Assert.Null(account.SuspendedReason);
        Assert.Null(account.SuspendedByAdminEmail);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(account, account.PasswordHash!, "brand-new-password"));
    }

    private static QueenZoneDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}
