using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class ToolArgsTests
{
    [Fact]
    public void TryReadInt_WhenFlagDoesNotMatch_ReturnsFalseAndLeavesIndex()
    {
        var index = 0;

        var matched = ToolArgs.TryReadInt(["--other", "5"], ref index, "--limit", 1, out _, out var error);

        Assert.False(matched);
        Assert.Null(error);
        Assert.Equal(0, index);
    }

    [Fact]
    public void TryReadInt_WhenFlagHasNoValue_DoesNotMatch()
    {
        var index = 0;

        var matched = ToolArgs.TryReadInt(["--limit"], ref index, "--limit", 1, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryReadInt_WithValidValue_ReturnsValueAndAdvancesIndex()
    {
        var index = 0;

        var matched = ToolArgs.TryReadInt(["--limit", "25"], ref index, "--limit", 1, out var value, out var error);

        Assert.True(matched);
        Assert.Null(error);
        Assert.Equal(25, value);
        Assert.Equal(1, index);
    }

    [Theory]
    [InlineData("abc", null, "--limit must be an integer.")]
    [InlineData("abc", 1, "--limit must be a positive integer.")]
    [InlineData("0", 1, "--limit must be a positive integer.")]
    [InlineData("-1", 0, "--limit must be >= 0.")]
    public void TryReadInt_WithInvalidValue_ReturnsMatchedWithError(string raw, int? minValue, string expected)
    {
        var index = 0;

        var matched = ToolArgs.TryReadInt(["--limit", raw], ref index, "--limit", minValue, out _, out var error);

        Assert.True(matched);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void TryReadInt_AllowsZeroWhenMinimumIsZero()
    {
        var index = 0;

        var matched = ToolArgs.TryReadInt(["--delay-ms", "0"], ref index, "--delay-ms", 0, out var value, out var error);

        Assert.True(matched);
        Assert.Null(error);
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryReadValue_ReadsMatchingFlag()
    {
        var index = 0;

        var matched = ToolArgs.TryReadValue(["--output", "report.txt"], ref index, "--output", out var value);

        Assert.True(matched);
        Assert.Equal("report.txt", value);
        Assert.Equal(1, index);
    }

    [Fact]
    public void TryReadCommonOption_ReadsSharedFlags()
    {
        string? connectionString = null;
        string? storageConnectionString = null;
        string? settingsFile = null;
        var index = 0;
        Assert.True(ToolArgs.TryReadCommonOption(
            ["--connection-string", "Server=.;"],
            ref index,
            ref connectionString,
            ref storageConnectionString,
            ref settingsFile));
        Assert.Equal("Server=.;", connectionString);

        index = 0;
        Assert.True(ToolArgs.TryReadCommonOption(
            ["--storage-connection-string", "UseDevelopmentStorage=true"],
            ref index,
            ref connectionString,
            ref storageConnectionString,
            ref settingsFile));
        Assert.Equal("UseDevelopmentStorage=true", storageConnectionString);

        index = 0;
        Assert.True(ToolArgs.TryReadCommonOption(
            ["--settings-file", "appsettings.Local.json"],
            ref index,
            ref connectionString,
            ref storageConnectionString,
            ref settingsFile));
        Assert.Equal("appsettings.Local.json", settingsFile);

        index = 0;
        Assert.False(ToolArgs.TryReadCommonOption(
            ["--limit", "3"],
            ref index,
            ref connectionString,
            ref storageConnectionString,
            ref settingsFile));
        Assert.Equal(0, index);
    }

    [Fact]
    public void WriteUsage_WritesErrorAndLinesToStderr()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            ToolArgs.WriteUsage("Missing flag.", "Usage:", "  example");
            var text = error.ToString();
            Assert.Contains("Missing flag.", text, StringComparison.Ordinal);
            Assert.Contains("Usage:", text, StringComparison.Ordinal);
            Assert.Contains("  example", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void WriteUsage_OmitsBlankErrorMessage()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            ToolArgs.WriteUsage("   ", "Usage only");
            Assert.Equal($"Usage only{Environment.NewLine}", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void WriteBackfillSummary_PrintsDryRunHintWhenUpdatesArePlanned()
    {
        using var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            ToolArgs.WriteBackfillSummary(2, 0, 1, 0, apply: false, "Re-run with --apply.");
            var text = output.ToString();
            Assert.Contains("Would update / planned: 2", text, StringComparison.Ordinal);
            Assert.Contains("Updated: 0", text, StringComparison.Ordinal);
            Assert.Contains("Skipped: 1", text, StringComparison.Ordinal);
            Assert.Contains("Failed: 0", text, StringComparison.Ordinal);
            Assert.Contains("Re-run with --apply.", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteBackfillSummary_OmitsHintWhenApplying()
    {
        using var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            ToolArgs.WriteBackfillSummary(1, 1, 0, 0, apply: true, "Re-run with --apply.");
            Assert.DoesNotContain("Re-run with --apply.", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
