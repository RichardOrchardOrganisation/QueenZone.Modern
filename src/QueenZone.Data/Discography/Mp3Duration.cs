namespace QueenZone.Data;

/// <summary>
/// Estimates MPEG audio duration from a file-prefix plus total length.
/// CBR files (legacy fan-stage MP3s) are derived from the first valid frame
/// bitrate; VBR Xing/VBRI headers are not required for this archive.
/// </summary>
public static class Mp3Duration
{
    public const int PrefixBytes = 64 * 1024;

    private static readonly int[] Mpeg1Layer3Kbps =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];

    private static readonly int[] Mpeg2Layer3Kbps =
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

    public static int? TryGetSeconds(ReadOnlySpan<byte> prefix, long fileLengthBytes)
    {
        if (prefix.IsEmpty || fileLengthBytes <= 0)
        {
            return null;
        }

        var searchFrom = SkipId3v2(prefix);
        if (searchFrom < 0 || searchFrom > prefix.Length - 4)
        {
            return null;
        }

        if (!TryFindBitrate(prefix, searchFrom, out var headerOffset, out var bitrateBitsPerSecond))
        {
            return null;
        }

        var audioBytes = fileLengthBytes - headerOffset;
        if (audioBytes <= 0)
        {
            return null;
        }

        var seconds = (int)Math.Round(audioBytes * 8d / bitrateBitsPerSecond, MidpointRounding.AwayFromZero);
        return seconds > 0 ? seconds : null;
    }

    private static int SkipId3v2(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 10
            || prefix[0] != (byte)'I'
            || prefix[1] != (byte)'D'
            || prefix[2] != (byte)'3')
        {
            return 0;
        }

        var size = ((prefix[6] & 0x7F) << 21)
            | ((prefix[7] & 0x7F) << 14)
            | ((prefix[8] & 0x7F) << 7)
            | (prefix[9] & 0x7F);
        var offset = 10 + size;
        return offset < 0 ? 0 : offset;
    }

    private static bool TryFindBitrate(
        ReadOnlySpan<byte> prefix,
        int start,
        out int headerOffset,
        out int bitrateBitsPerSecond)
    {
        headerOffset = 0;
        bitrateBitsPerSecond = 0;
        var last = prefix.Length - 4;
        for (var i = start; i <= last; i++)
        {
            if (prefix[i] != 0xFF || (prefix[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            if (TryParseBitrate(prefix.Slice(i, 4), out bitrateBitsPerSecond))
            {
                headerOffset = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseBitrate(ReadOnlySpan<byte> header, out int bitrateBitsPerSecond)
    {
        bitrateBitsPerSecond = 0;
        var versionId = (header[1] >> 3) & 0b11;
        var layerId = (header[1] >> 1) & 0b11;
        var bitrateIndex = header[2] >> 4;
        if (layerId != 0b01 || bitrateIndex is 0 or 15)
        {
            return false;
        }

        // MPEG-1 = 3, MPEG-2 = 2, MPEG-2.5 = 0. Layer III = 1.
        int[] table = versionId == 0b11 ? Mpeg1Layer3Kbps : Mpeg2Layer3Kbps;
        if (bitrateIndex >= table.Length || table[bitrateIndex] == 0)
        {
            return false;
        }

        bitrateBitsPerSecond = table[bitrateIndex] * 1000;
        return true;
    }
}
