using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class HomePollOptionEntityConfiguration : IEntityTypeConfiguration<HomePollOptionEntity>
{
    public void Configure(EntityTypeBuilder<HomePollOptionEntity> builder)
    {
        builder.ToTable("HomePollOptions");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.OptionText).HasMaxLength(HomePollValidation.OptionMaxLength).IsRequired();
        builder.HasIndex(option => new { option.PollId, option.DisplayOrder })
            .HasDatabaseName("IX_HomePollOptions_PollId_DisplayOrder");
    }
}
