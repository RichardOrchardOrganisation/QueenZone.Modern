using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ForumPollOptionEntityConfiguration : IEntityTypeConfiguration<ForumPollOptionEntity>
{
    public void Configure(EntityTypeBuilder<ForumPollOptionEntity> builder)
    {
        builder.ToTable("ForumPollOptions");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.OptionText).HasMaxLength(200).IsRequired();
        builder.HasIndex(option => new { option.PollId, option.DisplayOrder })
            .HasDatabaseName("IX_ForumPollOptions_PollId_DisplayOrder");
    }
}
