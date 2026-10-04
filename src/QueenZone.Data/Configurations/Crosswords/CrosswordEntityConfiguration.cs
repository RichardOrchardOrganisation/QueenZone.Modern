using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class CrosswordEntityConfiguration(bool sqlServer) : IEntityTypeConfiguration<CrosswordEntity>
{
    public void Configure(EntityTypeBuilder<CrosswordEntity> builder)
    {
        builder.ToTable("Crosswords", table =>
        {
            table.HasCheckConstraint("CK_Crosswords_Size", "[Width] BETWEEN 5 AND 15 AND [Height] BETWEEN 5 AND 15");
            table.HasCheckConstraint("CK_Crosswords_Difficulty", "[Difficulty] IN ('easy', 'medium', 'hard')");
            table.HasCheckConstraint("CK_Crosswords_Style", "[Style] IN ('american', 'british')");
            table.HasCheckConstraint("CK_Crosswords_Status", "[Status] IN ('Draft', 'Scheduled', 'Published', 'Archived')");
        });
        builder.HasKey(puzzle => puzzle.Id);
        builder.Property(puzzle => puzzle.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(puzzle => puzzle.Slug).IsUnique();
        builder.Property(puzzle => puzzle.Title).HasMaxLength(200).IsRequired();
        builder.Property(puzzle => puzzle.Description).HasMaxLength(1000).IsRequired();
        builder.Property(puzzle => puzzle.Difficulty).HasMaxLength(20).IsRequired();
        builder.Property(puzzle => puzzle.Style).HasMaxLength(20).IsRequired();
        builder.Property(puzzle => puzzle.BlockMask).HasMaxLength(225).IsRequired();
        builder.Property(puzzle => puzzle.SolutionRowsJson).HasMaxLength(2000).IsRequired();
        builder.Property(puzzle => puzzle.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(puzzle => puzzle.UpdatedByEmail).HasMaxLength(320).IsRequired();
        builder.HasIndex(puzzle => new { puzzle.Status, puzzle.PublishAt });
        if (sqlServer)
        {
            builder.Property(puzzle => puzzle.RowVersion).IsRowVersion();
        }
        else
        {
            builder.Property(puzzle => puzzle.RowVersion).IsConcurrencyToken().IsRequired().ValueGeneratedNever();
        }
        builder.HasMany(puzzle => puzzle.Entries).WithOne(entry => entry.Crossword)
            .HasForeignKey(entry => entry.CrosswordId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CrosswordEntryEntityConfiguration : IEntityTypeConfiguration<CrosswordEntryEntity>
{
    public void Configure(EntityTypeBuilder<CrosswordEntryEntity> builder)
    {
        builder.ToTable("CrosswordEntries");
        builder.HasKey(entry => entry.Id);
        builder.HasIndex(entry => new { entry.CrosswordId, entry.Number, entry.Direction }).IsUnique();
        builder.Property(entry => entry.Direction).HasConversion<string>().HasMaxLength(6).IsRequired();
        builder.Property(entry => entry.Answer).HasMaxLength(15).IsRequired();
        builder.Property(entry => entry.Clue).HasMaxLength(500).IsRequired();
        builder.Property(entry => entry.Enumeration).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.Explanation).HasMaxLength(300);
    }
}

public sealed class CrosswordAuditLogEntityConfiguration : IEntityTypeConfiguration<CrosswordAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<CrosswordAuditLogEntity> builder)
    {
        builder.ToTable("CrosswordAuditLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Actor).HasMaxLength(320).IsRequired();
        builder.Property(log => log.Action).HasMaxLength(20).IsRequired();
        builder.Property(log => log.Summary).HasMaxLength(1000).IsRequired();
        builder.HasIndex(log => new { log.CrosswordId, log.CreatedAt });
        builder.HasOne<CrosswordEntity>().WithMany().HasForeignKey(log => log.CrosswordId).OnDelete(DeleteBehavior.Cascade);
    }
}
