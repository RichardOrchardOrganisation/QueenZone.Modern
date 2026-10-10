using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace QueenZone.Web.E2E;

/// <summary>
/// Distinguishes a private Azure blob container refusal from a public
/// container that simply lacks the probe blob (#2211).
/// </summary>
/// <remarks>
/// A missing blob on a public container returns 404 <c>BlobNotFound</c>.
/// Anonymous access to a private container returns 404 <c>ResourceNotFound</c>
/// while the account still allows public access, or 409
/// <c>PublicAccessNotPermitted</c> when the account flag is off.
/// </remarks>
internal static class LiveSiteRawBlobDenial
{
    public const string ErrorCodeHeaderName = "x-ms-error-code";
    public const string ResourceNotFound = "ResourceNotFound";
    public const string PublicAccessNotPermitted = "PublicAccessNotPermitted";
    public const string BlobNotFound = "BlobNotFound";

    public static bool IsPrivateContainerRefusal(HttpStatusCode status, string? errorCode)
    {
        if (status == HttpStatusCode.NotFound
            && string.Equals(errorCode, ResourceNotFound, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return status == HttpStatusCode.Conflict
            && string.Equals(errorCode, PublicAccessNotPermitted, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPrivateContainerRefusal(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return IsPrivateContainerRefusal(response.StatusCode, ReadErrorCode(response.Headers));
    }

    public static string? ReadErrorCode(HttpHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return headers.TryGetValues(ErrorCodeHeaderName, out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
