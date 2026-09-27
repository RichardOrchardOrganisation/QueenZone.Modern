using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAgentRunRequestEntityConfiguration : IEntityTypeConfiguration<NewsAgentRunRequestEntity>
{
    public void Configure(EntityTypeBuilder<NewsAgentRunRequestEntity> builder)
    {
        builder.ToTable("NewsAgentRunRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(request => request.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(request => request.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(request => request.RequestedAtUtc).IsRequired();
        builder.Property(request => request.ArticleUrl).HasMaxLength(2000);
        builder.Property(request => request.GenerateDraft).IsRequired();
        builder.Property(request => request.RunnerId).HasMaxLength(100);
        builder.Property(request => request.Summary).HasMaxLength(2000);
        builder.Property(request => request.ErrorMessage).HasMaxLength(2000);
        builder.Property(request => request.ActiveKey).HasMaxLength(20);
        builder.Property(request => request.UpdatedAtUtc).IsRequired();

        builder.HasIndex(request => request.ActiveKey)
            .IsUnique()
            .HasFilter("[ActiveKey] IS NOT NULL")
            .HasDatabaseName("UX_NewsAgentRunRequests_ActiveKey");
        builder.HasIndex(request => new { request.Status, request.RequestedAtUtc })
            .HasDatabaseName("IX_NewsAgentRunRequests_Status_RequestedAtUtc");
    }
}
