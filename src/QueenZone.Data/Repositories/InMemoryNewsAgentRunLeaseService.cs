namespace QueenZone.Data;

public sealed class InMemoryNewsAgentRunLeaseService(SharedNewsAgentLeaseStore store)
    : InMemoryRunLeaseServiceBase<INewsAgentRunLease>(store), INewsAgentRunLeaseService
{
    protected override INewsAgentRunLease CreateLease(SharedLeaseStore store, string leaseName, string holderId) =>
        new InMemoryNewsAgentRunLease(store, leaseName, holderId);

    private sealed class InMemoryNewsAgentRunLease(SharedLeaseStore store, string leaseName, string holderId)
        : InMemoryRunLease(store, leaseName, holderId), INewsAgentRunLease;
}
