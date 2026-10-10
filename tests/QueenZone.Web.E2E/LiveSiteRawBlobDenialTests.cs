using System.Net;
using System.Net.Http;

namespace QueenZone.Web.E2E;

/// <summary>
/// Pure unit checks for raw-blob private-container denial (#2211). No browser.
/// </summary>
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class LiveSiteRawBlobDenialTests
{
    [Test]
    public void IsPrivateContainerRefusal_TrueForResourceNotFound404()
    {
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(
                HttpStatusCode.NotFound,
                LiveSiteRawBlobDenial.ResourceNotFound),
            Is.True);
    }

    [Test]
    public void IsPrivateContainerRefusal_TrueForPublicAccessNotPermitted409()
    {
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(
                HttpStatusCode.Conflict,
                LiveSiteRawBlobDenial.PublicAccessNotPermitted),
            Is.True);
    }

    [Test]
    public void IsPrivateContainerRefusal_FalseForBlobNotFound404()
    {
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(
                HttpStatusCode.NotFound,
                LiveSiteRawBlobDenial.BlobNotFound),
            Is.False);
    }

    [Test]
    public void IsPrivateContainerRefusal_FalseFor404WithoutErrorCode()
    {
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(HttpStatusCode.NotFound, errorCode: null),
            Is.False);
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(HttpStatusCode.NotFound, errorCode: ""),
            Is.False);
    }

    [Test]
    public void IsPrivateContainerRefusal_FalseForMismatchedStatusAndCode()
    {
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(
                HttpStatusCode.Conflict,
                LiveSiteRawBlobDenial.ResourceNotFound),
            Is.False);
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(
                HttpStatusCode.NotFound,
                LiveSiteRawBlobDenial.PublicAccessNotPermitted),
            Is.False);
        Assert.That(
            LiveSiteRawBlobDenial.IsPrivateContainerRefusal(
                HttpStatusCode.Forbidden,
                LiveSiteRawBlobDenial.ResourceNotFound),
            Is.False);
    }

    [Test]
    public void IsPrivateContainerRefusal_ReadsXMsErrorCodeHeader()
    {
        using var privateContainer = new HttpResponseMessage(HttpStatusCode.NotFound);
        privateContainer.Headers.TryAddWithoutValidation(
            LiveSiteRawBlobDenial.ErrorCodeHeaderName,
            LiveSiteRawBlobDenial.ResourceNotFound);

        using var publicMissingBlob = new HttpResponseMessage(HttpStatusCode.NotFound);
        publicMissingBlob.Headers.TryAddWithoutValidation(
            LiveSiteRawBlobDenial.ErrorCodeHeaderName,
            LiveSiteRawBlobDenial.BlobNotFound);

        using var accountClosed = new HttpResponseMessage(HttpStatusCode.Conflict);
        accountClosed.Headers.TryAddWithoutValidation(
            "X-MS-ERROR-CODE",
            LiveSiteRawBlobDenial.PublicAccessNotPermitted);

        Assert.That(LiveSiteRawBlobDenial.IsPrivateContainerRefusal(privateContainer), Is.True);
        Assert.That(LiveSiteRawBlobDenial.IsPrivateContainerRefusal(publicMissingBlob), Is.False);
        Assert.That(LiveSiteRawBlobDenial.IsPrivateContainerRefusal(accountClosed), Is.True);
        Assert.That(
            LiveSiteRawBlobDenial.ReadErrorCode(publicMissingBlob.Headers),
            Is.EqualTo(LiveSiteRawBlobDenial.BlobNotFound));
    }
}
