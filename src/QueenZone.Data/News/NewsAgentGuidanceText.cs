using System.Security.Cryptography;
using System.Text;

namespace QueenZone.Data;

public static class NewsAgentGuidanceText
{
    public static string Sanitize(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(content.Length);
        foreach (var ch in content)
        {
            if (ch is '\n' or '\r' or '\t' || !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    public static string ComputeContentHash(string sanitizedContent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sanitizedContent ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToStorageType(NewsAgentGuidanceType type) =>
        type switch
        {
            NewsAgentGuidanceType.Triage => "triage",
            NewsAgentGuidanceType.Draft => "draft",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown guidance type.")
        };

    public static NewsAgentGuidanceType ParseType(string value)
    {
        if (string.Equals(value, "triage", StringComparison.OrdinalIgnoreCase))
        {
            return NewsAgentGuidanceType.Triage;
        }

        if (string.Equals(value, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return NewsAgentGuidanceType.Draft;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown guidance type.");
    }
}
