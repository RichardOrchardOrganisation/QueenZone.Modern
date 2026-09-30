using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAgentGuidanceRevisionEntityConfiguration : IEntityTypeConfiguration<NewsAgentGuidanceRevisionEntity>
{
    private readonly bool sqlServer;

    public NewsAgentGuidanceRevisionEntityConfiguration(bool sqlServer)
    {
        this.sqlServer = sqlServer;
    }

    public void Configure(EntityTypeBuilder<NewsAgentGuidanceRevisionEntity> builder)
    {
        builder.ToTable("NewsAgentGuidanceRevisions");
        builder.HasKey(revision => revision.Id);

        builder.Property(revision => revision.Type)
            .HasConversion(
                value => NewsAgentGuidanceText.ToStorageType(value),
                value => NewsAgentGuidanceText.ParseType(value))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(revision => revision.RevisionNumber).IsRequired();
        builder.Property(revision => revision.Content).HasMaxLength(4000).IsRequired();
        builder.Property(revision => revision.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(revision => revision.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(revision => revision.CreatedAt).IsRequired();
        builder.Property(revision => revision.CreatedByEmail).HasMaxLength(320).IsRequired();
        builder.Property(revision => revision.PublishedByEmail).HasMaxLength(320);
        if (sqlServer)
        {
            builder.Property(revision => revision.RowVersion).IsRowVersion();
        }
        else
        {
            // SQLite and in-memory providers do not generate rowversion; persist a client token.
            builder.Property(revision => revision.RowVersion)
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedNever();
        }

        builder.HasIndex(revision => new { revision.Type, revision.RevisionNumber })
            .IsUnique()
            .HasDatabaseName("UX_NewsAgentGuidanceRevisions_Type_RevisionNumber");

        builder.HasIndex(revision => revision.Type, "UX_NewsAgentGuidanceRevisions_Type_Published")
            .IsUnique()
            .HasFilter("[Status] = 'Published'");

        builder.HasIndex(revision => revision.Type, "UX_NewsAgentGuidanceRevisions_Type_Draft")
            .IsUnique()
            .HasFilter("[Status] = 'Draft'");
    }
}
