using System.Buffers.Text;

namespace QueenZone.Web;

/// <summary>
/// Remembers which Quiz Sprint questions a browser has already answered, so the next round prefers
/// fresh ones. Kept in a cookie (guests and members alike, no account data involved), so each question
/// is stored as a 4-byte key rather than a full GUID to fit roughly seven rounds under the size limit.
/// </summary>
public static class QuizSprintSeenQuestions
{
    public const string CookieName = "qz_sprint_seen";

    /// <summary>At 6 base64url characters each this stays near 2.7 KB, under the 4 KB cookie limit.</summary>
    public const int MaxRemembered = 450;

    private const int KeyChars = 6;

    public static uint Key(Guid questionId) => BitConverter.ToUInt32(questionId.ToByteArray(), 0);

    /// <summary>Keys ordered oldest to newest; empty when the cookie is missing or malformed.</summary>
    public static IReadOnlyList<uint> Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value) || value.Length % KeyChars != 0)
        {
            return [];
        }

        var keys = new List<uint>(value.Length / KeyChars);
        Span<byte> bytes = stackalloc byte[4];
        for (var offset = 0; offset < value.Length; offset += KeyChars)
        {
            if (Base64Url.DecodeFromChars(value.AsSpan(offset, KeyChars), bytes, out _, out var written) != System.Buffers.OperationStatus.Done
                || written != 4)
            {
                return [];
            }

            keys.Add(BitConverter.ToUInt32(bytes));
        }

        return keys;
    }

    /// <summary>Merges <paramref name="answeredQuestionIds"/> in as the newest entries and rewrites the cookie.</summary>
    public static void Remember(HttpContext httpContext, IReadOnlyCollection<Guid> answeredQuestionIds)
    {
        if (answeredQuestionIds.Count == 0)
        {
            return;
        }

        var added = answeredQuestionIds.Select(Key).ToHashSet();
        var merged = Read(httpContext.Request).Where(key => !added.Contains(key)).Concat(added).ToList();
        if (merged.Count > MaxRemembered)
        {
            merged.RemoveRange(0, merged.Count - MaxRemembered);
        }

        var encoded = string.Concat(merged.Select(key => Base64Url.EncodeToString(BitConverter.GetBytes(key))));
        httpContext.Response.Cookies.Append(CookieName, encoded, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(90),
        });
    }
}
