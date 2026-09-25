using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueenZone.Data.Entities;

namespace QueenZone.Data.Configurations;

public sealed class ArticleSubmissionEntityConfiguration : IEntityTypeConfiguration<ArticleSubmissionEntity>
{
    public void Configure(EntityTypeBuilder<ArticleSubmissionEntity> builder)
    {
        builder.ToTable("ArticleSubmissions");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Slug).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Excerpt).HasMaxLength(500);
        builder.Property(a => a.Body).IsRequired();
        builder.Property(a => a.WordCount).IsRequired();
        builder.Property(a => a.CoverImageBlobPath).HasMaxLength(512);
        builder.Property(a => a.Tags).HasMaxLength(500);
        builder.Property(a => a.Status).HasMaxLength(50).IsRequired();
        builder.Property(a => a.ReviewerEmail).HasMaxLength(256);
        builder.Property(a => a.ReviewNotes).HasMaxLength(1000);
        builder.Property(a => a.RejectionReason).HasMaxLength(1000);

        builder.HasIndex(a => a.Slug)
            .HasDatabaseName("IX_ArticleSubmissions_Slug");

        builder.HasIndex(a => new { a.Status, a.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ArticleSubmissions_Status_SubmittedAt");

        builder.HasIndex(a => new { a.AuthorMemberId, a.SubmittedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ArticleSubmissions_Author_SubmittedAt");

        builder.HasOne(a => a.Author)
            .WithMany()
            .HasForeignKey(a => a.AuthorMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
