using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class CheckPhotosCommandTests
{
    [Fact]
    public async Task RunAsync_ReturnsUsageError_WhenOptionsAreInvalid()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"qz-tools-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsPath, """{ "ConnectionStrings": {} }""");

        try
        {
            var exitCode = await CheckPhotosCommand.RunAsync(["--settings-file", settingsPath]);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task RunInventoryCheckAsync_ReturnsZero_WhenNoPhotosMatch()
    {
        var options = ValidOptions();

        var exitCode = await CheckPhotosCommand.RunInventoryCheckAsync(options, []);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunInventoryCheckAsync_ReturnsZeroForDryRun()
    {
        var options = ValidOptions(dryRun: true);
        var photos = SamplePhotos();

        var exitCode = await CheckPhotosCommand.RunInventoryCheckAsync(options, photos);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunInventoryCheckAsync_WritesReportAndHideList_WhenAssetsAreMissing()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"qz-photo-report-{Guid.NewGuid():N}.csv");
        var hideIdsPath = Path.Combine(Path.GetTempPath(), $"qz-photo-hide-{Guid.NewGuid():N}.txt");
        var options = ValidOptions(outputPath: outputPath, hideIdsPath: hideIdsPath);
        var checker = new StubPhotoBlobChecker(url =>
        {
            if (url.EndsWith("missing-main.jpg", StringComparison.Ordinal) ||
                url.EndsWith("t_missing-thumb.jpg", StringComparison.Ordinal))
            {
                return new PhotoBlobProbeResult(false, "404");
            }

            return new PhotoBlobProbeResult(true, "200");
        });

        try
        {
            var exitCode = await CheckPhotosCommand.RunInventoryCheckAsync(
                options,
                SamplePhotos(),
                checker);

            Assert.Equal(1, exitCode);
            var csv = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("pic_id,cat_id,category", csv, StringComparison.Ordinal);
            Assert.Contains("201,12,\"Queen\"", csv, StringComparison.Ordinal);
            Assert.Contains("hide_from_pages", csv, StringComparison.Ordinal);
            var hideIds = await File.ReadAllLinesAsync(hideIdsPath);
            Assert.Equal(["201"], hideIds);
        }
        finally
        {
            File.Delete(outputPath);
            File.Delete(hideIdsPath);
        }
    }

    [Fact]
    public async Task RunInventoryCheckAsync_ReturnsZero_WhenAllAssetsExist()
    {
        var options = ValidOptions();
        var checker = new StubPhotoBlobChecker(_ => new PhotoBlobProbeResult(true, "200"));

        var exitCode = await CheckPhotosCommand.RunInventoryCheckAsync(
            options,
            SamplePhotos(),
            checker);

        Assert.Equal(0, exitCode);
    }

    private sealed class StubPhotoBlobChecker(Func<string, PhotoBlobProbeResult> responder) : IPhotoBlobChecker
    {
        public Task<PhotoBlobProbeResult> CheckAsync(string blobUrl, CancellationToken cancellationToken) =>
            Task.FromResult(responder(blobUrl));
    }

    private static CheckPhotosOptions ValidOptions(
        bool dryRun = false,
        string? outputPath = null,
        string? hideIdsPath = null)
    {
        var args = new List<string> { "--connection-string", "Server=.;Database=test;" };
        if (dryRun)
        {
            args.Add("--dry-run");
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            args.AddRange(["--output", outputPath]);
        }

        if (!string.IsNullOrWhiteSpace(hideIdsPath))
        {
            args.AddRange(["--hide-ids-output", hideIdsPath]);
        }

        var options = CheckPhotosOptions.Parse(args.ToArray());
        Assert.True(options.IsValid);
        return options;
    }

    private static IReadOnlyList<PhotoItem> SamplePhotos() =>
    [
        new PhotoItem(
            PicId: 201,
            CatId: 12,
            CategoryName: "Queen",
            CategorySlug: "queen",
            Title: "Missing main image",
            ImageUrl: "https://cdn.queenzone.org/queen/missing-main.jpg",
            ThumbnailUrl: "https://cdn.queenzone.org/queen/t_missing-main.jpg",
            ThumbWidth: 120,
            ThumbHeight: 90,
            PictureWidth: 800,
            PictureHeight: 600,
            Year: 1985,
            DateTime: new DateTime(1985, 7, 13)),
        new PhotoItem(
            PicId: 202,
            CatId: 12,
            CategoryName: "Queen",
            CategorySlug: "queen",
            Title: "Thumbnail missing only",
            ImageUrl: "https://cdn.queenzone.org/queen/present-main.jpg",
            ThumbnailUrl: "https://cdn.queenzone.org/queen/t_missing-thumb.jpg",
            ThumbWidth: 120,
            ThumbHeight: 90,
            PictureWidth: 1920,
            PictureHeight: 1080,
            Year: 1986,
            DateTime: new DateTime(1986, 8, 9)),
    ];

    [Fact]
    public async Task LoadPhotosAsync_FiltersByCategorySlugAndLimit()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var options = CheckPhotosOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-slug", "queen",
            "--limit", "2",
        ]);
        Assert.True(options.IsValid);

        var photos = await CheckPhotosCommand.LoadPhotosAsync(options, repository);

        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.Equal("queen", photo.CategorySlug, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadPhotosAsync_FiltersByCategoryIdWithoutLimit()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var options = CheckPhotosOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-id", "9",
        ]);
        Assert.True(options.IsValid);

        var photos = await CheckPhotosCommand.LoadPhotosAsync(options, repository);

        Assert.Equal(3, photos.Count);
        Assert.All(photos, photo => Assert.Equal(9, photo.CatId));
    }
}
