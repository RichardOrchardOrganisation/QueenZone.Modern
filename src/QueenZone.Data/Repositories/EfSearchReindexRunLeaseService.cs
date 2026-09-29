using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfSearchReindexRunLeaseService(QueenZoneDbContext dbContext)
    : EfRunLeaseServiceBase<ISearchReindexRunLease, SearchReindexLeaseEntity>(dbContext, "SearchReindexLeases"),
        ISearchReindexRunLeaseService
{
    protected override ISearchReindexRunLease CreateLease(string leaseName, string holderId) =>
        new EfSearchReindexRunLease(this, leaseName, holderId);

    private sealed class EfSearchReindexRunLease(EfSearchReindexRunLeaseService service, string leaseName, string holderId)
        : EfRunLease<ISearchReindexRunLease, SearchReindexLeaseEntity>(service, leaseName, holderId), ISearchReindexRunLease;
}
