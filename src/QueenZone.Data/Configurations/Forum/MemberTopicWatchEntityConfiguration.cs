using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class MemberTopicWatchEntityConfiguration : IEntityTypeConfiguration<MemberTopicWatchEntity>
{
    public void Configure(EntityTypeBuilder<MemberTopicWatchEntity> builder)
    {
        builder.ToTable("MemberTopicWatches");
        builder.HasKey(watch => new { watch.MemberAccountId, watch.TopicId });

        builder.Property(watch => watch.CreatedAt).IsRequired();

        builder.HasIndex(watch => watch.TopicId)
            .HasDatabaseName("IX_MemberTopicWatches_TopicId");

        builder.HasOne(watch => watch.MemberAccount)
            .WithMany()
            .HasForeignKey(watch => watch.MemberAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
