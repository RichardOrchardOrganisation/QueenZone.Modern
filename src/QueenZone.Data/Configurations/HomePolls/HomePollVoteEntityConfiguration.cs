using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class HomePollVoteEntityConfiguration : IEntityTypeConfiguration<HomePollVoteEntity>
{
    public void Configure(EntityTypeBuilder<HomePollVoteEntity> builder)
    {
        builder.ToTable("HomePollVotes");
        builder.HasKey(vote => vote.Id);
        builder.Property(vote => vote.VotedAt).IsRequired();
        builder.HasIndex(vote => new { vote.PollId, vote.MemberAccountId })
            .IsUnique()
            .HasDatabaseName("UQ_HomePollVotes_Poll_Member");
        builder.HasOne(vote => vote.Option)
            .WithMany(option => option.Votes)
            .HasForeignKey(vote => vote.OptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
