using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class TriviaFactEntityConfiguration : IEntityTypeConfiguration<TriviaFactEntity>
{
    public void Configure(EntityTypeBuilder<TriviaFactEntity> builder)
    {
        builder.ToTable("TriviaFacts");
        builder.HasKey(fact => fact.Id);

        builder.Property(fact => fact.Text).HasMaxLength(TriviaValidation.MaxTextLength).IsRequired();
        builder.Property(fact => fact.Category).HasMaxLength(TriviaValidation.MaxCategoryLength);
        builder.Property(fact => fact.Difficulty).HasMaxLength(TriviaValidation.MaxDifficultyLength);
        builder.Property(fact => fact.Source).HasMaxLength(TriviaValidation.MaxSourceLength);
        builder.Property(fact => fact.IsPublished).IsRequired();
        builder.Property(fact => fact.CreatedAt).IsRequired();

        builder.HasIndex(fact => new { fact.IsPublished, fact.Category })
            .HasDatabaseName("IX_TriviaFacts_Published_Category");
    }
}
