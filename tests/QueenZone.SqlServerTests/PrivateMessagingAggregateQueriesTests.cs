using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Exercises the SQL-Server-only paths in <see cref="EfPrivateMessageRepository"/>: the inbox
/// projection that folds the unread count into the paged query, the joined-aggregate unread
/// conversation count, and the merged participant/conversation lookup used when opening a
/// conversation. These only run against a real SQL Server (<c>IsSqliteDatabase()</c> gates them
/// off in the default SQLite-backed <c>QueenZone.Web.Tests</c> suite), so they need coverage here
/// instead. See <c>docs/architecture/testing-policy.md</c> ("Modern-schema SQL Server Tests") and
/// <see cref="DashboardAggregateQueriesTests"/> for the scratch-schema pattern this mirrors.
/// </summary>
public sealed class PrivateMessagingAggregateQueriesTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneSqlServerTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfPrivateMessageRepository repository = null!;
    private readonly Guid aliceId = Guid.NewGuid();
    private readonly Guid bobId = Guid.NewGuid();
    private readonly Guid carolId = Guid.NewGuid();

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
        repository = new EfPrivateMessageRepository(dbContext);

        dbContext.MemberAccounts.AddRange(
            new MemberAccount
            {
                Id = aliceId,
                Email = "alice-sql@example.com",
                NormalizedEmail = "ALICE-SQL@EXAMPLE.COM",
                DisplayName = "Alice",
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = bobId,
                Email = "bob-sql@example.com",
                NormalizedEmail = "BOB-SQL@EXAMPLE.COM",
                DisplayName = "Bob",
                CreatedAt = DateTime.UtcNow,
            },
            new MemberAccount
            {
                Id = carolId,
                Email = "carol-sql@example.com",
                NormalizedEmail = "CAROL-SQL@EXAMPLE.COM",
                DisplayName = "Carol",
                CreatedAt = DateTime.UtcNow,
            });
        await dbContext.SaveChangesAsync();
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
    public async Task GetInboxAsync_folds_unread_count_into_the_paged_query()
    {
        var withAlice = await repository.SendNewOrExistingAsync(
            aliceId, carolId, "Hi Carol, it's Alice", DateTimeOffset.Parse("2026-08-01T10:00:00Z"));
        Assert.True(withAlice.Succeeded);
        await repository.ReplyAsync(
            withAlice.ConversationId!.Value, aliceId, "Still there?", DateTimeOffset.Parse("2026-08-01T10:05:00Z"));

        var withBob = await repository.SendNewOrExistingAsync(
            bobId, carolId, "Hi Carol, it's Bob", DateTimeOffset.Parse("2026-08-01T11:00:00Z"));
        Assert.True(withBob.Succeeded);
        var bobView = await repository.GetConversationAsync(withBob.ConversationId!.Value, carolId);
        var lastFromBob = Assert.Single(bobView!.Messages);
        await repository.MarkConversationReadAsync(
            withBob.ConversationId.Value, carolId, lastFromBob.SortKey, lastFromBob.CreatedAt);

        var page = await repository.GetInboxAsync(carolId);

        Assert.Equal(2, page.TotalCount);
        // Newest activity (Alice's reply at 10:05, but Bob's own conversation started later at 11:00) first.
        var withBobItem = Assert.Single(page.Items, i => i.ConversationId == withBob.ConversationId);
        var withAliceItem = Assert.Single(page.Items, i => i.ConversationId == withAlice.ConversationId);

        Assert.Equal("Alice", withAliceItem.OtherParticipantDisplayName);
        Assert.True(withAliceItem.HasUnread);
        Assert.Equal(2, withAliceItem.UnreadCount);

        Assert.Equal("Bob", withBobItem.OtherParticipantDisplayName);
        Assert.False(withBobItem.HasUnread);
        Assert.Equal(0, withBobItem.UnreadCount);
    }

    [Fact]
    public async Task CountUnreadConversationsAsync_counts_distinct_conversations_not_messages()
    {
        var withAlice = await repository.SendNewOrExistingAsync(
            aliceId, carolId, "First", DateTimeOffset.Parse("2026-08-02T10:00:00Z"));
        await repository.ReplyAsync(
            withAlice.ConversationId!.Value, aliceId, "Second", DateTimeOffset.Parse("2026-08-02T10:01:00Z"));
        await repository.ReplyAsync(
            withAlice.ConversationId.Value, aliceId, "Third", DateTimeOffset.Parse("2026-08-02T10:02:00Z"));

        var withBob = await repository.SendNewOrExistingAsync(
            bobId, carolId, "Read this one", DateTimeOffset.Parse("2026-08-02T11:00:00Z"));
        var bobView = await repository.GetConversationAsync(withBob.ConversationId!.Value, carolId);
        var lastFromBob = Assert.Single(bobView!.Messages);
        await repository.MarkConversationReadAsync(
            withBob.ConversationId.Value, carolId, lastFromBob.SortKey, lastFromBob.CreatedAt);

        // Three unread messages, but they're all in the same conversation with Alice.
        Assert.Equal(1, await repository.CountUnreadConversationsAsync(carolId));

        await repository.ArchiveConversationAsync(withAlice.ConversationId.Value, carolId);
        Assert.Equal(0, await repository.CountUnreadConversationsAsync(carolId));
    }

    [Fact]
    public async Task GetConversationAsync_merges_participant_and_conversation_lookup_for_both_members()
    {
        var sent = await repository.SendNewOrExistingAsync(
            aliceId, bobId, "Hello Bob", DateTimeOffset.Parse("2026-08-03T09:00:00Z"));
        Assert.True(sent.Succeeded);
        var conversationId = sent.ConversationId!.Value;

        var bobView = await repository.GetConversationAsync(conversationId, bobId);
        Assert.NotNull(bobView);
        Assert.Equal(aliceId, bobView!.OtherParticipantId);
        Assert.Equal("Alice", bobView.OtherParticipantDisplayName);
        Assert.Equal("Hello Bob", Assert.Single(bobView.Messages).Body);

        var aliceView = await repository.GetConversationAsync(conversationId, aliceId);
        Assert.NotNull(aliceView);
        Assert.Equal(bobId, aliceView!.OtherParticipantId);
        Assert.Equal("Bob", aliceView.OtherParticipantDisplayName);

        Assert.Null(await repository.GetConversationAsync(conversationId, carolId));
        Assert.Null(await repository.GetConversationAsync(Guid.NewGuid(), bobId));
    }

    // Minimal model covering only MemberAccounts + the private-messaging tables, mirroring the
    // Fluent config in QueenZoneDbContext for those entities.
    private sealed class ScratchSchemaDbContext(DbContextOptions<ScratchSchemaDbContext> options)
        : DbContext(options)
    {
        public DbSet<MemberAccount> MemberAccounts => Set<MemberAccount>();

        public DbSet<PrivateConversationEntity> PrivateConversations => Set<PrivateConversationEntity>();

        public DbSet<PrivateConversationParticipantEntity> PrivateConversationParticipants =>
            Set<PrivateConversationParticipantEntity>();

        public DbSet<PrivateMessageEntity> PrivateMessages => Set<PrivateMessageEntity>();

        public DbSet<PrivateMessageReportEntity> PrivateMessageReports => Set<PrivateMessageReportEntity>();

        public DbSet<MemberMessageBlockEntity> MemberMessageBlocks => Set<MemberMessageBlockEntity>();

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

            modelBuilder.Entity<PrivateConversationEntity>(entity =>
            {
                entity.ToTable("PrivateConversations");
                entity.HasKey(conversation => conversation.Id);

                entity.Property(conversation => conversation.LastMessagePreview)
                    .HasMaxLength(PrivateMessageLimits.PreviewLength)
                    .IsRequired();
                entity.Property(conversation => conversation.CreatedAt).IsRequired();
                entity.Property(conversation => conversation.LastMessageAt).IsRequired();
                entity.Property(conversation => conversation.LastMessageSortKey).IsRequired();

                entity.HasIndex(conversation => new { conversation.MemberLowId, conversation.MemberHighId })
                    .IsUnique()
                    .HasDatabaseName("IX_PrivateConversations_MemberPair");

                entity.HasIndex(conversation => conversation.LastMessageSortKey)
                    .IsDescending()
                    .HasDatabaseName("IX_PrivateConversations_LastMessageSortKey");

                entity.HasOne(conversation => conversation.MemberLow)
                    .WithMany()
                    .HasForeignKey(conversation => conversation.MemberLowId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(conversation => conversation.MemberHigh)
                    .WithMany()
                    .HasForeignKey(conversation => conversation.MemberHighId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PrivateConversationParticipantEntity>(entity =>
            {
                entity.ToTable("PrivateConversationParticipants");
                entity.HasKey(participant => new { participant.ConversationId, participant.MemberId });

                entity.Property(participant => participant.IsArchived).IsRequired();
                entity.Property(participant => participant.IsRemoved).IsRequired();

                entity.HasIndex(participant => new { participant.MemberId, participant.IsArchived })
                    .HasDatabaseName("IX_PrivateConversationParticipants_Member_Archived");

                entity.HasIndex(participant => new { participant.MemberId, participant.IsRemoved })
                    .HasDatabaseName("IX_PrivateConversationParticipants_Member_Removed");

                entity.HasOne(participant => participant.Conversation)
                    .WithMany(conversation => conversation.Participants)
                    .HasForeignKey(participant => participant.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(participant => participant.Member)
                    .WithMany()
                    .HasForeignKey(participant => participant.MemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PrivateMessageEntity>(entity =>
            {
                entity.ToTable("PrivateMessages");
                entity.HasKey(message => message.Id);

                entity.Property(message => message.Body)
                    .HasMaxLength(PrivateMessageLimits.MaxBodyLength)
                    .IsRequired();
                entity.Property(message => message.CreatedAt).IsRequired();
                entity.Property(message => message.SortKey)
                    .ValueGeneratedOnAdd()
                    .IsRequired();

                entity.HasIndex(message => new { message.ConversationId, message.CreatedAt })
                    .HasDatabaseName("IX_PrivateMessages_Conversation_CreatedAt");
                entity.HasIndex(message => new { message.ConversationId, message.SortKey })
                    .HasDatabaseName("IX_PrivateMessages_Conversation_SortKey");
                entity.HasIndex(message => new { message.SenderMemberId, message.CreatedAt })
                    .HasDatabaseName("IX_PrivateMessages_Sender_CreatedAt");

                entity.HasOne(message => message.Conversation)
                    .WithMany(conversation => conversation.Messages)
                    .HasForeignKey(message => message.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(message => message.Sender)
                    .WithMany()
                    .HasForeignKey(message => message.SenderMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PrivateMessageReportEntity>(entity =>
            {
                entity.ToTable("PrivateMessageReports");
                entity.HasKey(report => report.Id);

                entity.Property(report => report.Reason)
                    .HasMaxLength(PrivateMessageLimits.MaxReportReasonLength);
                entity.Property(report => report.Status)
                    .HasMaxLength(50)
                    .IsRequired();
                entity.Property(report => report.MessageBodySnapshot)
                    .HasMaxLength(PrivateMessageLimits.MaxBodyLength)
                    .IsRequired();
                entity.Property(report => report.SenderDisplayNameSnapshot)
                    .HasMaxLength(100)
                    .IsRequired();
                entity.Property(report => report.CreatedAt).IsRequired();
                entity.Property(report => report.MessageCreatedAtSnapshot).IsRequired();
                entity.Property(report => report.MessageSortKeySnapshot).IsRequired();

                entity.HasIndex(report => new { report.ReporterMemberId, report.MessageId })
                    .IsUnique()
                    .HasDatabaseName("IX_PrivateMessageReports_Reporter_Message");

                entity.HasIndex(report => new { report.Status, report.CreatedAt })
                    .IsDescending(false, true)
                    .HasDatabaseName("IX_PrivateMessageReports_Status_CreatedAt");

                entity.HasIndex(report => report.ConversationId)
                    .HasDatabaseName("IX_PrivateMessageReports_Conversation");

                entity.HasOne(report => report.Message)
                    .WithMany()
                    .HasForeignKey(report => report.MessageId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(report => report.Conversation)
                    .WithMany()
                    .HasForeignKey(report => report.ConversationId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(report => report.Reporter)
                    .WithMany()
                    .HasForeignKey(report => report.ReporterMemberId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(report => report.Reported)
                    .WithMany()
                    .HasForeignKey(report => report.ReportedMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<MemberMessageBlockEntity>(entity =>
            {
                entity.ToTable("MemberMessageBlocks");
                entity.HasKey(block => block.Id);

                entity.Property(block => block.CreatedAt).IsRequired();

                entity.HasIndex(block => new { block.BlockerMemberId, block.BlockedMemberId })
                    .IsUnique()
                    .HasDatabaseName("IX_MemberMessageBlocks_Blocker_Blocked");

                entity.HasIndex(block => block.BlockedMemberId)
                    .HasDatabaseName("IX_MemberMessageBlocks_Blocked");

                entity.HasOne(block => block.Blocker)
                    .WithMany()
                    .HasForeignKey(block => block.BlockerMemberId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(block => block.Blocked)
                    .WithMany()
                    .HasForeignKey(block => block.BlockedMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
