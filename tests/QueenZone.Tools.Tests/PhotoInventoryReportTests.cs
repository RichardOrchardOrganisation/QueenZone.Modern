using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class PhotoInventoryReportTests
{
    [Fact]
    public void FromAssetResults_GroupsOriginalAndThumbnailPerPhoto()
    {
        var report = PhotoInventoryReport.FromAssetResults(
        [
            new PhotoBlobCheckResult(101, 12, "Queen", "original", "https://queenzoneprod.blob.core.windows.net/queen/a.jpg", true, "200"),
            new PhotoBlobCheckResult(101, 12, "Queen", "thumbnail", "https://queenzoneprod.blob.core.windows.net/queen/t_a.jpg", false, "404"),
            new PhotoBlobCheckResult(102, 12, "Queen", "original", "https://queenzoneprod.blob.core.windows.net/queen/b.jpg", false, "404"),
            new PhotoBlobCheckResult(102, 12, "Queen", "thumbnail", "https://queenzoneprod.blob.core.windows.net/queen/t_b.jpg", false, "404"),
            new PhotoBlobCheckResult(103, 9, "Brian May", "original", "https://queenzoneprod.blob.core.windows.net/brian-may/c.jpg", true, "200"),
            new PhotoBlobCheckResult(103, 9, "Brian May", "thumbnail", "https://queenzoneprod.blob.core.windows.net/brian-may/t_c.jpg", true, "200"),
        ]);

        Assert.Equal(3, report.PhotosChecked);
        Assert.Single(report.BothFound);
        Assert.Equal(103, report.BothFound[0].PicId);
        Assert.Single(report.HideFromPages);
        Assert.Equal(102, report.HideFromPages[0].PicId);
        Assert.Single(report.ThumbnailMissingOnly);
        Assert.Equal(101, report.ThumbnailMissingOnly[0].PicId);
        Assert.True(report.ThumbnailMissingOnly[0].ThumbnailMissingOnly);
        Assert.False(report.ThumbnailMissingOnly[0].HideFromPages);
        Assert.Single(report.BothMissing);
        Assert.Equal(102, report.BothMissing[0].PicId);
    }
}
