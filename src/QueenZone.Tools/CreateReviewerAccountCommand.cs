using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Tools;

/// <summary>
/// Provisions (or resets the password on) a single local username/password member account.
/// There is no self-service registration UI for this sign-in path — it exists only for
/// accounts that cannot use a social provider (e.g. an App Store/Play Store reviewer),
/// and every account it touches must be created deliberately, out-of-band, by an operator
/// running this command.
/// </summary>
internal static class CreateReviewerAccountCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CreateReviewerAccountOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(options.ConnectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);
        return await RunCoreAsync(options, dbContext);
    }

    internal static async Task<int> RunCoreAsync(CreateReviewerAccountOptions options, QueenZoneDbContext dbContext)
    {
        var normalizedEmail = options.Email.Trim().ToUpperInvariant();
        var account = await dbContext.MemberAccounts
            .SingleOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail);

        var hasher = new PasswordHasher<MemberAccount>();
        var isNewAccount = account is null;
        if (account is null)
        {
            account = new MemberAccount
            {
                Id = Guid.NewGuid(),
                Email = options.Email.Trim(),
                NormalizedEmail = normalizedEmail,
                DisplayName = options.DisplayName,
                CreatedAt = DateTime.UtcNow,
            };
            dbContext.MemberAccounts.Add(account);
        }
        else
        {
            account.DisplayName = options.DisplayName;
            account.IsSuspended = false;
            account.SuspendedAt = null;
            account.SuspendedReason = null;
            account.SuspendedByAdminEmail = null;
        }

        account.PasswordHash = hasher.HashPassword(account, options.Password);

        await dbContext.SaveChangesAsync();

        Console.WriteLine(isNewAccount
            ? $"Created member account '{account.Email}' with a local password."
            : $"Updated the password for existing member account '{account.Email}'.");
        return 0;
    }

    private static void WriteUsage(string errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- create-reviewer-account --email <email> --password <password> --display-name <name> --connection-string <connection-string>",
            "",
            "Connection string can also be supplied with ConnectionStrings__QueenZoneLegacy.",
            "Creates the member account if it does not exist, or resets its password (and un-suspends it) if it does.");
}

internal sealed class CreateReviewerAccountOptions
{
    private CreateReviewerAccountOptions()
    {
    }

    public string Email { get; private init; } = string.Empty;

    public string Password { get; private init; } = string.Empty;

    public string DisplayName { get; private init; } = string.Empty;

    public string ConnectionString { get; private init; } = string.Empty;

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public static CreateReviewerAccountOptions Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "create-reviewer-account", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Command is required.");
        }

        string? email = null;
        string? password = null;
        string? displayName = null;
        string? connectionString = null;
        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--email", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                email = args[++index];
                continue;
            }

            if (string.Equals(arg, "--password", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                password = args[++index];
                continue;
            }

            if (string.Equals(arg, "--display-name", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                displayName = args[++index];
                continue;
            }

            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(email))
        {
            return Invalid("--email is required.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            return Invalid("--password is required and must be at least 12 characters.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string or ConnectionStrings__QueenZoneLegacy is required.");
        }

        return new CreateReviewerAccountOptions
        {
            Email = email,
            Password = password,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "App Reviewer" : displayName,
            ConnectionString = connectionString,
            IsValid = true,
        };
    }

    private static CreateReviewerAccountOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}
