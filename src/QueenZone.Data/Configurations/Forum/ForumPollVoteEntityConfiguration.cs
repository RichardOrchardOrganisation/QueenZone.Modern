using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ForumPollVoteEntityConfiguration : IEntityTypeConfiguration<ForumPollVoteEntity>
{
    public void Configure(EntityTypeBuilder<ForumPollVoteEntity> builder)
    {
        builder.ToTable("ForumPollVotes");
        builder.HasKey(vote => vote.Id);
        builder.Property(vote => vote.VotedAt).IsRequired();
        builder.HasIndex(vote => new { vote.PollId, vote.MemberAccountId, vote.OptionId })
            .IsUnique()
            .HasDatabaseName("UQ_ForumPollVotes_Poll_Member_Option");
        builder.HasOne(vote => vote.Option)
            .WithMany(option => option.Votes)
            .HasForeignKey(vote => vote.OptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
