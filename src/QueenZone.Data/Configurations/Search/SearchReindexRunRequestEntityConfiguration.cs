using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class SearchReindexRunRequestEntityConfiguration : IEntityTypeConfiguration<SearchReindexRunRequestEntity>
{
    public void Configure(EntityTypeBuilder<SearchReindexRunRequestEntity> builder)
    {
        builder.ToTable("SearchReindexRunRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(request => request.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(request => request.RequestedAtUtc).IsRequired();
        builder.Property(request => request.RunnerId).HasMaxLength(100);
        builder.Property(request => request.Summary).HasMaxLength(2000);
        builder.Property(request => request.ErrorMessage).HasMaxLength(2000);
        builder.Property(request => request.ActiveKey).HasMaxLength(20);
        builder.Property(request => request.UpdatedAtUtc).IsRequired();

        builder.HasIndex(request => request.ActiveKey)
            .IsUnique()
            .HasFilter("[ActiveKey] IS NOT NULL")
            .HasDatabaseName("UX_SearchReindexRunRequests_ActiveKey");
        builder.HasIndex(request => new { request.Status, request.RequestedAtUtc })
            .HasDatabaseName("IX_SearchReindexRunRequests_Status_RequestedAtUtc");
    }
}
