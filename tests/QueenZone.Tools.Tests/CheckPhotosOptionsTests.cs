using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class CheckPhotosOptionsTests
{
    [Fact]
    public void Parse_ReturnsInvalid_WhenConnectionStringMissing()
    {
        var settingsPath = WriteSettingsFile("""{ "ConnectionStrings": {} }""");
        try
        {
            var options = CheckPhotosOptions.Parse(
            [
                "--settings-file", settingsPath,
                "--limit", "5",
            ]);

            Assert.False(options.IsValid);
            Assert.Contains("legacy SQL connection string", options.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Parse_AcceptsExplicitConnectionStringAndLimits()
    {
        var options = CheckPhotosOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--category-id", "12",
            "--category-slug", "queen",
            "--limit", "25",
            "--concurrency", "4",
            "--method", "http",
            "--blob-endpoint", "https://example.blob.core.windows.net",
            "--timeout", "15",
            "--output", "report.csv",
            "--hide-ids-output", "hide.txt",
        ]);

        Assert.True(options.IsValid);
        Assert.Equal("Server=.;Database=test;", options.ConnectionString);
        Assert.Equal(12, options.CategoryId);
        Assert.Equal("queen", options.CategorySlug);
        Assert.Equal(25, options.Limit);
        Assert.Equal(4, options.Concurrency);
        Assert.Equal(PhotoCheckMethod.Http, options.Method);
        Assert.Equal("https://example.blob.core.windows.net", options.BlobEndpoint);
        Assert.Equal(15, options.HttpTimeout);
        Assert.Equal("report.csv", options.OutputPath);
        Assert.Equal("hide.txt", options.HideIdsOutputPath);
    }

    [Theory]
    [InlineData("--category-id", "nope", "--category-id must be an integer.")]
    [InlineData("--limit", "0", "--limit must be a positive integer.")]
    [InlineData("--concurrency", "-2", "--concurrency must be a positive integer.")]
    [InlineData("--timeout", "abc", "--timeout must be a positive integer.")]
    public void Parse_RejectsInvalidIntegers(string flag, string value, string expected)
    {
        var options = CheckPhotosOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            flag,
            value,
        ]);

        Assert.False(options.IsValid);
        Assert.Equal(expected, options.ErrorMessage);
    }

    [Fact]
    public void Parse_RequiresStorageConnectionStringForBlobMethod()
    {
        var settingsPath = WriteSettingsFile(
            """
            {
              "ConnectionStrings": {
                "QueenZoneLegacyLive": "Server=.;Database=test;"
              }
            }
            """);

        try
        {
            var options = CheckPhotosOptions.Parse(
            [
                "--settings-file", settingsPath,
                "--method", "blob",
            ]);

            Assert.False(options.IsValid);
            Assert.Contains("blob storage connection string", options.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Parse_LoadsSettingsFromExplicitSettingsFile()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"qz-tools-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "ConnectionStrings": {
                "QueenZoneLegacyLive": "Server=live;",
                "BlobStorage": "UseDevelopmentStorage=true"
              }
            }
            """);

        try
        {
            var options = CheckPhotosOptions.Parse(
            [
                "--settings-file", settingsPath,
                "--method", "blob",
            ]);

            Assert.True(options.IsValid);
            Assert.Equal("Server=live;", options.ConnectionString);
            Assert.Equal("UseDevelopmentStorage=true", options.StorageConnectionString);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    private static string WriteSettingsFile(string contents)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"qz-tools-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsPath, contents);
        return settingsPath;
    }
}
