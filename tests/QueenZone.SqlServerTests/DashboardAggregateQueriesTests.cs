using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Exercises the SQL-Server-only conditional-aggregate paths in
/// <see cref="EfNewsSuggestionRepository"/>, <see cref="EfPhotoSubmissionRepository"/>, and
/// <see cref="EfArticleSubmissionRepository"/> against a real SQL Server instance.
/// </summary>
/// <remarks>
/// EF Core's SQLite provider cannot translate <see cref="DateTimeOffset"/> comparisons at all,
/// so these queries have no automated coverage in the default SQLite-backed
/// <c>QueenZone.Web.Tests</c> suite. This project targets a real SQL Server instead: the CI
/// <c>sql-server-tests</c> job runs it against a <c>mcr.microsoft.com/mssql/server</c> Docker
/// service container (see <c>.github/workflows/ci.yml</c>); locally it targets SQL Server
/// LocalDB by default. See <c>docs/architecture/testing-policy.md</c> ("Modern-schema SQL
/// Server tests").
///
/// The full <see cref="QueenZoneDbContext"/> model can't <c>EnsureCreated</c>/<c>Migrate</c> on
/// a blank database — several tables are marked <c>ExcludeFromMigrations</c> because they're
/// expected to already exist via the legacy BACPAC import, but other tables still carry live
/// FKs to them. So this creates just the four tables under test (mirroring the real Fluent
/// config for those entities) via a minimal scratch <see cref="DbContext"/>, then points the
/// real repositories at the same database — EF only generates SQL against the tables a given
/// LINQ query actually touches.
/// </remarks>
public sealed class DashboardAggregateQueriesTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneSqlServerTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;

    private string ConnectionString
    {
        get
        {
            var baseConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        var scratchOptions = new DbContextOptionsBuilder<ScratchSchemaDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using (var scratch = new ScratchSchemaDbContext(scratchOptions))
        {
            await scratch.Database.EnsureCreatedAsync();
        }

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        dbContext = new QueenZoneDbContext(options);
    }

    public async Task DisposeAsync()
    {
        var scratchOptions = new DbContextOptionsBuilder<ScratchSchemaDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var scratch = new ScratchSchemaDbContext(scratchOptions);
        await scratch.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task NewsSuggestions_dashboard_and_top_contributors_match_expected()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        SeedMembers(memberA, "Alice", memberB, "Bob");

        var seed = new[]
        {
            New(memberA, NewsSuggestionStatus.Pending, now), // today
            New(memberA, NewsSuggestionStatus.UnderReview, now.AddDays(-3)), // this week
            New(memberB, NewsSuggestionStatus.Promoted, now.AddDays(-10)), // last 30, approved
            New(memberB, NewsSuggestionStatus.Rejected, now.AddDays(-20)), // last 30, rejected
            New(memberA, NewsSuggestionStatus.Duplicate, now.AddDays(-25)), // last 30, rejected (duplicate)
            New(memberB, NewsSuggestionStatus.Pending, now.AddDays(-45)), // outside 30 days
        };
        dbContext.NewsSuggestions.AddRange(seed);
        await dbContext.SaveChangesAsync();

        var repo = new EfNewsSuggestionRepository(dbContext);
        var counts = await repo.GetDashboardCountsAsync(now);

        Assert.Equal(3, counts.Pending); // Pending x2 + UnderReview x1 (Duplicate/Rejected/Promoted excluded)
        Assert.Equal(1, counts.ReceivedToday);
        Assert.Equal(2, counts.ReceivedThisWeek); // today + 3 days ago
        Assert.Equal(1, counts.ApprovedLast30Days);
        Assert.Equal(2, counts.RejectedLast30Days); // Rejected + Duplicate
        Assert.Equal(2, counts.StillPendingFromLast30Days); // Pending(today) + UnderReview(-3d); the -45d Pending is outside 30 days

        var contributors = await repo.GetTopContributorsThisMonthAsync(now.AddDays(-30), 10);
        var byMember = contributors.ToDictionary(c => c.MemberId);
        Assert.Equal(3, byMember[memberA].Count); // today, -3d, -25d
        Assert.Equal(2, byMember[memberB].Count); // -10d, -20d (the -45d row is outside the window)
        Assert.Equal("Alice", byMember[memberA].DisplayName);
        Assert.Equal("Bob", byMember[memberB].DisplayName);

        static NewsSuggestionEntity New(Guid member, string status, DateTimeOffset submittedAt) => new()
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = member,
            Url = $"https://example.com/{Guid.NewGuid():N}",
            UrlHash = Guid.NewGuid().ToString("N"),
            Status = status,
            SubmittedAt = submittedAt,
        };
    }

    [Fact]
    public async Task PhotoSubmissions_dashboard_and_top_contributors_match_expected()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        SeedMembers(memberA, "Carol", memberB, "Dave");

        var seed = new[]
        {
            New(memberA, PhotoSubmissionStatus.Pending, now),
            New(memberA, PhotoSubmissionStatus.NeedsInfo, now.AddDays(-4)),
            New(memberB, PhotoSubmissionStatus.Approved, now.AddDays(-15)),
            New(memberB, PhotoSubmissionStatus.Rejected, now.AddDays(-29)),
            New(memberA, PhotoSubmissionStatus.UnderReview, now.AddDays(-40)),
        };
        dbContext.PhotoSubmissions.AddRange(seed);
        await dbContext.SaveChangesAsync();

        var repo = new EfPhotoSubmissionRepository(dbContext);
        var counts = await repo.GetDashboardCountsAsync(now);

        Assert.Equal(3, counts.Pending); // Pending + NeedsInfo + UnderReview
        Assert.Equal(1, counts.ReceivedToday);
        Assert.Equal(2, counts.ReceivedThisWeek); // today + -4d
        Assert.Equal(1, counts.ApprovedLast30Days);
        Assert.Equal(1, counts.RejectedLast30Days);
        Assert.Equal(2, counts.StillPendingFromLast30Days); // today + -4d (the -40d row is outside 30 days)

        var contributors = await repo.GetTopContributorsThisMonthAsync(now.AddDays(-30), 10);
        var byMember = contributors.ToDictionary(c => c.MemberId);
        Assert.Equal(2, byMember[memberA].Count); // today, -4d (the -40d row is outside the window)
        Assert.Equal(2, byMember[memberB].Count); // -15d, -29d

        static PhotoSubmissionEntity New(Guid member, string status, DateTimeOffset submittedAt) => new()
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = member,
            Title = "Test photo",
            BlobPath = $"{Guid.NewGuid():N}.webp",
            WebOptimizedBlobPath = $"{Guid.NewGuid():N}-web.webp",
            ThumbnailBlobPath = $"{Guid.NewGuid():N}-thumb.webp",
            OriginalFileName = "original.jpg",
            MimeType = "image/webp",
            Status = status,
            SubmittedAt = submittedAt,
        };
    }

    [Fact]
    public async Task ArticleSubmissions_dashboard_and_top_contributors_match_expected()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        SeedMembers(memberA, "Erin", memberB, "Frank");

        var seed = new[]
        {
            New(memberA, ArticleSubmissionStatus.Submitted, now),
            New(memberA, ArticleSubmissionStatus.Draft, null), // no SubmittedAt: must not blow up nullable-aggregate translation
            New(memberB, ArticleSubmissionStatus.ApprovedForPublishing, now.AddDays(-6)),
            New(memberB, ArticleSubmissionStatus.RequiresRevision, now.AddDays(-28)),
            New(memberA, ArticleSubmissionStatus.Rejected, now.AddDays(-50)),
        };
        dbContext.ArticleSubmissions.AddRange(seed);
        await dbContext.SaveChangesAsync();

        var repo = new EfArticleSubmissionRepository(dbContext);
        var counts = await repo.GetDashboardCountsAsync(now);

        Assert.Equal(2, counts.Pending); // Submitted + ApprovedForPublishing (Draft not counted)
        Assert.Equal(1, counts.ReceivedToday);
        Assert.Equal(2, counts.ReceivedThisWeek); // today + -6d
        Assert.Equal(1, counts.ApprovedLast30Days); // ApprovedForPublishing at -6d
        Assert.Equal(1, counts.RejectedLast30Days); // RequiresRevision at -28d
        Assert.Equal(1, counts.StillPendingFromLast30Days); // Submitted(today); -50d Rejected is out of window & wrong status anyway

        var contributors = await repo.GetTopContributorsThisMonthAsync(now.AddDays(-30), 10);
        var byMember = contributors.ToDictionary(c => c.MemberId);
        Assert.Equal(1, byMember[memberA].Count); // only "today" row; the null-SubmittedAt draft and -50d row are excluded
        Assert.Equal(2, byMember[memberB].Count); // -6d, -28d

        static ArticleSubmissionEntity New(Guid member, string status, DateTimeOffset? submittedAt) => new()
        {
            Id = Guid.NewGuid(),
            AuthorMemberId = member,
            Title = "Test article",
            Slug = $"test-article-{Guid.NewGuid():N}",
            Body = "Body text",
            Status = status,
            SubmittedAt = submittedAt,
        };
    }

    [Fact]
    public async Task Pending_and_member_queues_page_in_sql_with_offset()
    {
        var member = Guid.NewGuid();
        SeedMembers(member, "Pager", Guid.NewGuid(), "Other");
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var articleOldest = Guid.NewGuid();
        var articleNewest = Guid.NewGuid();
        dbContext.ArticleSubmissions.AddRange(
            NewArticle(articleOldest, "Oldest article", now.AddDays(-2)),
            NewArticle(Guid.NewGuid(), "Middle article", now.AddDays(-1)),
            NewArticle(articleNewest, "Newest article", now));

        var photoOldest = Guid.NewGuid();
        dbContext.PhotoSubmissions.AddRange(
            NewPhoto(photoOldest, "Oldest photo", now.AddDays(-2)),
            NewPhoto(Guid.NewGuid(), "Middle photo", now.AddDays(-1)),
            NewPhoto(Guid.NewGuid(), "Newest photo", now));

        var newsOldest = Guid.NewGuid();
        dbContext.NewsSuggestions.AddRange(
            NewNews(newsOldest, "https://example.com/oldest", now.AddDays(-2)),
            NewNews(Guid.NewGuid(), "https://example.com/middle", now.AddDays(-1)),
            NewNews(Guid.NewGuid(), "https://example.com/newest", now));
        await dbContext.SaveChangesAsync();

        var articles = new EfArticleSubmissionRepository(dbContext);
        var photos = new EfPhotoSubmissionRepository(dbContext);
        var news = new EfNewsSuggestionRepository(dbContext);

        AssertSqlPages(articles.PendingQueueQuery(2, 1).ToQueryString());
        AssertSqlPages(articles.MemberDraftsSqlQuery(2, 1, member).ToQueryString());
        AssertSqlPages(photos.PendingQueueQuery(2, 1).ToQueryString());
        AssertSqlPages(photos.MemberQueueQuery(member, 2, 1).ToQueryString());
        AssertSqlPages(news.PendingQueueQuery(2, 1).ToQueryString());
        AssertSqlPages(news.MemberQueueQuery(member, 2, 1).ToQueryString());

        var pendingArticles = await articles.GetPendingAsync(2, 2);
        Assert.Single(pendingArticles);
        Assert.Equal(articleOldest, pendingArticles[0].Id);
        Assert.DoesNotContain(pendingArticles, item => item.Id == articleNewest);

        var memberArticles = await articles.GetDraftsForMemberAsync(member, page: 2, pageSize: 2);
        Assert.Single(memberArticles.Items);
        Assert.Equal(articleOldest, memberArticles.Items[0].Id);

        var pendingPhotos = await photos.GetPendingAsync(2, 2);
        Assert.Single(pendingPhotos);
        Assert.Equal(photoOldest, pendingPhotos[0].Id);

        var memberPhotos = await photos.GetBySubmitterAsync(member, page: 2, pageSize: 2);
        Assert.Single(memberPhotos.Items);
        Assert.Equal(photoOldest, memberPhotos.Items[0].Id);

        var pendingNews = await news.GetPendingAsync(2, 2);
        Assert.Single(pendingNews);
        Assert.Equal(newsOldest, pendingNews[0].Id);

        var memberNews = await news.GetBySubmitterAsync(member, page: 2, pageSize: 2);
        Assert.Single(memberNews.Items);
        Assert.Equal(newsOldest, memberNews.Items[0].Id);

        ArticleSubmissionEntity NewArticle(Guid id, string title, DateTimeOffset submittedAt) => new()
        {
            Id = id,
            AuthorMemberId = member,
            Title = title,
            Slug = $"slug-{id:N}",
            Body = new string('a', 400),
            Status = ArticleSubmissionStatus.Submitted,
            SubmittedAt = submittedAt,
        };

        PhotoSubmissionEntity NewPhoto(Guid id, string title, DateTimeOffset submittedAt) => new()
        {
            Id = id,
            SubmitterMemberId = member,
            Title = title,
            BlobPath = $"{id:N}.webp",
            WebOptimizedBlobPath = $"{id:N}-web.webp",
            ThumbnailBlobPath = $"{id:N}-thumb.webp",
            OriginalFileName = "original.jpg",
            MimeType = "image/webp",
            Status = PhotoSubmissionStatus.Pending,
            SubmittedAt = submittedAt,
        };

        NewsSuggestionEntity NewNews(Guid id, string url, DateTimeOffset submittedAt) => new()
        {
            Id = id,
            SubmitterMemberId = member,
            Url = url,
            UrlHash = id.ToString("N"),
            Status = NewsSuggestionStatus.Pending,
            SubmittedAt = submittedAt,
        };
    }

    private static void AssertSqlPages(string sql)
    {
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FETCH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client evaluation", sql, StringComparison.OrdinalIgnoreCase);
    }

    private void SeedMembers(Guid idA, string nameA, Guid idB, string nameB)
    {
        dbContext.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = idA,
                Email = $"{nameA.ToLowerInvariant()}@example.com",
                NormalizedEmail = $"{nameA.ToUpperInvariant()}@EXAMPLE.COM",
                DisplayName = nameA,
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = idB,
                Email = $"{nameB.ToLowerInvariant()}@example.com",
                NormalizedEmail = $"{nameB.ToUpperInvariant()}@EXAMPLE.COM",
                DisplayName = nameB,
                CreatedAt = DateTime.UtcNow,
            });
        dbContext.SaveChanges();
    }

    // Minimal model covering only MemberAccounts + the three submission tables, mirroring the
    // Fluent config in QueenZoneDbContext for those entities.
    private sealed class ScratchSchemaDbContext(DbContextOptions<ScratchSchemaDbContext> options)
        : DbContext(options)
    {
        public DbSet<MemberAccount> MemberAccounts => Set<MemberAccount>();

        public DbSet<NewsSuggestionEntity> NewsSuggestions => Set<NewsSuggestionEntity>();

        public DbSet<PhotoSubmissionEntity> PhotoSubmissions => Set<PhotoSubmissionEntity>();

        public DbSet<ArticleSubmissionEntity> ArticleSubmissions => Set<ArticleSubmissionEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MemberAccount>(entity =>
            {
                entity.ToTable("MemberAccounts");
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Email).HasMaxLength(256).IsRequired();
                entity.Property(a => a.NormalizedEmail).HasMaxLength(256).IsRequired();
                entity.Property(a => a.DisplayName).HasMaxLength(100).IsRequired();
                entity.Property(a => a.MessagePrivacy)
                    .HasConversion<byte>()
                    .IsRequired()
                    .HasDefaultValue(MemberMessagePrivacy.Members);
                entity.Property(a => a.PasswordFailureCount).IsRequired().HasDefaultValue(0);
                entity.Property(a => a.IsSuspended).IsRequired().HasDefaultValue(false);
            });

            modelBuilder.Entity<NewsSuggestionEntity>(entity =>
            {
                entity.ToTable("NewsSuggestions");
                entity.HasKey(s => s.Id);
                entity.Ignore(s => s.DuplicateCandidate);
                entity.Property(s => s.Url).HasMaxLength(2000).IsRequired();
                entity.Property(s => s.UrlHash).HasMaxLength(64).IsRequired();
                entity.Property(s => s.Status).HasMaxLength(50).IsRequired();
                entity.HasOne(s => s.Submitter).WithMany().HasForeignKey(s => s.SubmitterMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PhotoSubmissionEntity>(entity =>
            {
                entity.ToTable("PhotoSubmissions");
                entity.HasKey(s => s.Id);
                entity.Ignore(s => s.AuditLogs);
                entity.Property(s => s.Title).HasMaxLength(200).IsRequired();
                entity.Property(s => s.BlobPath).HasMaxLength(512).IsRequired();
                entity.Property(s => s.WebOptimizedBlobPath).HasMaxLength(512).IsRequired();
                entity.Property(s => s.ThumbnailBlobPath).HasMaxLength(512).IsRequired();
                entity.Property(s => s.OriginalFileName).HasMaxLength(255).IsRequired();
                entity.Property(s => s.MimeType).HasMaxLength(100).IsRequired();
                entity.Property(s => s.Status).HasMaxLength(50).IsRequired();
                entity.HasOne(s => s.Submitter).WithMany().HasForeignKey(s => s.SubmitterMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ArticleSubmissionEntity>(entity =>
            {
                entity.ToTable("ArticleSubmissions");
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Title).HasMaxLength(300).IsRequired();
                entity.Property(a => a.Slug).HasMaxLength(300).IsRequired();
                entity.Property(a => a.Body).IsRequired();
                entity.Property(a => a.Status).HasMaxLength(50).IsRequired();
                entity.HasOne(a => a.Author).WithMany().HasForeignKey(a => a.AuthorMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
