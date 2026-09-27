using System.Buffers;
using System.Text;

namespace QueenZone.Storage;

/// <summary>
/// Best-effort content-type detection from leading file bytes (magic numbers).
/// </summary>
internal static class BlobContentSniffer
{
    /// <summary>OLE compound document (legacy .doc/.xls/.ppt). Not a public MIME type.</summary>
    public const string OleCompoundContentType = "application/x-cfbf";

    public static string? TryDetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 8
            && header[0] == 0x89
            && header[1] == 0x50
            && header[2] == 0x4E
            && header[3] == 0x47
            && header[4] == 0x0D
            && header[5] == 0x0A
            && header[6] == 0x1A
            && header[7] == 0x0A)
        {
            return "image/png";
        }

        if (header.Length >= 6
            && header[0] == 0x47
            && header[1] == 0x49
            && header[2] == 0x46
            && header[3] == 0x38
            && (header[4] == 0x37 || header[4] == 0x39)
            && header[5] == 0x61)
        {
            return "image/gif";
        }

        // RIFF....WEBP
        if (header.Length >= 12
            && header[0] == 0x52
            && header[1] == 0x49
            && header[2] == 0x46
            && header[3] == 0x46
            && header[8] == 0x57
            && header[9] == 0x45
            && header[10] == 0x42
            && header[11] == 0x50)
        {
            return "image/webp";
        }

        // TIFF little-endian (II*\0) or big-endian (MM\0*)
        if (header.Length >= 4
            && ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                || (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
        {
            return "image/tiff";
        }

        // %PDF
        if (header.Length >= 4
            && header[0] == 0x25
            && header[1] == 0x50
            && header[2] == 0x44
            && header[3] == 0x46)
        {
            return "application/pdf";
        }

        // ZIP local file header (also used by docx/xlsx/odt packages).
        if (header.Length >= 4
            && header[0] == 0x50
            && header[1] == 0x4B
            && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
            && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08))
        {
            return "application/zip";
        }

        // ID3v2 tag (MP3). JPEG already returned above (0xFF 0xD8 0xFF).
        if (header.Length >= 3
            && header[0] == (byte)'I'
            && header[1] == (byte)'D'
            && header[2] == (byte)'3')
        {
            return "audio/mpeg";
        }

        // FLAC stream marker.
        if (header.Length >= 4
            && header[0] == (byte)'f'
            && header[1] == (byte)'L'
            && header[2] == (byte)'a'
            && header[3] == (byte)'C')
        {
            return "audio/flac";
        }

        if (HasMpegFrameSync(header))
        {
            return "audio/mpeg";
        }

        // OLE compound file (legacy Word/Excel/PowerPoint). Checked before text so
        // embedded NULs are not required for a positive signature.
        if (header.Length >= 8
            && header[0] == 0xD0
            && header[1] == 0xCF
            && header[2] == 0x11
            && header[3] == 0xE0
            && header[4] == 0xA1
            && header[5] == 0xB1
            && header[6] == 0x1A
            && header[7] == 0xE1)
        {
            return OleCompoundContentType;
        }

        if (IsConservativePlainText(header))
        {
            return "text/plain";
        }

        return null;
    }

    /// <summary>
    /// Plain text has no magic number. Accept only a header with no NUL in the first 64 bytes
    /// that is valid UTF-8 or Windows-1252.
    /// </summary>
    private static bool IsConservativePlainText(ReadOnlySpan<byte> header)
    {
        if (header.IsEmpty)
        {
            return false;
        }

        var window = header.Length > 64 ? header[..64] : header;
        if (window.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        return IsValidUtf8(window) || IsValidWindows1252(window);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> header)
    {
        var remaining = header;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(remaining, out _, out var consumed);
            if (status == OperationStatus.Done)
            {
                remaining = remaining[consumed..];
                continue;
            }

            // The sniff window can split a trailing multibyte character.
            return status == OperationStatus.NeedMoreData;
        }

        return true;
    }

    /// <summary>
    /// Windows-1252 defines every byte except 0x81, 0x8D, 0x8F, 0x90, and 0x9D.
    /// </summary>
    private static bool IsValidWindows1252(ReadOnlySpan<byte> header)
    {
        foreach (var value in header)
        {
            if (value is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMpegFrameSync(ReadOnlySpan<byte> header)
    {
        var last = header.Length - 2;
        for (var i = 0; i <= last; i++)
        {
            if (header[i] != 0xFF || (header[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var version = (header[i + 1] >> 3) & 0b11;
            var layer = (header[i + 1] >> 1) & 0b11;
            if (version != 0b01 && layer != 0b00)
            {
                return true;
            }
        }

        return false;
    }

    public static string? GuessContentTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            _ => null,
        };
    }
}
