using QueenZone.Data;

namespace QueenZone.Web.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class PhotoImageUrlTests
{
    [Fact]
    public void Build_UsesPublicPicturesBaseUrl() =>
        Assert.Equal(
            "https://cdn.queenzone.org/brian-may/img-101.jpg",
            PhotoImageUrl.Build("/Brian_May/img-101.jpg"));

    [Fact]
    public void BuildBlobStorageUrl_UsesBlobEndpointAndFolderMapping() =>
        Assert.Equal(
            "https://queenzoneprod.blob.core.windows.net/brian-may/img-101.jpg",
            PhotoImageUrl.BuildBlobStorageUrl("/Brian_May/img-101.jpg"));

    [Fact]
    public void BuildBlobStorageUrl_AllowsCustomEndpoint() =>
        Assert.Equal(
            "https://example.test/queen/img-201.jpg",
            PhotoImageUrl.BuildBlobStorageUrl("/Queen/img-201.jpg", "https://example.test"));

    [Fact]
    public void Dev_environment_origins_override_production_defaults()
    {
        var previousPublic = Environment.GetEnvironmentVariable(PhotoImageUrl.PublicBaseUrlEnvironmentVariable);
        var previousBlob = Environment.GetEnvironmentVariable(PhotoImageUrl.BlobEndpointEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(PhotoImageUrl.PublicBaseUrlEnvironmentVariable, "https://queenzonedev.blob.core.windows.net");
            Environment.SetEnvironmentVariable(PhotoImageUrl.BlobEndpointEnvironmentVariable, "https://queenzonedev.blob.core.windows.net");

            Assert.Equal(
                "https://queenzonedev.blob.core.windows.net/queen/img-201.jpg",
                PhotoImageUrl.Build("/Queen/img-201.jpg"));
            Assert.Equal(
                "https://queenzonedev.blob.core.windows.net/queen/img-201.jpg",
                PhotoImageUrl.ToBlobStorageUrl("/Queen/img-201.jpg"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PhotoImageUrl.PublicBaseUrlEnvironmentVariable, previousPublic);
            Environment.SetEnvironmentVariable(PhotoImageUrl.BlobEndpointEnvironmentVariable, previousBlob);
        }
    }

    [Fact]
    public void ToBlobStorageUrl_ConvertsPublicUrlToBlobEndpoint() =>
        Assert.Equal(
            "https://queenzoneprod.blob.core.windows.net/queen/img-201.jpg",
            PhotoImageUrl.ToBlobStorageUrl("https://cdn.queenzone.org/queen/img-201.jpg"));

    [Fact]
    public void ToBlobStorageUrl_FallsBackToLegacyPathMapping() =>
        Assert.Equal(
            "https://queenzoneprod.blob.core.windows.net/multimedia/photo.jpg",
            PhotoImageUrl.ToBlobStorageUrl("/Multimedia/photo.jpg"));

    [Fact]
    public void BuildBlobStorageUrl_HandlesSingleSegmentLegacyPath() =>
        Assert.Equal(
            "https://queenzoneprod.blob.core.windows.net/photo.jpg",
            PhotoImageUrl.BuildBlobStorageUrl("photo.jpg"));

    [Theory]
    [InlineData("https://cdn.queenzone.org/queen/img.jpg", "queen", "img.jpg")]
    [InlineData("https://queenzoneprod.blob.core.windows.net/multimedia/t_123.jpg", "multimedia", "t_123.jpg")]
    [InlineData("https://cdn.queenzone.org/freddie-mercury/claching%20colors%20(urk).jpg", "freddie-mercury", "claching colors (urk).jpg")]
    public void TryParseBlobLocation_ParsesContainerAndBlobName(string url, string container, string blobName)
    {
        var parsed = PhotoImageUrl.TryParseBlobLocation(url, out var actualContainer, out var actualBlobName);

        Assert.True(parsed);
        Assert.Equal(container, actualContainer);
        Assert.Equal(blobName, actualBlobName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/Multimedia/photo.jpg")]
    [InlineData("https://queenzoneprod.blob.core.windows.net/only-container")]
    public void TryParseBlobLocation_RejectsInvalidUrls(string url)
    {
        var parsed = PhotoImageUrl.TryParseBlobLocation(url, out _, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Build_AliasesUsQueenConventionFolder_ToActualContainer()
    {
        Assert.Equal(
            "https://cdn.queenzone.org/us-convention-2001/121120014455.jpg",
            PhotoImageUrl.Build("/US_Queen_Convention_2001/121120014455.jpg"));
        Assert.Equal(
            "https://cdn.queenzone.org/us-convention-2001/121120014455.jpg",
            PhotoImageUrl.Build("/US_queen_Convention_2001/121120014455.jpg"));
    }

    [Fact]
    public void TryParseBlobLocation_NormalizesAliasedContainer()
    {
        Assert.True(PhotoImageUrl.TryParseBlobLocation(
            "https://cdn.queenzone.org/us-queen-convention-2001/121120014455.jpg",
            out var container,
            out var blobName));
        Assert.Equal("us-convention-2001", container);
        Assert.Equal("121120014455.jpg", blobName);
    }
}
