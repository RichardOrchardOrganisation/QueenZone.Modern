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
}
