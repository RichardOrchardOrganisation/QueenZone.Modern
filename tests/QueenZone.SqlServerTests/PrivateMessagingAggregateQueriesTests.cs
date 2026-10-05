using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Exercises the production SQL Server paths in <see cref="EfPrivateMessageRepository"/> and
/// <see cref="EfPrivateMessageModerationRepository"/>. Inbox paging/unread fold and the joined
/// unread-conversation count now use one SQL shape on both providers (SortKey / Guid, not
/// DateTimeOffset). Rate-limit counters and report-list ordering still split because SQLite
/// cannot translate DateTimeOffset comparisons or ORDER BY. IDENTITY SortKey assignment is
/// SQL Server-only (SQLite EnsureCreated still uses the in-process helper). See
/// <c>docs/architecture/testing-policy.md</c> ("Production SQL paths: no new SQLite branches")
/// and <see cref="DashboardAggregateQueriesTests"/> for the scratch-schema pattern.
/// </summary>
public sealed class PrivateMessagingAggregateQueriesTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneSqlServerTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfPrivateMessageRepository repository = null!;
    private EfPrivateMessageModerationRepository moderation = null!;
    private readonly Guid aliceId = Guid.NewGuid();
    private readonly Guid bobId = Guid.NewGuid();
    private readonly Guid carolId = Guid.NewGuid();
    private readonly Guid daveId = Guid.NewGuid();

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
        moderation = new EfPrivateMessageModerationRepository(dbContext);

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
            },
            new MemberAccount
            {
                Id = daveId,
                Email = "dave-sql@example.com",
                NormalizedEmail = "DAVE-SQL@EXAMPLE.COM",
                DisplayName = "Dave",
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

    [Fact]
    public async Task GetInboxAsync_pages_and_orders_by_last_message_sort_key()
    {
        var withAlice = await repository.SendNewOrExistingAsync(
            aliceId, carolId, "Oldest thread", DateTimeOffset.Parse("2026-08-04T09:00:00Z"));
        var withBob = await repository.SendNewOrExistingAsync(
            bobId, carolId, "Middle thread", DateTimeOffset.Parse("2026-08-04T10:00:00Z"));
        var withDave = await repository.SendNewOrExistingAsync(
            daveId, carolId, "Newest thread", DateTimeOffset.Parse("2026-08-04T11:00:00Z"));
        Assert.True(withAlice.Succeeded && withBob.Succeeded && withDave.Succeeded);

        var page1 = await repository.GetInboxAsync(carolId, page: 1, pageSize: 2);
        var page2 = await repository.GetInboxAsync(carolId, page: 2, pageSize: 2);
        var clamped = await repository.GetInboxAsync(carolId, page: 99, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(withDave.ConversationId, page1.Items[0].ConversationId);
        Assert.Equal(withBob.ConversationId, page1.Items[1].ConversationId);
        Assert.Equal("Dave", page1.Items[0].OtherParticipantDisplayName);
        Assert.Equal("Bob", page1.Items[1].OtherParticipantDisplayName);
        Assert.Equal(withAlice.ConversationId, Assert.Single(page2.Items).ConversationId);
        Assert.Equal(page2.Items[0].ConversationId, Assert.Single(clamped.Items).ConversationId);
        Assert.Equal(2, clamped.Page);

        Assert.True(await repository.ArchiveConversationAsync(withDave.ConversationId!.Value, carolId));
        var inbox = await repository.GetInboxAsync(carolId, page: 1, pageSize: 10);
        var archived = await repository.GetArchivedInboxAsync(carolId, page: 1, pageSize: 10);
        Assert.Equal(2, inbox.TotalCount);
        Assert.Equal(
            [withBob.ConversationId!.Value, withAlice.ConversationId!.Value],
            inbox.Items.Select(i => i.ConversationId).ToArray());
        Assert.Equal(withDave.ConversationId, Assert.Single(archived.Items).ConversationId);

        var strangerId = Guid.NewGuid();
        var empty = await repository.GetInboxAsync(strangerId);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        Assert.Equal(0, await repository.CountUnreadConversationsAsync(strangerId));
    }

    [Fact]
    public async Task RateLimitCounts_use_sql_server_datetimeoffset_comparisons()
    {
        var start = DateTimeOffset.Parse("2026-08-05T10:00:00Z");
        var created = await repository.SendNewOrExistingAsync(aliceId, bobId, "Repeat", start.AddMinutes(-1));
        await repository.ReplyAsync(created.ConversationId!.Value, aliceId, "Repeat", start.AddMinutes(1));
        await repository.ReplyAsync(created.ConversationId.Value, aliceId, "Different", start.AddMinutes(2));

        Assert.Equal(2, await repository.CountMessagesBySenderSinceAsync(aliceId, start));
        Assert.Equal(0, await repository.CountMessagesBySenderSinceAsync(bobId, start));
        Assert.Equal(1, await repository.CountIdenticalMessagesBySenderSinceAsync(aliceId, "Repeat", start));
        Assert.Equal(1, await repository.CountIdenticalMessagesBySenderSinceAsync(aliceId, "Different", start));
        Assert.Equal(0, await repository.CountIdenticalMessagesBySenderSinceAsync(aliceId, "Missing", start));
    }

    [Fact]
    public async Task DistinctNewRecipients_uses_sql_server_datetimeoffset_join()
    {
        var start = DateTimeOffset.Parse("2026-08-06T10:00:00Z");
        var old = await repository.SendNewOrExistingAsync(aliceId, bobId, "Old", start.AddMinutes(-30));
        await repository.ReplyAsync(old.ConversationId!.Value, aliceId, "Reply", start.AddMinutes(1));
        await repository.SendNewOrExistingAsync(aliceId, carolId, "New", start.AddMinutes(2));

        Assert.Equal(1, await repository.CountDistinctNewRecipientsSinceAsync(aliceId, start));
        Assert.Equal(0, await repository.CountDistinctNewRecipientsSinceAsync(bobId, start));
    }

    [Fact]
    public async Task Send_assigns_identity_sort_keys_on_sql_server()
    {
        var sent = await repository.SendNewOrExistingAsync(
            aliceId, bobId, "First", DateTimeOffset.Parse("2026-08-07T09:00:00Z"));
        Assert.True(sent.Succeeded);
        var first = await repository.GetConversationAsync(sent.ConversationId!.Value, bobId);
        Assert.Equal(1, Assert.Single(first!.Messages).SortKey);

        await repository.ReplyAsync(
            sent.ConversationId.Value, bobId, "Second", DateTimeOffset.Parse("2026-08-07T09:01:00Z"));
        var both = await repository.GetConversationAsync(sent.ConversationId.Value, aliceId);
        Assert.Equal([1L, 2L], both!.Messages.Select(m => m.SortKey).ToArray());
        Assert.Equal(["First", "Second"], both.Messages.Select(m => m.Body).ToArray());
    }

    [Fact]
    public async Task ListReportsAsync_orders_pages_and_filters_on_sql_server()
    {
        var first = await repository.SendNewOrExistingAsync(
            aliceId, bobId, "To Bob", DateTimeOffset.Parse("2026-08-08T09:00:00Z"));
        var second = await repository.SendNewOrExistingAsync(
            aliceId, carolId, "To Carol", DateTimeOffset.Parse("2026-08-08T09:01:00Z"));
        var third = await repository.SendNewOrExistingAsync(
            aliceId, daveId, "To Dave", DateTimeOffset.Parse("2026-08-08T09:02:00Z"));
        var firstMessageId = (await repository.GetConversationAsync(first.ConversationId!.Value, bobId))!.Messages[^1].Id;
        var secondMessageId = (await repository.GetConversationAsync(second.ConversationId!.Value, carolId))!.Messages[^1].Id;
        var thirdMessageId = (await repository.GetConversationAsync(third.ConversationId!.Value, daveId))!.Messages[^1].Id;

        var older = await moderation.CreateReportAsync(
            bobId, first.ConversationId!.Value, firstMessageId, "Older", DateTimeOffset.Parse("2026-08-08T10:00:00Z"));
        var middle = await moderation.CreateReportAsync(
            carolId, second.ConversationId!.Value, secondMessageId, "Middle", DateTimeOffset.Parse("2026-08-08T11:00:00Z"));
        var newer = await moderation.CreateReportAsync(
            daveId, third.ConversationId!.Value, thirdMessageId, "Newer", DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
        Assert.True(older.Succeeded && middle.Succeeded && newer.Succeeded);

        var page1 = await moderation.ListReportsAsync(null, 1, 1);
        var page2 = await moderation.ListReportsAsync("all", 2, 1);
        var page3 = await moderation.ListReportsAsync(PrivateMessageReportStatus.Open, 3, 1);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(newer.ReportId, Assert.Single(page1.Items).Id);
        Assert.Equal(middle.ReportId, Assert.Single(page2.Items).Id);
        Assert.Equal(older.ReportId, Assert.Single(page3.Items).Id);
        Assert.Equal("Dave", page1.Items[0].ReporterDisplayName);
        Assert.Equal("Alice", page1.Items[0].ReportedDisplayName);

        await moderation.UpdateReportStatusAsync(
            older.ReportId!.Value, PrivateMessageReportStatus.Dismissed, "mod@example.com");
        var dismissed = await moderation.ListReportsAsync(PrivateMessageReportStatus.Dismissed, 1, 10);
        var stillOpen = await moderation.ListReportsAsync(PrivateMessageReportStatus.Open, 1, 10);
        Assert.Equal(older.ReportId, Assert.Single(dismissed.Items).Id);
        Assert.Equal(2, stillOpen.TotalCount);
        Assert.Equal(
            [newer.ReportId!.Value, middle.ReportId!.Value],
            stillOpen.Items.Select(i => i.Id).ToArray());
        Assert.Equal(2, await moderation.CountOpenReportsAsync());
    }

    // Scratch schema aligned to the 2026-09-30 queenzone_legacy_sync dump for the six modern
    // tables (no stored procedures). Extra live columns ReviewNotes/ReviewedAt/ReviewerEmail on
    // PrivateMessageReports are not mapped by EF and are omitted. Audit log has no FK to reports.
    private sealed class ScratchSchemaDbContext(DbContextOptions<ScratchSchemaDbContext> options)
        : DbContext(options)
    {
        public DbSet<MemberAccount> MemberAccounts => Set<MemberAccount>();

        public DbSet<PrivateConversationEntity> PrivateConversations => Set<PrivateConversationEntity>();

        public DbSet<PrivateConversationParticipantEntity> PrivateConversationParticipants =>
            Set<PrivateConversationParticipantEntity>();

        public DbSet<PrivateMessageEntity> PrivateMessages => Set<PrivateMessageEntity>();

        public DbSet<PrivateMessageReportEntity> PrivateMessageReports => Set<PrivateMessageReportEntity>();

        public DbSet<PrivateMessageReportAuditLogEntity> PrivateMessageReportAuditLogs =>
            Set<PrivateMessageReportAuditLogEntity>();

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
                entity.Property(a => a.ThemePreference)
                    .HasConversion<byte>()
                    .IsRequired()
                    .HasDefaultValue(MemberThemePreference.System);
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
                    .UseIdentityColumn(1, 1)
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
                entity.Property(report => report.PrecedingContextJson).HasColumnType("nvarchar(max)");

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

            modelBuilder.Entity<PrivateMessageReportAuditLogEntity>(entity =>
            {
                entity.ToTable("PrivateMessageReportAuditLog");
                entity.HasKey(log => log.Id);
                entity.Property(log => log.Id).UseIdentityColumn(1, 1);
                entity.Property(log => log.Action).HasMaxLength(50).IsRequired();
                entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
                entity.Property(log => log.OccurredAt).IsRequired();
                entity.Property(log => log.Details).HasMaxLength(2000);
                entity.HasIndex(log => new { log.ReportId, log.OccurredAt })
                    .IsDescending(false, true)
                    .HasDatabaseName("IX_PrivateMessageReportAuditLog_ReportId_OccurredAt");
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
