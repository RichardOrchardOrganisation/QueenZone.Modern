using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using QueenZone.Tools.BbCode;

namespace QueenZone.Tools;

/// <summary>
/// Converts literal legacy BBCode markup (e.g. [quote], [b], [i], [u], [url]) still sitting
/// unconverted in ModernForumPost.BodyHtml into sanitized HTML (issue: forum quoting), including
/// any freshly re-imported from the legacy database. Dry-run by default; use --apply to write.
/// Re-running is naturally idempotent: converted rows no longer contain recognized BBCode
/// markers, so BbCodeConverter.ContainsBbCode excludes them from future runs without needing a
/// separate backup/flag column.
/// </summary>
internal static class ConvertLegacyBbCodeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = ConvertLegacyBbCodeOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        var candidates = await LoadCandidatesAsync(options);
        return await RunCoreAsync(options, candidates);
    }

    internal static async Task<int> RunCoreAsync(
        ConvertLegacyBbCodeOptions options,
        IReadOnlyList<BbCodeCandidateRow> candidates)
    {
        Console.WriteLine("Convert legacy BBCode markup in ModernForumPost.BodyHtml");
        Console.WriteLine("========================================================");
        Console.WriteLine($"Candidates (contain a literal '['): {candidates.Count}");
        Console.WriteLine($"Mode: {(options.Apply ? "APPLY (writes enabled)" : "dry-run (no writes)")}");
        Console.WriteLine($"Delay between items: {options.DelayMs}ms");
        Console.WriteLine();

        if (candidates.Count == 0)
        {
            Console.WriteLine("No candidate rows found.");
            return 0;
        }

        var wouldUpdate = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var row in candidates)
        {
            try
            {
                if (!BbCodeConverter.ContainsBbCode(row.BodyHtml))
                {
                    // Coarse SQL filter only checks for a literal '[' — confirm a real marker here.
                    skipped++;
                    Console.WriteLine($"  SKIP id={row.Id}: no recognized BBCode markers");
                    continue;
                }

                var converted = BbCodeConverter.Convert(row.BodyHtml);

                if (converted.Length > BodyHtmlMaxLength)
                {
                    failed++;
                    await Console.Error.WriteLineAsync(
                        $"  FAIL id={row.Id}: converted HTML is {converted.Length} chars, "
                        + $"exceeds the {BodyHtmlMaxLength}-char BodyHtml column limit; needs manual review");
                    continue;
                }

                wouldUpdate++;
                Console.WriteLine($"  {(options.Apply ? "UPDATE" : "PLAN")} id={row.Id} "
                    + $"{row.BodyHtml.Length} chars -> {converted.Length} chars");

                if (options.Apply)
                {
                    await UpdateBodyAsync(options.ConnectionString, row.Id, converted, options.CancellationToken);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                await Console.Error.WriteLineAsync($"  FAIL id={row.Id}: {ex.Message}");
            }

            if (options.DelayMs > 0)
            {
                await Task.Delay(options.DelayMs, options.CancellationToken);
            }
        }

        ToolArgs.WriteBackfillSummary(
            wouldUpdate,
            updated,
            skipped,
            failed,
            options.Apply,
            "Dry-run only. Re-run with --apply to write BodyHtml.");

        return failed == 0 ? 0 : 1;
    }

    private const int BodyHtmlMaxLength = 8000;

    [ExcludeFromCodeCoverage]
    private static async Task<IReadOnlyList<BbCodeCandidateRow>> LoadCandidatesAsync(ConvertLegacyBbCodeOptions options)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(options.CancellationToken);
        await using var command = connection.CreateCommand();

        // Coarse full-scan candidate query (no supporting index for CHARINDEX) can run well
        // past ADO.NET's 30s default on a busier/lower-tier production instance. --limit lets
        // the operator break the whole run into smaller, resumable batches if needed.
        command.CommandTimeout = 180;

        var top = options.Limit is int limit ? $"TOP ({limit}) " : string.Empty;

        // CHARINDEX on a literal '[' (CHAR(91)) is a cheap, escaping-free coarse filter —
        // T-SQL LIKE treats '[' as a wildcard character-class opener, so matching a literal
        // "[quote" via LIKE requires fiddly escaping. Precise marker detection happens in C#
        // via BbCodeConverter.ContainsBbCode against the small candidate set this returns.
        command.CommandText = $"""
            SELECT {top}Id, BodyHtml
            FROM dbo.ModernForumPost
            WHERE CHARINDEX(CHAR(91), BodyHtml) > 0
            ORDER BY Id
            """;

        var rows = new List<BbCodeCandidateRow>();
        await using var reader = await command.ExecuteReaderAsync(options.CancellationToken);
        while (await reader.ReadAsync(options.CancellationToken))
        {
            rows.Add(new BbCodeCandidateRow(reader.GetInt64(0), reader.GetString(1)));
        }

        return rows;
    }

    [ExcludeFromCodeCoverage]
    private static async Task UpdateBodyAsync(
        string connectionString,
        long id,
        string converted,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = """
            UPDATE dbo.ModernForumPost
            SET BodyHtml = @converted
            WHERE Id = @id
            """;
        command.Parameters.Add("@converted", System.Data.SqlDbType.VarChar, 8000).Value = converted;
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void WriteUsage(string? errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- convert-legacy-bbcode [options]",
            "",
            "Options:",
            "  --connection-string <cs>   SQL Server (or ConnectionStrings__QueenZoneLegacy)",
            "  --limit <n>                Max candidate rows to consider",
            "  --delay-ms <n>              Pause between items (default 50)",
            "  --apply                    Write updates (default is dry-run)",
            "",
            "Default: dry-run. Re-running is idempotent — converted rows no longer contain",
            "recognized BBCode markers, so they're skipped on subsequent runs automatically.");
}

internal sealed record BbCodeCandidateRow(long Id, string BodyHtml);

internal sealed class ConvertLegacyBbCodeOptions
{
    private ConvertLegacyBbCodeOptions()
    {
    }

    public string ConnectionString { get; private init; } = string.Empty;

    public int? Limit { get; private init; }

    public bool Apply { get; private init; }

    public int DelayMs { get; private init; } = 50;

    public CancellationToken CancellationToken { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public static ConvertLegacyBbCodeOptions Parse(string[] args)
    {
        string? connectionString = null;
        int? limit = null;
        var apply = false;
        var delayMs = 50;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
                continue;
            }

            if (ToolArgs.TryReadInt(args, ref index, "--limit", 1, out var n, out var nError))
            {
                if (nError is not null)
                {
                    return Invalid(nError);
                }

                limit = n;
                continue;
            }

            if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
                continue;
            }

            if (ToolArgs.TryReadInt(args, ref index, "--delay-ms", 0, out var delay, out var delayError))
            {
                if (delayError is not null)
                {
                    return Invalid(delayError);
                }

                delayMs = delay;
                continue;
            }

            return Invalid($"Unsupported or incomplete argument: {arg}");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string or ConnectionStrings__QueenZoneLegacy is required.");
        }

        return new ConvertLegacyBbCodeOptions
        {
            ConnectionString = connectionString,
            Limit = limit,
            Apply = apply,
            DelayMs = delayMs,
            CancellationToken = CancellationToken.None,
            IsValid = true,
        };
    }

    private static ConvertLegacyBbCodeOptions Invalid(string message) =>
        new()
        {
            ErrorMessage = message,
            IsValid = false,
        };
}
