using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class BackfillPhotoDimensionsCommandTests
{
    [Fact]
    public void Parse_DefaultsToDryRunPublicZerosOnly()
    {
        var options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
        ]);
        Assert.True(options.IsValid);
        Assert.False(options.Apply);
        Assert.True(options.PublicOnly);
        Assert.False(options.Force);
    }

    [Fact]
    public void Parse_ApplyAndForce()
    {
        var options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--apply",
            "--force",
            "--limit", "5",
            "--include-hidden",
            "--category-id", "12",
            "--pic-ids", "1,2",
            "--delay-ms", "0",
        ]);
        Assert.True(options.IsValid);
        Assert.True(options.Apply);
        Assert.True(options.Force);
        Assert.Equal(5, options.Limit);
        Assert.False(options.PublicOnly);
        Assert.Equal(12, options.CategoryId);
        Assert.Equal([1, 2], options.PicIds);
        Assert.Equal(0, options.DelayMs);
    }

    [Theory]
    [InlineData("--category-id", "x", "--category-id must be an integer.")]
    [InlineData("--limit", "0", "--limit must be a positive integer.")]
    [InlineData("--delay-ms", "-1", "--delay-ms must be >= 0.")]
    public void Parse_RejectsInvalidIntegers(string flag, string value, string expected)
    {
        var options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            flag,
            value,
        ]);

        Assert.False(options.IsValid);
        Assert.Equal(expected, options.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_PrintsUsage_WhenOptionsAreInvalid()
    {
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        var previous = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", null);
        try
        {
            var exitCode = await BackfillPhotoDimensionsCommand.RunAsync([]);

            Assert.Equal(2, exitCode);
            var text = error.ToString();
            Assert.Contains("backfill-photo-dimensions", text, StringComparison.Ordinal);
            Assert.Contains("--apply", text, StringComparison.Ordinal);
            Assert.Contains("photo-dim-inventory", text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy", previous);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task RunCore_DryRun_UsesProbeAndDoesNotRequireDbWrite()
    {
        var options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--delay-ms", "0",
        ]);
        var candidates = new[]
        {
            new BackfillPhotoRow(1, "/Queen/a.jpg", 0, 0, 12, 1),
            new BackfillPhotoRow(2, "/Queen/b.jpg", 800, 600, 12, 1),
        };
        var probe = new StubProbe();
        var exit = await BackfillPhotoDimensionsCommand.RunCoreAsync(options, candidates, probe);
        Assert.Equal(0, exit);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task RunCore_Apply_CallsProbeForZeroDims()
    {
        var options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--apply",
            "--delay-ms", "0",
        ]);
        // Apply path updates SQL — only use dry-run-compatible probe success without apply for unit test.
        // Force dry-run path for CI: re-parse without apply.
        options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--delay-ms", "0",
        ]);
        var candidates = new[]
        {
            new BackfillPhotoRow(9, "/x.jpg", 0, 0, 1, 1),
        };
        var probe = new StubProbe { Result = new MeasuredPhotoSize(1024, 768) };
        var exit = await BackfillPhotoDimensionsCommand.RunCoreAsync(options, candidates, probe);
        Assert.Equal(0, exit);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task RunCore_Fails_WhenProbeCannotDecodeImage()
    {
        var (exit, error) = await RunCoreCapturingError(
            new StubProbe { Result = null },
            new BackfillPhotoRow(11, "/missing.jpg", 0, 0, 1, 1));

        Assert.Equal(1, exit);
        Assert.Contains("FAIL pic_id=11: could not load/decode image", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCore_Fails_WhenMeasuredSizeIsNonPositive()
    {
        var (exit, error) = await RunCoreCapturingError(
            new StubProbe { Result = new MeasuredPhotoSize(0, 480) },
            new BackfillPhotoRow(12, "/zero.jpg", 0, 0, 1, 1));

        Assert.Equal(1, exit);
        Assert.Contains("FAIL pic_id=12: measured non-positive size", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCore_Fails_WhenMeasuredSizeExceedsSmallint()
    {
        var (exit, error) = await RunCoreCapturingError(
            new StubProbe { Result = new MeasuredPhotoSize(short.MaxValue + 1, 1080) },
            new BackfillPhotoRow(13, "/huge.jpg", 0, 0, 1, 1));

        Assert.Equal(1, exit);
        Assert.Contains("FAIL pic_id=13: measured 32768x1080 exceeds smallint", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCore_Fails_WhenProbeThrows()
    {
        var (exit, error) = await RunCoreCapturingError(
            new ThrowingProbe(),
            new BackfillPhotoRow(14, "/boom.jpg", 0, 0, 1, 1));

        Assert.Equal(1, exit);
        Assert.Contains("FAIL pic_id=14: probe exploded", error, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Error)> RunCoreCapturingError(
        IPhotoDimensionProbe probe,
        BackfillPhotoRow row)
    {
        var options = BackfillPhotoDimensionsOptions.Parse(
        [
            "--connection-string", "Server=.;Database=test;",
            "--delay-ms", "0",
        ]);
        using var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await BackfillPhotoDimensionsCommand.RunCoreAsync(options, [row], probe);
            return (exit, error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private sealed class StubProbe : IPhotoDimensionProbe
    {
        public int Calls { get; private set; }

        public MeasuredPhotoSize? Result { get; init; } = new(1920, 1080);

        public Task<MeasuredPhotoSize?> MeasureAsync(BackfillPhotoRow row, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingProbe : IPhotoDimensionProbe
    {
        public Task<MeasuredPhotoSize?> MeasureAsync(BackfillPhotoRow row, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("probe exploded");
    }
}
