namespace QueenZone.Data;

/// <summary>
/// Chooses one id inside an inclusive indexed range. Callers seek that id
/// instead of sorting a table with <c>NEWID()</c>.
/// </summary>
internal static class IndexedIdRange
{
    public static int NextTarget(int minId, int maxId)
    {
        if (minId >= maxId)
        {
            return minId;
        }

        var span = (long)maxId - minId;
        return (int)(minId + Random.Shared.NextInt64(0, span + 1));
    }
}
