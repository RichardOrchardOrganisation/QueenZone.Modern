using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class SearchReindexLeaseEntityConfiguration()
    : LeaseEntityConfigurationBase<SearchReindexLeaseEntity>("SearchReindexLeases");
