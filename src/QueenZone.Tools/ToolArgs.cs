namespace QueenZone.Tools;

/// <summary>Shared `--flag value` argument-parsing helper for Tools commands.</summary>
internal static class ToolArgs
{
    public static bool TryReadValue(string[] args, ref int index, string name, out string value)
    {
        value = string.Empty;
        if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (index + 1 >= args.Length)
        {
            return false;
        }

        value = args[++index];
        return true;
    }

    /// <summary>Reads the `--connection-string`, `--storage-connection-string` and `--settings-file` options shared by Tools commands.</summary>
    public static bool TryReadCommonOption(
        string[] args,
        ref int index,
        ref string? connectionString,
        ref string? storageConnectionString,
        ref string? settingsFile)
    {
        if (TryReadValue(args, ref index, "--connection-string", out var connectionStringValue))
        {
            connectionString = connectionStringValue;
            return true;
        }

        if (TryReadValue(args, ref index, "--storage-connection-string", out var storageConnectionStringValue))
        {
            storageConnectionString = storageConnectionStringValue;
            return true;
        }

        if (TryReadValue(args, ref index, "--settings-file", out var settingsFileValue))
        {
            settingsFile = settingsFileValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads an integer `--flag value` option. Returns true when the flag matched; <paramref name="error"/> is set when
    /// the value is not an integer or is below <paramref name="minValue"/>.
    /// </summary>
    public static bool TryReadInt(
        string[] args,
        ref int index,
        string name,
        int? minValue,
        out int value,
        out string? error)
    {
        value = 0;
        error = null;
        if (!TryReadValue(args, ref index, name, out var raw))
        {
            return false;
        }

        if (!int.TryParse(raw, out value) || value < minValue)
        {
            error = minValue switch
            {
                null => $"{name} must be an integer.",
                1 => $"{name} must be a positive integer.",
                _ => $"{name} must be >= {minValue}.",
            };
        }

        return true;
    }

    /// <summary>Writes the optional error message followed by the usage lines to stderr.</summary>
    public static void WriteUsage(string? errorMessage, params string[] lines)
    {
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
        }

        foreach (var line in lines)
        {
            Console.Error.WriteLine(line);
        }
    }

    /// <summary>Writes the shared trailer of the dry-run-by-default backfill commands.</summary>
    public static void WriteBackfillSummary(
        int wouldUpdate,
        int updated,
        int skipped,
        int failed,
        bool apply,
        string dryRunHint)
    {
        Console.WriteLine();
        Console.WriteLine($"Would update / planned: {wouldUpdate}");
        Console.WriteLine($"Updated: {updated}");
        Console.WriteLine($"Skipped: {skipped}");
        Console.WriteLine($"Failed: {failed}");
        if (!apply && wouldUpdate > 0)
        {
            Console.WriteLine(dryRunHint);
        }
    }
}
