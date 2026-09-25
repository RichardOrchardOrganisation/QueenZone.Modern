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
}
