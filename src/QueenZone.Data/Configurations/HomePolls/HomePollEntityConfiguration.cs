using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class HomePollEntityConfiguration : IEntityTypeConfiguration<HomePollEntity>
{
    public void Configure(EntityTypeBuilder<HomePollEntity> builder)
    {
        builder.ToTable("HomePolls");
        builder.HasKey(poll => poll.Id);
        builder.Property(poll => poll.Question).HasMaxLength(HomePollValidation.QuestionMaxLength).IsRequired();
        builder.Property(poll => poll.CreatedAt).IsRequired();
        builder.HasIndex(poll => poll.IsCurrent)
            .IsUnique()
            .HasFilter("[IsCurrent] = 1")
            .HasDatabaseName("UX_HomePolls_IsCurrent");
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
