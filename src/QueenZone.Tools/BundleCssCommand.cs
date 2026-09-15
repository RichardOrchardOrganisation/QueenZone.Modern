namespace QueenZone.Tools;

/// <summary>
/// Concatenates CSS files in the supplied order into a single output file.
/// Used by QueenZone.Web's publish pipeline; see the "BundleCss" MSBuild target in QueenZone.Web.csproj.
/// </summary>
internal static class BundleCssCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("bundle-css requires an output CSS file path and at least one input CSS file path.");
            Console.Error.WriteLine("Usage: dotnet run --project src/QueenZone.Tools -- bundle-css <output.css> <input.css> [more inputs...]");
            return 2;
        }

        var outputPath = args[0];
        var inputPaths = args[1..];
        var missingPaths = inputPaths.Where(path => !File.Exists(path)).ToArray();

        if (missingPaths.Length > 0)
        {
            foreach (var missingPath in missingPaths)
            {
                Console.Error.WriteLine($"CSS file was not found: {missingPath}");
            }

            return 1;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using var output = File.CreateText(outputPath);
        foreach (var inputPath in inputPaths)
        {
            var source = await File.ReadAllTextAsync(inputPath);
            await output.WriteAsync(source);
            if (!source.EndsWith('\n'))
            {
                await output.WriteLineAsync();
            }
        }

        Console.WriteLine($"Bundled {inputPaths.Length} CSS files into {outputPath}.");
        return 0;
    }
}
