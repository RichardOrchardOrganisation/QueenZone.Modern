namespace QueenZone.Data;

public static class CrosswordSampleData
{
    public static IReadOnlyList<CrosswordSeed> Load()
    {
        var assembly = typeof(CrosswordSampleData).Assembly;
        var seeds = new List<CrosswordSeed>();
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("QueenZone.Data.Crosswords.Seeds.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var result = CrosswordSeedJson.Parse(bytes.ToArray());
            if (!result.IsValid)
            {
                throw new InvalidDataException($"Embedded crossword seed '{name}' failed validation.");
            }
            seeds.Add(result.Seed!);
        }
        return seeds;
    }
}
