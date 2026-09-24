using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class NewsAgentRunnerHeartbeatEntityConfiguration : IEntityTypeConfiguration<NewsAgentRunnerHeartbeatEntity>
{
    public void Configure(EntityTypeBuilder<NewsAgentRunnerHeartbeatEntity> builder)
    {
        builder.ToTable("NewsAgentRunnerHeartbeats");
        builder.HasKey(heartbeat => heartbeat.RunnerId);
        builder.Property(heartbeat => heartbeat.RunnerId).HasMaxLength(100).IsRequired();
        builder.Property(heartbeat => heartbeat.LastSeenAtUtc).IsRequired();
    }
}
