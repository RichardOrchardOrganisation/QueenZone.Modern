using QueenZone.Data;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class PhotoDimInventoryCommandTests
{
    [Fact]
    public void Parse_RequiresConnectionString()
    {
        WithNoLegacyConnectionString(() =>
        {
            var options = PhotoDimInventoryOptions.Parse([]);
            Assert.False(options.IsValid);
            Assert.Contains("connection", options.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Parse_AcceptsFilters()
    {
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-slug", "brian-may",
            "--limit", "5",
            "--output", "report.txt",
        ]);

        Assert.True(options.IsValid);
        Assert.Equal("brian-may", options.CategorySlug);
        Assert.Equal(5, options.Limit);
        Assert.Equal("report.txt", options.OutputPath);
    }

    [Fact]
    public void Parse_AcceptsCategoryId()
    {
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-id", "12",
        ]);

        Assert.True(options.IsValid);
        Assert.Equal(12, options.CategoryId);
    }

    [Theory]
    [InlineData("--category-id", "abc", "--category-id must be an integer.")]
    [InlineData("--limit", "0", "--limit must be a positive integer.")]
    [InlineData("--limit", "nope", "--limit must be a positive integer.")]
    public void Parse_RejectsInvalidIntegers(string flag, string value, string expected)
    {
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            flag,
            value,
        ]);

        Assert.False(options.IsValid);
        Assert.Equal(expected, options.ErrorMessage);
    }

    [Fact]
    public void Parse_RejectsUnknownArgument()
    {
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--unknown",
        ]);

        Assert.False(options.IsValid);
        Assert.Contains("Unsupported or incomplete argument", options.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PrintsUsage_WhenOptionsAreInvalid()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = await WithNoLegacyConnectionStringAsync(() => PhotoDimInventoryCommand.RunAsync([]));

            Assert.Equal(2, exitCode);
            var text = error.ToString();
            Assert.Contains("photo-dim-inventory", text, StringComparison.Ordinal);
            Assert.Contains("--category-id", text, StringComparison.Ordinal);
            Assert.Contains("Read-only", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ToolsApp_RoutesPhotoDimInventoryToUsageError()
    {
        var exitCode = await WithNoLegacyConnectionStringAsync(() => ToolsApp.RunAsync(["photo-dim-inventory"]));
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task LoadPublicPhotosAsync_UsesRepositoryAndLimit()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-slug", "brian-may",
            "--limit", "2",
        ]);

        var photos = await PhotoDimInventoryCommand.LoadPublicPhotosAsync(options, repository);
        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.Equal("brian-may", photo.CategorySlug));
    }

    [Fact]
    public async Task LoadPublicPhotosAsync_FiltersByCategoryId()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-id", "18",
        ]);

        var photos = await PhotoDimInventoryCommand.LoadPublicPhotosAsync(options, repository);
        Assert.Equal(4, photos.Count);
        Assert.All(photos, photo => Assert.Equal(18, photo.CatId));
    }

    [Fact]
    public async Task BuildReportAsync_UsesRepositoryOverride()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var options = PhotoDimInventoryOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-id", "9",
        ]);

        var report = await PhotoDimInventoryCommand.BuildReportAsync(options, repository);

        Assert.Equal(3, report.TotalPublic);
        Assert.Equal(2, report.UsableDimensions);
        Assert.Equal(1, report.MissingOrZeroDimensions);
    }

    private static void WithNoLegacyConnectionString(Action action)
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }

    private static async Task<T> WithNoLegacyConnectionStringAsync<T>(Func<Task<T>> action)
    {
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
        }
    }
}
