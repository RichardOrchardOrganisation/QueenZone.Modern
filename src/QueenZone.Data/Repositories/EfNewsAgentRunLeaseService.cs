using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfNewsAgentRunLeaseService(QueenZoneDbContext dbContext)
    : EfRunLeaseServiceBase<INewsAgentRunLease, NewsAgentRunLeaseEntity>(dbContext, "NewsAgentRunLeases"),
        INewsAgentRunLeaseService
{
    protected override INewsAgentRunLease CreateLease(string leaseName, string holderId) =>
        new EfNewsAgentRunLease(this, leaseName, holderId);

    private sealed class EfNewsAgentRunLease(EfNewsAgentRunLeaseService service, string leaseName, string holderId)
        : EfRunLease<INewsAgentRunLease, NewsAgentRunLeaseEntity>(service, leaseName, holderId), INewsAgentRunLease;
}
