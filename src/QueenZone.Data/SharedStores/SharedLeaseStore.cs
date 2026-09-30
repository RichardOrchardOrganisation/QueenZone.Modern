namespace QueenZone.Data;

/// <summary>In-memory lease bookkeeping. Subclassed per feature so each gets its own singleton.</summary>
public abstract class SharedLeaseStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, LeaseRecord> leases = new(StringComparer.Ordinal);

    public bool TryAcquire(string leaseName, string holderId, DateTime expiresAtUtc)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if (leases.TryGetValue(leaseName, out var existing)
                && existing.ExpiresAtUtc > now
                && !string.Equals(existing.HolderId, holderId, StringComparison.Ordinal))
            {
                return false;
            }

            leases[leaseName] = new LeaseRecord(holderId, expiresAtUtc);
            return true;
        }
    }

    public bool Release(string leaseName, string holderId)
    {
        lock (gate)
        {
            if (!leases.TryGetValue(leaseName, out var existing)
                || !string.Equals(existing.HolderId, holderId, StringComparison.Ordinal))
            {
                return false;
            }

            leases.Remove(leaseName);
            return true;
        }
    }

    private sealed record LeaseRecord(string HolderId, DateTime ExpiresAtUtc);
}
