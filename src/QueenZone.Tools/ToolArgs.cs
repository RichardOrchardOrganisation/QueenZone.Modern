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
}
