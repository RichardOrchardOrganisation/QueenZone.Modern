using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class ConvertLegacyBbCodeCommandTests
{
    [Fact]
    public void Parse_DefaultsToDryRun()
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
        ]);
        Assert.True(options.IsValid);
        Assert.False(options.Apply);
        Assert.Equal(50, options.DelayMs);
        Assert.Null(options.Limit);
    }

    [Fact]
    public void Parse_ApplyAndLimit()
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--apply",
            "--limit", "10",
            "--delay-ms", "0",
        ]);
        Assert.True(options.IsValid);
        Assert.True(options.Apply);
        Assert.Equal(10, options.Limit);
        Assert.Equal(0, options.DelayMs);
    }

    [Fact]
    public void Parse_MissingConnectionString_IsInvalid()
    {
        var options = ConvertLegacyBbCodeOptions.Parse([]);
        Assert.False(options.IsValid);
    }

    [Theory]
    [InlineData("--limit", "0", "--limit must be a positive integer.")]
    [InlineData("--delay-ms", "-3", "--delay-ms must be >= 0.")]
    public void Parse_RejectsInvalidIntegers(string flag, string value, string expected)
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            flag,
            value,
        ]);

        Assert.False(options.IsValid);
        Assert.Equal(expected, options.ErrorMessage);
    }

    [Fact]
    public async Task RunCore_DryRun_PlansConvertibleRowsAndSkipsFalsePositives()
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--delay-ms", "0",
        ]);
        var candidates = new[]
        {
            new BbCodeCandidateRow(1, "[b]hello[/b]"),
            new BbCodeCandidateRow(2, "no markers, just a stray [ bracket"),
        };

        var exit = await ConvertLegacyBbCodeCommand.RunCoreAsync(options, candidates);

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RunCore_DoesNotThrow_WhenConvertedExceedsColumnLimit()
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--delay-ms", "0",
        ]);
        var longBody = "[b]" + new string('x', 7999) + "[/b]";
        var candidates = new[] { new BbCodeCandidateRow(1, longBody) };

        var exit = await ConvertLegacyBbCodeCommand.RunCoreAsync(options, candidates);

        // Adding <strong></strong> markup pushes this over the 8000-char column limit;
        // the row must be reported as a failure needing manual review, not silently truncated.
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunCore_NoCandidates_ReturnsSuccess()
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
        ]);

        var exit = await ConvertLegacyBbCodeCommand.RunCoreAsync(options, []);

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RunCore_Apply_AttemptsUpdateAndFailsGracefullyWithoutADatabase()
    {
        var options = ConvertLegacyBbCodeOptions.Parse(
        [
            "--connection-string", "Server=.;Database=does-not-exist;Connect Timeout=1;",
            "--apply",
            "--delay-ms", "0",
        ]);
        var candidates = new[] { new BbCodeCandidateRow(1, "[b]hello[/b]") };

        var exit = await ConvertLegacyBbCodeCommand.RunCoreAsync(options, candidates);

        // No real database is reachable in this unit test; the write attempt fails and is
        // reported, rather than crashing the whole run.
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_MissingConnectionString_PrintsUsageAndReturnsError()
    {
        var exit = await ConvertLegacyBbCodeCommand.RunAsync([]);

        Assert.Equal(2, exit);
    }
}
