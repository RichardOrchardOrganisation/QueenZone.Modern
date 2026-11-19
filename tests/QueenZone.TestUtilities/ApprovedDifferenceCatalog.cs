using System.Text.Json;

namespace QueenZone.TestUtilities;

public sealed class ApprovedDifferenceCatalog
{
    private readonly Entry[] entries;

    private ApprovedDifferenceCatalog(Entry[] entries)
    {
        this.entries = entries;
    }

    public static ApprovedDifferenceCatalog Load(string path)
    {
        var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Expected an approved differences array.");
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                throw new InvalidOperationException($"Approved difference '{entry.Id}' needs a reason.");
            }
        }

        return new ApprovedDifferenceCatalog(entries);
    }

    public void Verify(string suite, string member, string runner, IEnumerable<string> observedIds)
    {
        var applicable = entries.Where(entry => entry.Suite == suite && entry.Member == member
            && entry.Runners.Contains(runner, StringComparer.Ordinal)).Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        var observed = observedIds.ToHashSet(StringComparer.Ordinal);
        var unlisted = observed.Except(applicable).ToArray();
        if (unlisted.Length > 0)
        {
            throw new InvalidOperationException($"Unlisted differences for {suite}/{member}/{runner}: {string.Join(", ", unlisted)}");
        }

        var stale = applicable.Except(observed).ToArray();
        if (stale.Length > 0)
        {
            throw new InvalidOperationException($"Stale approved differences for {suite}/{member}/{runner}: {string.Join(", ", stale)}");
        }
    }

    public sealed record Entry(string Id, string Suite, string Member, string[] Runners,
        string Kind, string Reason, string Owner);
}
