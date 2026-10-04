using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class CrosswordProgressEntityConfiguration(bool sqlServer) : IEntityTypeConfiguration<CrosswordProgressEntity>
{
    public void Configure(EntityTypeBuilder<CrosswordProgressEntity> builder)
    {
        builder.ToTable("CrosswordProgress", table =>
            table.HasCheckConstraint("CK_CrosswordProgress_Time", "[ElapsedSeconds] >= 0"));
        builder.HasKey(progress => progress.Id);
        builder.HasIndex(progress => new { progress.CrosswordId, progress.MemberId }).IsUnique();
        builder.HasIndex(progress => progress.MemberId);
        builder.Property(progress => progress.GridFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(progress => progress.Letters).HasMaxLength(225).IsRequired();
        builder.Property(progress => progress.RevealedCellsJson).HasMaxLength(2000).IsRequired();
        builder.HasOne<CrosswordEntity>().WithMany().HasForeignKey(progress => progress.CrosswordId).OnDelete(DeleteBehavior.Cascade);
        if (sqlServer)
        {
            builder.Property(progress => progress.RowVersion).IsRowVersion();
        }
        else
        {
            builder.Property(progress => progress.RowVersion).IsConcurrencyToken().IsRequired().ValueGeneratedNever();
        }
    }
}

public sealed class CrosswordCompletionEntityConfiguration : IEntityTypeConfiguration<CrosswordCompletionEntity>
{
    public void Configure(EntityTypeBuilder<CrosswordCompletionEntity> builder)
    {
        builder.ToTable("CrosswordCompletions", table =>
            table.HasCheckConstraint("CK_CrosswordCompletions_Time", "[ElapsedSeconds] >= 0"));
        builder.HasKey(completion => completion.Id);
        builder.HasIndex(completion => new { completion.CrosswordId, completion.MemberId }).IsUnique();
        builder.HasIndex(completion => new { completion.CrosswordId, completion.RankingEligible, completion.ElapsedSeconds });
        builder.HasIndex(completion => new { completion.MemberId, completion.CompletedAt });
        builder.HasOne<CrosswordEntity>().WithMany().HasForeignKey(completion => completion.CrosswordId).OnDelete(DeleteBehavior.Cascade);
    }
}
