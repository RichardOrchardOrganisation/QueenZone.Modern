using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class PrivateConversationParticipantEntityConfiguration : IEntityTypeConfiguration<PrivateConversationParticipantEntity>
{
    public void Configure(EntityTypeBuilder<PrivateConversationParticipantEntity> builder)
    {
        builder.ToTable("PrivateConversationParticipants");
        builder.HasKey(participant => new { participant.ConversationId, participant.MemberId });

        builder.Property(participant => participant.IsArchived).IsRequired();
        builder.Property(participant => participant.IsRemoved).IsRequired();

        builder.HasIndex(participant => new { participant.MemberId, participant.IsArchived })
            .HasDatabaseName("IX_PrivateConversationParticipants_Member_Archived");

        builder.HasIndex(participant => new { participant.MemberId, participant.IsRemoved })
            .HasDatabaseName("IX_PrivateConversationParticipants_Member_Removed");

        builder.HasOne(participant => participant.Conversation)
            .WithMany(conversation => conversation.Participants)
            .HasForeignKey(participant => participant.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(participant => participant.Member)
            .WithMany()
            .HasForeignKey(participant => participant.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
