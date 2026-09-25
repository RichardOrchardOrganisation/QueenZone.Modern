using System.Security.Claims;
using QueenZone.Storage;

namespace QueenZone.Web;

internal static class ImageUploadContextFactory
{
    internal static BlobUploadContext Create(ClaimsPrincipal user)
    {
        var email = user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("preferred_username")
            ?? user.Identity?.Name;

        Guid? memberAccountId = null;
        var memberIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(memberIdValue, out var parsed) && parsed != Guid.Empty)
        {
            memberAccountId = parsed;
        }

        return new BlobUploadContext
        {
            ActorEmail = email,
            MemberAccountId = memberAccountId,
        };
    }

    internal static BlobUploadContext WithPreferredBlobName(BlobUploadContext source, string preferredBlobName) =>
        new()
        {
            MemberAccountId = source.MemberAccountId,
            MemberId = source.MemberId,
            ActorEmail = source.ActorEmail,
            PreferredBlobName = preferredBlobName,
        };
}
