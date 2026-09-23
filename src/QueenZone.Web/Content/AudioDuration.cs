using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Null-safe duration estimate for pending fan-performance audio (MPEG or FLAC).
/// Never throws: unreadable prefixes store <c>null</c> and still accept the upload
/// when MIME/size validation already passed.
/// </summary>
internal static class AudioDuration
{
    public static int? TryGetSeconds(
        string? sniffedContentType,
        ReadOnlySpan<byte> prefix,
        long fileLengthBytes)
    {
        try
        {
            if (IsMpeg(sniffedContentType))
            {
                return Mp3Duration.TryGetSeconds(prefix, fileLengthBytes);
            }

            if (IsFlac(sniffedContentType))
            {
                return FlacDuration.TryGetSeconds(prefix);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsMpeg(string? contentType) =>
        contentType is not null
        && (contentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase));

    private static bool IsFlac(string? contentType) =>
        contentType is not null
        && (contentType.Equals("audio/flac", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("audio/x-flac", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Minimal FLAC STREAMINFO parser. Reads sample rate and total samples from the
/// first metadata block; unknown or truncated streams return <c>null</c>.
/// </summary>
internal static class FlacDuration
{
    public static int? TryGetSeconds(ReadOnlySpan<byte> prefix)
    {
        try
        {
            // "fLaC" + 4-byte block header + STREAMINFO through total-samples (offset 25).
            if (prefix.Length < 26
                || prefix[0] != (byte)'f'
                || prefix[1] != (byte)'L'
                || prefix[2] != (byte)'a'
                || prefix[3] != (byte)'C')
            {
                return null;
            }

            var blockType = prefix[4] & 0x7F;
            if (blockType != 0)
            {
                return null;
            }

            var blockLength = (prefix[5] << 16) | (prefix[6] << 8) | prefix[7];
            if (blockLength < 18)
            {
                return null;
            }

            var sampleRate = (prefix[18] << 12) | (prefix[19] << 4) | (prefix[20] >> 4);
            if (sampleRate <= 0)
            {
                return null;
            }

            var totalSamples =
                ((long)(prefix[21] & 0x0F) << 32)
                | ((long)prefix[22] << 24)
                | ((long)prefix[23] << 16)
                | ((long)prefix[24] << 8)
                | prefix[25];
            if (totalSamples <= 0)
            {
                return null;
            }

            var seconds = (int)Math.Round(totalSamples / (double)sampleRate, MidpointRounding.AwayFromZero);
            return seconds > 0 ? seconds : null;
        }
        catch
        {
            return null;
        }
    }
}
