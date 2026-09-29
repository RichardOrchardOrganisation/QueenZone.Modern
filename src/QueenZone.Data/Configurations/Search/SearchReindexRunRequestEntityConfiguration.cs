using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class SearchReindexRunRequestEntityConfiguration()
    : RunRequestEntityConfigurationBase<SearchReindexRunRequestEntity>("SearchReindexRunRequests")
{
    protected override void ConfigureStatus(EntityTypeBuilder<SearchReindexRunRequestEntity> builder) =>
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
}
