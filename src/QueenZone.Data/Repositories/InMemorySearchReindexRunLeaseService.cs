namespace QueenZone.Data;

public sealed class InMemorySearchReindexRunLeaseService(SharedSearchReindexLeaseStore store)
    : InMemoryRunLeaseServiceBase<ISearchReindexRunLease>(store), ISearchReindexRunLeaseService
{
    protected override ISearchReindexRunLease CreateLease(SharedLeaseStore store, string leaseName, string holderId) =>
        new InMemorySearchReindexRunLease(store, leaseName, holderId);

    private sealed class InMemorySearchReindexRunLease(SharedLeaseStore store, string leaseName, string holderId)
        : InMemoryRunLease(store, leaseName, holderId), ISearchReindexRunLease;
}
