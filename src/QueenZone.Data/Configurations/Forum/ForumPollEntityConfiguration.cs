using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ForumPollEntityConfiguration : IEntityTypeConfiguration<ForumPollEntity>
{
    public void Configure(EntityTypeBuilder<ForumPollEntity> builder)
    {
        builder.ToTable("ForumPolls");
        builder.HasKey(poll => poll.Id);
        builder.Property(poll => poll.Question).HasMaxLength(300).IsRequired();
        builder.Property(poll => poll.CreatedAt).IsRequired();
        builder.HasIndex(poll => poll.LegacyTopicId)
            .IsUnique()
            .HasDatabaseName("UQ_ForumPolls_LegacyTopicId");
        builder.HasIndex(poll => poll.ThreadId)
            .IsUnique()
            .HasDatabaseName("UQ_ForumPolls_ThreadId");
        builder.HasOne(poll => poll.Thread)
            .WithMany()
            .HasForeignKey(poll => poll.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(poll => poll.Options)
            .WithOne(option => option.Poll)
            .HasForeignKey(option => option.PollId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(poll => poll.Votes)
            .WithOne(vote => vote.Poll)
            .HasForeignKey(vote => vote.PollId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
