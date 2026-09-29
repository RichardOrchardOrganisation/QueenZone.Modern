using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Unique scratch SQL Server database for modern forum procedure and read-stat tests (#1892).
/// </summary>
public sealed class ModernForumSqlServerDatabase : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneForumTests_{Guid.NewGuid():N}";
    private readonly List<QueenZoneDbContext> contexts = [];
    private bool created;

    public string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await using (var schema = new EmptySchemaContext(SchemaOptions()))
            {
                await schema.Database.EnsureCreatedAsync();
                await ModernForumSqlServerSchema.InstallAsync(schema);
            }

            created = true;
        }
        catch
        {
            await TryDeleteAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }

        contexts.Clear();
        await TryDeleteAsync();
    }

    public QueenZoneDbContext CreateContext(bool enableRetry = false)
    {
        var builder = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(
            ConnectionString,
            sql =>
            {
                sql.CommandTimeout(QueenZoneSqlServerOptions.DefaultCommandTimeoutSeconds);
                if (enableRetry)
                {
                    sql.EnableRetryOnFailure(
                        QueenZoneSqlServerOptions.MaxRetryCount,
                        QueenZoneSqlServerOptions.MaxRetryDelay,
                        errorNumbersToAdd: null);
                }
            });
        var context = new QueenZoneDbContext(builder.Options);
        contexts.Add(context);
        return context;
    }

    public async Task InstallSequencesAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await ModernForumSqlServerSchema.InstallSequencesAsync(schema);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.ExecuteSqlRawAsync(sql);
    }

    public Task SeedCategoryAsync(
        int id,
        int legacyForumId,
        string name,
        int sortOrder,
        int legacyPostCount = 0,
        string? description = null,
        DateTime? lastActivityAt = null,
        bool isSynthetic = false) =>
        ExecuteAsync($"""
            SET IDENTITY_INSERT dbo.ModernForumCategory ON;
            INSERT INTO dbo.ModernForumCategory
                (Id, LegacyForumId, Name, Description, SortOrder, LegacyPostCount, LastActivityAt, IsSynthetic)
            VALUES
                ({id}, {legacyForumId}, N'{Escape(name)}', {NvarcharOrNull(description)}, {sortOrder},
                 {legacyPostCount}, {DateOrNull(lastActivityAt)}, {(isSynthetic ? 1 : 0)});
            SET IDENTITY_INSERT dbo.ModernForumCategory OFF;
            """);

    public Task SeedThreadAsync(
        long id,
        int legacyTopicId,
        int legacyForumId,
        int categoryId,
        string title,
        string startedByDisplayName,
        int replyCount = 0,
        bool isSticky = false,
        bool isLegacyTopicStarter = true,
        bool? startedByUserValidated = true,
        bool isHidden = false,
        DateTime? lastActivityAt = null,
        DateTime? startedAt = null,
        int? startedByLegacyUserId = null,
        byte legacyDiscography = 0) =>
        ExecuteAsync($"""
            SET IDENTITY_INSERT dbo.ModernForumThread ON;
            INSERT INTO dbo.ModernForumThread
            (
                Id, LegacyTopicId, LegacyForumId, CategoryId, Title, StartedByLegacyUserId,
                StartedByDisplayName, StartedAt, LastActivityAt, ReplyCount, IsSticky,
                IsLegacyTopicStarter, LegacyDiscography, StartedByUserValidated, IsHidden,
                StarterAttachCount
            )
            VALUES
            (
                {id}, {legacyTopicId}, {legacyForumId}, {categoryId}, N'{Escape(title)}',
                {IntOrNull(startedByLegacyUserId)}, N'{Escape(startedByDisplayName)}',
                {DateOrNull(startedAt)}, {DateOrNull(lastActivityAt)}, {replyCount},
                {(isSticky ? 1 : 0)}, {(isLegacyTopicStarter ? 1 : 0)}, {legacyDiscography},
                {BitOrNull(startedByUserValidated)}, {(isHidden ? 1 : 0)}, 0
            );
            SET IDENTITY_INSERT dbo.ModernForumThread OFF;
            """);

    public Task SeedPostAsync(
        long id,
        int legacyPostId,
        int legacyThreadTopicId,
        long threadId,
        int legacyForumId,
        string authorDisplayName,
        string bodyHtml,
        DateTime? postedAt = null,
        int? authorLegacyUserId = null,
        Guid? authorMemberId = null,
        int? authorPostCount = null,
        DateTime? authorJoinedAt = null,
        string? signatureHtml = null,
        DateTime? editedAt = null,
        int editCount = 0,
        string? attachment = null,
        string? fileSize = null,
        bool isHidden = false) =>
        ExecuteAsync($"""
            SET IDENTITY_INSERT dbo.ModernForumPost ON;
            INSERT INTO dbo.ModernForumPost
            (
                Id, LegacyPostId, LegacyThreadTopicId, ThreadId, LegacyForumId, AuthorLegacyUserId,
                AuthorMemberId, AuthorDisplayName, AuthorPostCount, AuthorJoinedAt, BodyHtml,
                SignatureHtml, PostedAt, EditedAt, EditCount, LegacyDiscography, AttachCount,
                Attachment, FileSize, IsHidden
            )
            VALUES
            (
                {id}, {legacyPostId}, {legacyThreadTopicId}, {threadId}, {legacyForumId},
                {IntOrNull(authorLegacyUserId)}, {GuidOrNull(authorMemberId)},
                N'{Escape(authorDisplayName)}', {IntOrNull(authorPostCount)}, {DateOrNull(authorJoinedAt)},
                N'{Escape(bodyHtml)}', {NvarcharOrNull(signatureHtml)}, {DateOrNull(postedAt)},
                {DateOrNull(editedAt)}, {editCount}, 0, 0, {VarcharOrNull(attachment)},
                {VarcharOrNull(fileSize)}, {(isHidden ? 1 : 0)}
            );
            SET IDENTITY_INSERT dbo.ModernForumPost OFF;
            """);

    public Task SeedCategoryStatsAsync(int categoryId, int legacyForumId, int totalThreads, int validatedDisplayThreads) =>
        ExecuteAsync($"""
            INSERT INTO dbo.ModernForumCategoryReadStats
                (CategoryId, LegacyForumId, TotalThreads, ValidatedDisplayThreads)
            VALUES ({categoryId}, {legacyForumId}, {totalThreads}, {validatedDisplayThreads});
            """);

    public Task SeedThreadStatsAsync(long threadId, int legacyTopicId, int postCount, DateTime? updatedAt = null) =>
        ExecuteAsync($"""
            INSERT INTO dbo.ModernForumThreadReadStats (ThreadId, LegacyTopicId, PostCount, UpdatedAt)
            VALUES ({threadId}, {legacyTopicId}, {postCount}, {(updatedAt is null ? "SYSUTCDATETIME()" : DateOrNull(updatedAt))});
            """);

    public Task SeedArchiveStatsAsync(int totalThreads, int sitemapTopicCount) =>
        ExecuteAsync($"""
            INSERT INTO dbo.ModernForumArchiveReadStats (Id, TotalThreads, SitemapTopicCount)
            VALUES (1, {totalThreads}, {sitemapTopicCount});
            """);

    public Task SeedPollAsync(long threadId, int legacyTopicId) =>
        ExecuteAsync($"""
            INSERT INTO dbo.ForumPolls
                (Id, ThreadId, LegacyTopicId, Question, IsMultiChoice, CreatedByMemberId, CreatedAt)
            VALUES
                ('{Guid.NewGuid():D}', {threadId}, {legacyTopicId}, N'Test poll', 0,
                 '{Guid.NewGuid():D}', SYSDATETIMEOFFSET());
            """);

    public Task SeedModernAttachmentAsync(long postId, int legacyPostId, string fileName, DateTimeOffset uploadedAt) =>
        ExecuteAsync($"""
            INSERT INTO dbo.ForumPostAttachments
            (
                Id, PostId, LegacyPostId, OriginalFileName, BlobPath, ContainerName,
                FileSizeBytes, MimeType, UploadedAt, DownloadCount
            )
            VALUES
            (
                '{Guid.NewGuid():D}', {postId}, {legacyPostId}, N'{Escape(fileName)}',
                N'posts/{legacyPostId}/{fileName}', N'ugc-forum', 2048, N'image/jpeg',
                '{uploadedAt:O}', 0
            );
            """);

    public Task RefreshReadStatsAsync() =>
        ExecuteAsync("EXEC dbo.ModernForum_RefreshReadStats;");

    private async Task TryDeleteAsync()
    {
        if (!created)
        {
            try
            {
                await using var schema = new EmptySchemaContext(SchemaOptions());
                await schema.Database.EnsureDeletedAsync();
            }
            catch (Exception)
            {
                // Creating the database may have failed before it existed.
            }

            return;
        }

        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureDeletedAsync();
        }

        created = false;
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string NvarcharOrNull(string? value) =>
        value is null ? "NULL" : $"N'{Escape(value)}'";

    private static string VarcharOrNull(string? value) =>
        value is null ? "NULL" : $"'{Escape(value)}'";

    private static string DateOrNull(DateTime? value) =>
        value is null ? "NULL" : $"'{value.Value:yyyy-MM-ddTHH:mm:ss}'";

    private static string IntOrNull(int? value) => value is null ? "NULL" : value.Value.ToString();

    private static string BitOrNull(bool? value) => value is null ? "NULL" : (value.Value ? "1" : "0");

    private static string GuidOrNull(Guid? value) => value is null ? "NULL" : $"'{value.Value:D}'";

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
