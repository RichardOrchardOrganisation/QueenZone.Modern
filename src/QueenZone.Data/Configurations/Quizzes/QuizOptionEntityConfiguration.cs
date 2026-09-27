using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class QuizOptionEntityConfiguration : IEntityTypeConfiguration<QuizOptionEntity>
{
    public void Configure(EntityTypeBuilder<QuizOptionEntity> builder)
    {
        builder.ToTable("QuizOptions");
        builder.HasKey(option => option.Id);
        builder.Property(option => option.OptionText).HasMaxLength(QuizValidation.OptionMaxLength).IsRequired();
        builder.HasIndex(option => new { option.QuestionId, option.DisplayOrder })
            .HasDatabaseName("IX_QuizOptions_QuestionId_DisplayOrder");
    }
}
