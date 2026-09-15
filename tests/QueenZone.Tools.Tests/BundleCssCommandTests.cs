using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class BundleCssCommandTests
{
    [Fact]
    public async Task RunAsync_ReturnsError_WhenOutputOrInputsAreMissing()
    {
        var exitCode = await BundleCssCommand.RunAsync([]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WithoutWritingOutput_WhenAnInputDoesNotExist()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "tokens.css");

        try
        {
            var exitCode = await BundleCssCommand.RunAsync(
                [outputPath, Path.Combine(directory, "missing.css")]);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ConcatenatesInputsInOrder_WithSafeSeparators()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "publish", "tokens.css");
        var firstPath = Path.Combine(directory, "first.css");
        var secondPath = Path.Combine(directory, "second.css");
        await File.WriteAllTextAsync(firstPath, ":root { --color: red; }");
        await File.WriteAllTextAsync(secondPath, "body { color: var(--color); }\n");

        try
        {
            var exitCode = await BundleCssCommand.RunAsync([outputPath, firstPath, secondPath]);

            Assert.Equal(0, exitCode);
            Assert.Equal(
                ":root { --color: red; }" + Environment.NewLine + "body { color: var(--color); }\n",
                await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qz-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
