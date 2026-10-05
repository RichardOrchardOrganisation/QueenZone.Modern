namespace QueenZone.Tools;

internal sealed record DevSnapshotOptions(
    string Mode,
    string ConfigPath,
    string ManifestPath,
    string SummaryPath)
{
    public static DevSnapshotOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("copy" or "verify"))
        {
            throw new ArgumentException("dev-snapshot requires copy or verify.");
        }

        string? config = null;
        var manifest = "dev-snapshot-manifest.json";
        var summary = "dev-snapshot-summary.json";
        var index = 1;
        while (index < args.Length)
        {
            var value = args[index++];
            if (value is "--config" or "--manifest" or "--summary")
            {
                if (index >= args.Length)
                {
                    throw new ArgumentException($"{value} requires a value.");
                }

                var optionValue = args[index++];
                switch (value)
                {
                    case "--config": config = optionValue; break;
                    case "--manifest": manifest = optionValue; break;
                    case "--summary": summary = optionValue; break;
                }

                continue;
            }

            throw new ArgumentException($"Unsupported argument: {value}");
        }

        if (string.IsNullOrWhiteSpace(config))
        {
            throw new ArgumentException("--config is required.");
        }

        return new DevSnapshotOptions(args[0], config, manifest, summary);
    }
}
