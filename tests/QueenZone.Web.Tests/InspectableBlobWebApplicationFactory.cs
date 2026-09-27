namespace QueenZone.Web.Tests;

/// <summary>
/// Testing host with the external-cookie test handler and an inspectable in-memory blob backend.
/// Use as <c>IClassFixture</c> when a class needs only this variant.
/// </summary>
public sealed class InspectableBlobWebApplicationFactory : VariantWebApplicationFactory
{
    public InspectableBlobWebApplicationFactory()
        : base(WebHostVariants.ExternalCookieInspectableBlob)
    {
    }
}
