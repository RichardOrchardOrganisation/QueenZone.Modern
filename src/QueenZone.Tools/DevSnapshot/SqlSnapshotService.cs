using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace QueenZone.Tools;

internal sealed record BlobReference(string Container, string Name, string Budget, string Source);

[ExcludeFromCodeCoverage]
internal sealed class SqlSnapshotService(DevSnapshotConfig config, string targetConnectionString)
{
    public async Task<SqlSnapshotCopySession> OpenCopySessionAsync(string sourceConnectionString)
    {
        var source = new SqlConnection(DevSnapshotSafety.BuildReadOnlySourceConnectionString(sourceConnectionString));
        var target = new SqlConnection(targetConnectionString);
        await source.OpenAsync();
        await target.OpenAsync();

        try
        {
            await EnsureDatabaseAsync(source, config.SourceDatabase, requireReadOnly: true);
            await EnsureDatabaseAsync(target, config.TargetDatabase, requireReadOnly: false);
            return new SqlSnapshotCopySession(config, source, target);
        }
        catch
        {
            await source.DisposeAsync();
            await target.DisposeAsync();
            throw;
        }
    }

    private async Task<Dictionary<string, long>> EnsureForbiddenTablesEmptyAsync(SqlConnection target)
    {
        var forbidden = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in config.ForbiddenTables)
        {
            var count = await CountAsync(target, table);
            forbidden[table] = count;
            if (count != 0)
            {
                throw new InvalidOperationException($"Forbidden table {table} contains {count} rows.");
            }
        }

        return forbidden;
    }

    public async Task<SnapshotSummary> VerifyAsync(
        IReadOnlyList<SnapshotBlob>? manifest = null,
        bool requireSearchIndex = true)
    {
        await using var target = new SqlConnection(targetConnectionString);
        await target.OpenAsync();
        await EnsureDatabaseAsync(target, config.TargetDatabase, requireReadOnly: false);

        var forbidden = await EnsureForbiddenTablesEmptyAsync(target);

        var invalidEmails = await ScalarAsync<long>(target, """
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.MemberAccounts WHERE Email NOT LIKE '%@dev.queenzone.invalid')
              + (SELECT COUNT_BIG(*) FROM dbo.USERS_T WHERE EMAIL IS NOT NULL AND EMAIL NOT LIKE '%@example.invalid')
              + (SELECT COUNT_BIG(*) FROM dbo.NEWS_T WHERE EDITOR_EMAIL IS NOT NULL AND EDITOR_EMAIL NOT LIKE '%@dev.queenzone.invalid')
              + (SELECT COUNT_BIG(*) FROM dbo.EditorialArticles WHERE UpdatedBy IS NOT NULL AND UpdatedBy NOT LIKE '%@dev.queenzone.invalid')
              + (SELECT COUNT_BIG(*) FROM dbo.FREDDIE_T WHERE Email IS NOT NULL)
              + (SELECT COUNT_BIG(*) FROM dbo.Q_STAGE_T WHERE CONTACT IS NOT NULL);
            """);
        if (invalidEmails != 0)
        {
            throw new InvalidOperationException($"Privacy guard found {invalidEmails} non-sanitised email values.");
        }

        var credentialRows = await ScalarAsync<long>(target, """
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.USERS_T WHERE PASSWORD IS NOT NULL OR LAST_IP IS NOT NULL OR IP_ADDRESS IS NOT NULL)
              + (SELECT COUNT_BIG(*) FROM dbo.MemberAccounts
                 WHERE PasswordHash IS NOT NULL
                   AND Id NOT IN ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'))
              + CASE WHEN
                    (SELECT COUNT_BIG(*) FROM dbo.MemberAccounts
                     WHERE PasswordHash IS NOT NULL
                       AND Id IN ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222')) <> 2
                THEN 1 ELSE 0 END;
            """);
        if (credentialRows != 0)
        {
            throw new InvalidOperationException("A production password/IP value is present, or synthetic credentials are incomplete.");
        }

        var orphanRows = await ScalarAsync<long>(target, """
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.ModernForumPost p LEFT JOIN dbo.ModernForumThread t ON t.Id=p.ThreadId WHERE t.Id IS NULL)
              + (SELECT COUNT_BIG(*) FROM dbo.ModernForumThread t LEFT JOIN dbo.ModernForumCategory c ON c.Id=t.CategoryId WHERE c.Id IS NULL)
              + (SELECT COUNT_BIG(*) FROM dbo.ForumPostAttachments a LEFT JOIN dbo.ModernForumPost p ON p.Id=a.PostId WHERE p.Id IS NULL);
            """);
        if (orphanRows != 0)
        {
            throw new InvalidOperationException($"Foreign-key guard found {orphanRows} orphan rows.");
        }

        var categoryCount = checked((int)await CountAsync(target, "ModernForumCategory"));
        var representedCategories = await ScalarAsync<int>(target, "SELECT COUNT(DISTINCT CategoryId) FROM dbo.ModernForumThread;");
        if (representedCategories != categoryCount)
        {
            throw new InvalidOperationException($"Forum sample represents {representedCategories} of {categoryCount} categories.");
        }

        var forumThreads = checked((int)await CountAsync(target, "ModernForumThread"));
        if (forumThreads > config.ForumThreadCount)
        {
            throw new SnapshotSizeException($"Forum sample has {forumThreads} threads; ceiling is {config.ForumThreadCount}.");
        }

        var forumPosts = checked((int)await CountAsync(target, "ModernForumPost"));

        var newsRows = await CountAsync(target, "NEWS_T");
        if (newsRows > config.NewsArticleCount)
        {
            throw new SnapshotSizeException($"News sample has {newsRows} articles; ceiling is {config.NewsArticleCount}.");
        }

        var legacyArticleRows = await CountAsync(target, "Q_ARTICLE_T");
        var articleFileRows = await CountAsync(target, "ARTICLE_T");
        if (legacyArticleRows > config.ArticleCount || articleFileRows > config.ArticleCount)
        {
            throw new SnapshotSizeException(
                $"Article sample exceeds the {config.ArticleCount}-row per-source ceiling " +
                $"(Q_ARTICLE_T={legacyArticleRows}, ARTICLE_T={articleFileRows}).");
        }

        var usedMb = await ScalarAsync<decimal>(target, """
            SELECT CAST(SUM(FILEPROPERTY(name, 'SpaceUsed')) * 8.0 / 1024 AS decimal(18,1))
            FROM sys.database_files WHERE type_desc = 'ROWS';
            """);
        if (usedMb > config.DatabaseMaximumUsedMb)
        {
            throw new SnapshotSizeException($"Database size guard failed: {usedMb:F1} MB > {config.DatabaseMaximumUsedMb:F1} MB.");
        }

        var requiredRows = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new[]
        {
            "NEWS_T", "ARTICLE_T", "Q_ARTICLE_T", "Q_BIO_T", "QueenHistoryEvents", "TriviaFacts",
            "PIC_FILES_T", "ModernForumThread", "ModernForumPost", "SearchDocument",
        })
        {
            requiredRows[table] = await CountAsync(target, table);
        }

        foreach (var (table, count) in requiredRows.Where(pair => pair.Key != "SearchDocument"))
        {
            if (count == 0)
            {
                throw new InvalidOperationException($"Required public table {table} is empty.");
            }
        }

        if (requireSearchIndex && requiredRows["SearchDocument"] == 0)
        {
            throw new InvalidOperationException("SearchDocument is empty; rebuild the derived search index before enabling dev.");
        }

        var manifestRows = manifest ?? [];
        return new SnapshotSummary(
            DateTimeOffset.UtcNow,
            config.SourceDatabase,
            config.TargetDatabase,
            categoryCount,
            forumThreads,
            forumPosts,
            checked((int)requiredRows["PIC_FILES_T"]),
            checked((int)await CountAsync(target, "MemberAccounts")),
            checked((int)await CountAsync(target, "USERS_T")),
            manifestRows.Count,
            manifestRows.Where(blob => blob.Budget == "gallery").Sum(blob => blob.Bytes),
            manifestRows.Where(blob => blob.Budget == "forum").Sum(blob => blob.Bytes),
            usedMb,
            requiredRows.Concat(forbidden).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task EnsureDatabaseAsync(SqlConnection connection, string expected, bool requireReadOnly)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DB_NAME(), CONVERT(bit, CASE WHEN
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'EXECUTE') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 1
                OR EXISTS
                (
                    SELECT 1
                    FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
                    WHERE HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'INSERT') = 1
                       OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'UPDATE') = 1
                       OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'DELETE') = 1
                       OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'ALTER') = 1
                       OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(t.name), 'OBJECT', 'CONTROL') = 1
                )
                OR EXISTS
                (
                    SELECT 1
                    FROM sys.procedures p JOIN sys.schemas s ON s.schema_id=p.schema_id
                    WHERE HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(p.name), 'OBJECT', 'EXECUTE') = 1
                       OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(p.name), 'OBJECT', 'ALTER') = 1
                       OR HAS_PERMS_BY_NAME(QUOTENAME(s.name) + '.' + QUOTENAME(p.name), 'OBJECT', 'CONTROL') = 1
                )
                THEN 1 ELSE 0 END);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var actual = reader.GetString(0);
        var canMutate = !reader.IsDBNull(1) && reader.GetBoolean(1);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Database boundary failed: expected {expected}, got {actual}.");
        }

        if (requireReadOnly && canMutate)
        {
            throw new InvalidOperationException("Production SQL credential has mutation, DDL, control, or execute permission. A dedicated read-only credential is required.");
        }
    }

    private static Task<long> CountAsync(SqlConnection connection, string table) =>
        ScalarAsync<long>(connection, $"SELECT COUNT_BIG(*) FROM dbo.[{table.Replace("]", "]]", StringComparison.Ordinal)}];");

    internal static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 300;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}

[ExcludeFromCodeCoverage]
internal sealed class SqlSnapshotCopySession(
    DevSnapshotConfig config,
    SqlConnection source,
    SqlConnection target) : IAsyncDisposable
{
    private const int SourceCommandTimeoutSeconds = 600;

    public async Task PrepareSelectionsAsync()
    {
        // Keep temp-table creation outside the parameterized command below.
        // SqlClient executes parameterized text through sp_executesql; a temp
        // table created inside that nested scope is dropped when it returns.
        await ExecuteSourceAsync("""
            CREATE TABLE #SelectedThread (Id bigint NOT NULL PRIMARY KEY);
            CREATE TABLE #SelectedPhoto (Id int NOT NULL PRIMARY KEY);
            CREATE TABLE #SelectedNews (Id int NOT NULL PRIMARY KEY);
            CREATE TABLE #SelectedLegacyArticle (Id int NOT NULL PRIMARY KEY);
            CREATE TABLE #SelectedArticleFile (Id int NOT NULL PRIMARY KEY);
            CREATE TABLE #SelectedEditorialArticle (Id uniqueidentifier NOT NULL PRIMARY KEY);
            """);

        await ExecuteSourceAsync("""
            INSERT #SelectedNews(Id)
            SELECT TOP (@NewsArticleCount) NEWS_ID
            FROM dbo.NEWS_T
            WHERE DISPLAY=1
            ORDER BY [DATE] DESC, NEWS_ID DESC;

            INSERT #SelectedLegacyArticle(Id)
            SELECT TOP (@ArticleCount) Q_ARTICLE_ID
            FROM dbo.Q_ARTICLE_T
            WHERE DISPLAY=1
            ORDER BY DATE_CREATED DESC, Q_ARTICLE_ID DESC;

            INSERT #SelectedArticleFile(Id)
            SELECT TOP (@ArticleCount) ID
            FROM dbo.ARTICLE_T
            WHERE display=1
            ORDER BY COALESCE(PUBLICATION_DATE, Date_d) DESC, ID DESC;

            INSERT #SelectedEditorialArticle(Id)
            SELECT e.Id
            FROM dbo.EditorialArticles e
            JOIN #SelectedLegacyArticle a ON a.Id=e.LegacyArticleId;

            INSERT #SelectedEditorialArticle(Id)
            SELECT TOP (@ArticleCount) e.Id
            FROM dbo.EditorialArticles e
            WHERE NOT EXISTS (SELECT 1 FROM #SelectedEditorialArticle x WHERE x.Id=e.Id)
            ORDER BY COALESCE(e.LivePublishedAt, e.PublishedAt, e.UpdatedAt) DESC, e.Id DESC;

            ;WITH Ranked AS
            (
                SELECT t.Id, t.LegacyTopicId, t.CategoryId, t.StartedAt, t.LastActivityAt,
                       ROW_NUMBER() OVER
                           (PARTITION BY t.CategoryId ORDER BY COALESCE(t.LastActivityAt, t.StartedAt) DESC, t.Id DESC) AS CategoryRank
                FROM dbo.ModernForumThread t
                WHERE t.IsHidden=0
            ), Mandatory AS
            (
                SELECT DISTINCT Id
                FROM Ranked
                WHERE LegacyTopicId=455095 OR CategoryRank=1
            )
            INSERT #SelectedThread(Id) SELECT Id FROM Mandatory;

            INSERT #SelectedThread(Id)
            SELECT TOP
                (CASE WHEN @ForumThreadCount > (SELECT COUNT(*) FROM #SelectedThread)
                      THEN @ForumThreadCount - (SELECT COUNT(*) FROM #SelectedThread) ELSE 0 END)
                t.Id
            FROM dbo.ModernForumThread t
            WHERE t.IsHidden=0
              AND t.LegacyTopicId IN
                  (SELECT FORUM_TOPIC_ID FROM dbo.NEWS_T n JOIN #SelectedNews x ON x.Id=n.NEWS_ID
                   WHERE FORUM_TOPIC_ID IS NOT NULL)
              AND NOT EXISTS (SELECT 1 FROM #SelectedThread x WHERE x.Id=t.Id)
            ORDER BY COALESCE(t.LastActivityAt, t.StartedAt) DESC, t.Id DESC;

            INSERT #SelectedThread(Id)
            SELECT TOP
                (CASE WHEN @ForumThreadCount > (SELECT COUNT(*) FROM #SelectedThread)
                      THEN @ForumThreadCount - (SELECT COUNT(*) FROM #SelectedThread) ELSE 0 END)
                t.Id
            FROM dbo.ModernForumThread t
            WHERE t.IsHidden=0
              AND NOT EXISTS (SELECT 1 FROM #SelectedThread x WHERE x.Id=t.Id)
            ORDER BY COALESCE(t.LastActivityAt, t.StartedAt) DESC, t.Id DESC;
            """,
            ("@NewsArticleCount", config.NewsArticleCount),
            ("@ArticleCount", config.ArticleCount),
            ("@ForumThreadCount", config.ForumThreadCount));

        var selectedThreads = await SqlSnapshotService.ScalarAsync<int>(source, "SELECT COUNT(*) FROM #SelectedThread;");
        if (selectedThreads > config.ForumThreadCount)
        {
            throw new InvalidOperationException(
                $"Required category, news, and forum-guideline topics exceed the {config.ForumThreadCount}-thread limit.");
        }
    }

    public async Task<IReadOnlyList<PhotoCandidate>> GetPhotoCandidatesAsync()
    {
        await using var command = source.CreateCommand();
        command.CommandText = """
            ;WITH Ranked AS
            (
                SELECT p.PIC_ID, p.Cat_ID, p.Url, p.Thumb_URL,
                       ROW_NUMBER() OVER (PARTITION BY p.Cat_ID ORDER BY p.Date_time DESC, p.PIC_ID DESC) AS SampleRank
                FROM dbo.PIC_FILES_T p WHERE p.DISPLAY=1
            ), Required AS
            (
                SELECT IMAGE_GALLERY_PIC_ID AS PIC_ID
                FROM dbo.NEWS_T n JOIN #SelectedNews x ON x.Id=n.NEWS_ID
                WHERE n.DISPLAY=1 AND n.IMAGE_GALLERY_PIC_ID IS NOT NULL
            )
            SELECT r.PIC_ID, r.Cat_ID, r.Url, r.Thumb_URL, r.SampleRank,
                   CONVERT(bit, CASE WHEN q.PIC_ID IS NULL THEN 0 ELSE 1 END) IsRequired
            FROM Ranked r
            LEFT JOIN Required q ON q.PIC_ID=r.PIC_ID
            WHERE r.SampleRank <= @PhotosPerCategory OR q.PIC_ID IS NOT NULL
            UNION
            SELECT p.PIC_ID,p.Cat_ID,p.Url,p.Thumb_URL,0,CONVERT(bit,1)
            FROM dbo.PIC_FILES_T p JOIN Required q ON q.PIC_ID=p.PIC_ID
            WHERE NOT EXISTS (SELECT 1 FROM Ranked r WHERE r.PIC_ID=p.PIC_ID)
            ORDER BY SampleRank, Cat_ID, PIC_ID;
            """;
        command.Parameters.AddWithValue("@PhotosPerCategory", config.PhotosPerCategory);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<PhotoCandidate>();
        while (await reader.ReadAsync())
        {
            result.Add(new PhotoCandidate(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(5)));
        }

        return result;
    }

    public async Task SetSelectedPhotosAsync(IReadOnlyList<int> ids)
    {
        const int batchSize = 500;
        for (var offset = 0; offset < ids.Count; offset += batchSize)
        {
            var batch = ids.Skip(offset).Take(batchSize).ToArray();
            await using var command = source.CreateCommand();
            command.CommandText = "INSERT #SelectedPhoto(Id) VALUES "
                + string.Join(",", batch.Select((_, index) => $"(@p{index})")) + ";";
            for (var index = 0; index < batch.Length; index++)
            {
                command.Parameters.AddWithValue($"@p{index}", batch[index]);
            }

            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<IReadOnlyList<BlobReference>> GetBlobReferencesAsync()
    {
        var result = new List<BlobReference>();
        await ReadBlobReferencesAsync("""
            SELECT 'attachments', p.Attachment, 'forum', CONCAT('ModernForumPost:', p.Id)
            FROM dbo.ModernForumPost p JOIN #SelectedThread t ON t.Id=p.ThreadId
            WHERE NULLIF(LTRIM(RTRIM(p.Attachment)), '') IS NOT NULL
            UNION ALL
            SELECT a.ContainerName, a.BlobPath, 'forum', CONCAT('ForumPostAttachments:', a.Id)
            FROM dbo.ForumPostAttachments a
            JOIN dbo.ModernForumPost p ON p.Id=a.PostId JOIN #SelectedThread t ON t.Id=p.ThreadId;
            """, result);

        await ReadEditorialBlobsAsync("""
            SELECT n.IMAGE_BLOB_KEY, CONCAT('NEWS_T:', n.NEWS_ID)
            FROM dbo.NEWS_T n JOIN #SelectedNews x ON x.Id=n.NEWS_ID
            WHERE n.DISPLAY=1 AND n.IMAGE_BLOB_KEY IS NOT NULL;
            """, result);
        await ReadEditorialBlobsAsync("""
            SELECT e.ImageBlobKey, CONCAT('EditorialArticles:', e.Id)
            FROM dbo.EditorialArticles e JOIN #SelectedEditorialArticle x ON x.Id=e.Id
            WHERE e.ImageBlobKey IS NOT NULL
            UNION ALL
            SELECT e.LiveImageBlobKey, CONCAT('EditorialArticles-live:', e.Id)
            FROM dbo.EditorialArticles e JOIN #SelectedEditorialArticle x ON x.Id=e.Id
            WHERE e.LiveImageBlobKey IS NOT NULL;
            """, result);
        return result;
    }

    public async Task ResetTargetAsync()
    {
        await ExecuteTargetAsync("""
            DECLARE @sql nvarchar(max)='';
            SELECT @sql += 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ' NOCHECK CONSTRAINT ALL;'
                         + 'DISABLE TRIGGER ALL ON ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ';'
            FROM sys.tables;
            EXEC sys.sp_executesql @sql;
            SET @sql='';
            SELECT @sql += 'DELETE FROM ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ';'
            FROM sys.tables;
            EXEC sys.sp_executesql @sql;
            """);
    }

    public async Task CopyRowsAsync()
    {
        await CopyTableAsync("__EFMigrationsHistory", "SELECT * FROM dbo.[__EFMigrationsHistory];");

        foreach (var table in config.PublicTables)
        {
            await CopyTableAsync(table, $"SELECT * FROM dbo.[{Escape(table)}]");
        }

        await CopyTableAsync("NEWS_T", """
            SELECT NEWS_ID,TITLE,EXCERPT,ARTICLE,[DATE],USER_ID,DISPLAY,[TYPE],QUEEN_ONLINE,SOURCE_URL,SLUG,CREATED_AT,UPDATED_AT,
                   CASE WHEN EDITOR_EMAIL IS NULL THEN NULL ELSE CONCAT('editor-',NEWS_ID,'@dev.queenzone.invalid') END AS EDITOR_EMAIL,
                   IMAGE_BLOB_KEY,IMAGE_GALLERY_PIC_ID,FORUM_TOPIC_ID
            FROM dbo.NEWS_T n JOIN #SelectedNews x ON x.Id=n.NEWS_ID;
            """);
        await CopyTableAsync("FREDDIE_T", "SELECT ID,Name,Thought,CAST(NULL AS varchar(60)) Email,Freddie_Date,Freddie_Time,Country,DISPLAY FROM dbo.FREDDIE_T;");
        await CopyTableAsync("Q_STAGE_T", "SELECT Q_STAGE_ID,TITLE,PERFORMED_BY,DESCRIPTION,URL,THESIZE,DATE_ADDED,DISPLAY,CAST(NULL AS varchar(300)) CONTACT,USER_ID,ALLOW_RATING FROM dbo.Q_STAGE_T;");
        await CopyTableAsync("EditorialArticles", """
            SELECT e.Id,e.LegacyArticleId,e.SourceSubmissionId,e.Title,e.Slug,e.Excerpt,e.Body,e.AuthorName,e.Category,e.Tags,e.Source,e.ImageBlobKey,e.Status,e.PublishedAt,e.UpdatedAt,
                   CONCAT('editor-',CONVERT(varchar(36),e.Id),'@dev.queenzone.invalid') UpdatedBy,
                   e.LiveTitle,e.LiveSlug,e.LiveExcerpt,e.LiveBody,e.LiveAuthorName,e.LiveCategory,e.LiveTags,e.LiveSource,e.LiveImageBlobKey,e.LivePublishedAt
            FROM dbo.EditorialArticles e JOIN #SelectedEditorialArticle x ON x.Id=e.Id;
            """);
        await CopyTableAsync("ARTICLE_T", "SELECT a.* FROM dbo.ARTICLE_T a JOIN #SelectedArticleFile x ON x.Id=a.ID;");
        await CopyTableAsync("Q_ARTICLE_T", "SELECT a.* FROM dbo.Q_ARTICLE_T a JOIN #SelectedLegacyArticle x ON x.Id=a.Q_ARTICLE_ID;");
        await CopyTableAsync("PIC_CAT_T", "SELECT * FROM dbo.PIC_CAT_T;");
        await CopyTableAsync("PIC_FILES_T", "SELECT p.* FROM dbo.PIC_FILES_T p JOIN #SelectedPhoto x ON x.Id=p.PIC_ID;");
        await CopyTableAsync("USERS_T", SanitizedLegacyUsersSql);
        await CopyTableAsync("MemberAccounts", SanitizedMembersSql);
        await CopyTableAsync("ModernForumCategory", "SELECT * FROM dbo.ModernForumCategory;");
        await CopyTableAsync("ModernForumThread", "SELECT t.* FROM dbo.ModernForumThread t JOIN #SelectedThread x ON x.Id=t.Id;");
        await CopyTableAsync("ModernForumPost", "SELECT p.* FROM dbo.ModernForumPost p JOIN #SelectedThread x ON x.Id=p.ThreadId;");
        await CopyTableAsync("ForumPostAttachments", "SELECT a.* FROM dbo.ForumPostAttachments a JOIN dbo.ModernForumPost p ON p.Id=a.PostId JOIN #SelectedThread x ON x.Id=p.ThreadId;");
        await CopyTableAsync("ModernForumThreadReadStats", "SELECT s.* FROM dbo.ModernForumThreadReadStats s JOIN #SelectedThread x ON x.Id=s.ThreadId;");
        await CopyTableAsync("ForumPolls", "SELECT p.* FROM dbo.ForumPolls p JOIN #SelectedThread x ON x.Id=p.ThreadId;");
        await CopyTableAsync("ForumPollOptions", "SELECT o.* FROM dbo.ForumPollOptions o JOIN dbo.ForumPolls p ON p.Id=o.PollId JOIN #SelectedThread x ON x.Id=p.ThreadId;");
    }

    public async Task RemoveMissingForumBlobReferencesAsync(IReadOnlyList<MissingForumBlobReference> missing)
    {
        var legacyPostIds = missing.Where(item => item.LegacyPostId.HasValue)
            .Select(item => item.LegacyPostId!.Value)
            .Distinct()
            .ToArray();
        var attachmentIds = missing.Where(item => item.AttachmentId.HasValue)
            .Select(item => item.AttachmentId!.Value)
            .Distinct()
            .ToArray();
        var affectedPostIds = legacyPostIds.ToHashSet();

        foreach (var batch in attachmentIds.Chunk(500))
        {
            await using (var select = target.CreateCommand())
            {
                var parameters = AddIdParameters(select, batch);
                select.CommandText = $"SELECT DISTINCT PostId FROM dbo.ForumPostAttachments WHERE Id IN ({parameters});";
                select.CommandTimeout = 1200;
                await using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync()) affectedPostIds.Add(reader.GetInt64(0));
            }

            await using var delete = target.CreateCommand();
            var deleteParameters = AddIdParameters(delete, batch);
            delete.CommandText = $"DELETE FROM dbo.ForumPostAttachments WHERE Id IN ({deleteParameters});";
            delete.CommandTimeout = 1200;
            await delete.ExecuteNonQueryAsync();
        }

        foreach (var batch in legacyPostIds.Chunk(500))
        {
            await using var update = target.CreateCommand();
            var parameters = AddIdParameters(update, batch);
            update.CommandText = $"UPDATE dbo.ModernForumPost SET Attachment=NULL, FileSize=NULL WHERE Id IN ({parameters});";
            update.CommandTimeout = 1200;
            await update.ExecuteNonQueryAsync();
        }

        foreach (var batch in affectedPostIds.Chunk(500))
        {
            await using var update = target.CreateCommand();
            var parameters = AddIdParameters(update, batch);
            update.CommandText = $"""
                UPDATE p
                SET AttachCount = CASE WHEN NULLIF(LTRIM(RTRIM(p.Attachment)), '') IS NULL THEN 0 ELSE 1 END
                    + (SELECT COUNT(*) FROM dbo.ForumPostAttachments a WHERE a.PostId=p.Id)
                FROM dbo.ModernForumPost p
                WHERE p.Id IN ({parameters});
                """;
            update.CommandTimeout = 1200;
            await update.ExecuteNonQueryAsync();
        }
    }

    public async Task RemoveMissingEditorialBlobReferencesAsync(IReadOnlyList<MissingEditorialBlobReference> missing)
    {
        var legacyNewsIds = missing.Where(item => item.LegacyNewsId.HasValue)
            .Select(item => item.LegacyNewsId!.Value)
            .Distinct()
            .ToArray();
        var draftArticleIds = missing.Where(item => item.EditorialArticleId.HasValue && !item.IsLive)
            .Select(item => item.EditorialArticleId!.Value)
            .Distinct()
            .ToArray();
        var liveArticleIds = missing.Where(item => item.EditorialArticleId.HasValue && item.IsLive)
            .Select(item => item.EditorialArticleId!.Value)
            .Distinct()
            .ToArray();

        foreach (var batch in legacyNewsIds.Chunk(500))
        {
            await using var update = target.CreateCommand();
            var parameters = AddIdParameters(update, batch);
            update.CommandText = $"UPDATE dbo.NEWS_T SET IMAGE_BLOB_KEY=NULL WHERE NEWS_ID IN ({parameters});";
            update.CommandTimeout = 1200;
            await update.ExecuteNonQueryAsync();
        }

        foreach (var batch in draftArticleIds.Chunk(500))
        {
            await using var update = target.CreateCommand();
            var parameters = AddIdParameters(update, batch);
            update.CommandText = $"UPDATE dbo.EditorialArticles SET ImageBlobKey=NULL WHERE Id IN ({parameters});";
            update.CommandTimeout = 1200;
            await update.ExecuteNonQueryAsync();
        }

        foreach (var batch in liveArticleIds.Chunk(500))
        {
            await using var update = target.CreateCommand();
            var parameters = AddIdParameters(update, batch);
            update.CommandText = $"UPDATE dbo.EditorialArticles SET LiveImageBlobKey=NULL WHERE Id IN ({parameters});";
            update.CommandTimeout = 1200;
            await update.ExecuteNonQueryAsync();
        }
    }

    public async Task SeedSyntheticAccountsAsync(string adminPassword, string memberPassword)
    {
        var hasher = new PasswordHasher<object>();
        var adminHash = hasher.HashPassword(new object(), adminPassword);
        var memberHash = hasher.HashPassword(new object(), memberPassword);
        await using var command = target.CreateCommand();
        command.CommandText = """
            INSERT dbo.MemberAccounts(Id,Email,NormalizedEmail,DisplayName,PasswordHash,CreatedAt,IsSuspended,MessagePrivacy)
            VALUES
              ('11111111-1111-1111-1111-111111111111','admin@dev.queenzone.invalid','ADMIN@DEV.QUEENZONE.INVALID','Dev snapshot admin',@AdminHash,SYSUTCDATETIME(),0,0),
              ('22222222-2222-2222-2222-222222222222','member@dev.queenzone.invalid','MEMBER@DEV.QUEENZONE.INVALID','Dev snapshot member',@MemberHash,SYSUTCDATETIME(),0,0);
            """;
        command.Parameters.AddWithValue("@AdminHash", adminHash);
        command.Parameters.AddWithValue("@MemberHash", memberHash);
        await command.ExecuteNonQueryAsync();
    }

    public async Task FinalizeTargetAsync()
    {
        await ExecuteTargetAsync("""
            IF OBJECT_ID('dbo.ModernForum_RefreshReadStats','P') IS NOT NULL EXEC dbo.ModernForum_RefreshReadStats;
            DECLARE @sql nvarchar(max)='';
            SELECT @sql += 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ' WITH CHECK CHECK CONSTRAINT ALL;'
                         + 'ENABLE TRIGGER ALL ON ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ';'
            FROM sys.tables;
            EXEC sys.sp_executesql @sql;
            """);
    }

    private async Task CopyTableAsync(string table, string query)
    {
        var columns = await GetInsertableColumnsAsync(table);
        await using var command = source.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = SourceCommandTimeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var sourceColumns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapped = columns.Where(sourceColumns.Contains).ToArray();
        if (mapped.Length == 0)
        {
            throw new InvalidOperationException($"No insertable columns found for {table}.");
        }

        using var bulk = new SqlBulkCopy(target, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = $"dbo.[{Escape(table)}]",
            BulkCopyTimeout = 1200,
            BatchSize = 5000,
        };
        foreach (var column in mapped)
        {
            bulk.ColumnMappings.Add(column, column);
        }

        await bulk.WriteToServerAsync(reader);
    }

    private async Task<IReadOnlyList<string>> GetInsertableColumnsAsync(string table)
    {
        await using var command = target.CreateCommand();
        command.CommandText = """
            SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
            WHERE t.schema_id=SCHEMA_ID('dbo') AND t.name=@Table AND c.is_computed=0 AND c.system_type_id<>189
            ORDER BY c.column_id;
            """;
        command.Parameters.AddWithValue("@Table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private async Task ReadBlobReferencesAsync(string sql, List<BlobReference> destination)
    {
        await using var command = source.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = SourceCommandTimeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            destination.Add(new BlobReference(reader.GetString(0), reader.GetString(1).Trim().TrimStart('/'), reader.GetString(2), reader.GetString(3)));
        }
    }

    private async Task ReadEditorialBlobsAsync(string sql, List<BlobReference> destination)
    {
        await using var command = source.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = SourceCommandTimeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0).Trim();
            if (key.StartsWith("gallery:", StringComparison.OrdinalIgnoreCase)) continue;
            var slash = key.IndexOf('/');
            var container = slash > 0 ? key[..slash] : "ugc-articles";
            var name = slash > 0 ? key[(slash + 1)..] : key;
            destination.Add(new BlobReference(container, name, "gallery", reader.GetString(1)));
        }
    }

    private async Task ExecuteSourceAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = source.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = SourceCommandTimeoutSeconds;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteTargetAsync(string sql)
    {
        await using var command = target.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 1200;
        await command.ExecuteNonQueryAsync();
    }

    private static string AddIdParameters<T>(SqlCommand command, IReadOnlyList<T> ids)
    {
        var names = new string[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            names[index] = $"@id{index}";
            command.Parameters.AddWithValue(names[index], ids[index]!);
        }

        return string.Join(",", names);
    }

    private static string Escape(string value) => value.Replace("]", "]]", StringComparison.Ordinal);

    public async ValueTask DisposeAsync()
    {
        await source.DisposeAsync();
        await target.DisposeAsync();
    }

    private const string SanitizedMembersSql = """
        SELECT DISTINCT m.Id,
               CONCAT('member-',LOWER(CONVERT(varchar(36),m.Id)),'@dev.queenzone.invalid') Email,
               UPPER(CONCAT('member-',CONVERT(varchar(36),m.Id),'@dev.queenzone.invalid')) NormalizedEmail,
               CONCAT('Dev member ',LEFT(CONVERT(varchar(36),m.Id),8)) DisplayName,CAST(NULL AS nvarchar(1024)) PasswordHash,m.CreatedAt,m.LinkedLegacyUserId,
               CAST(NULL AS nvarchar(1024)) AvatarUrl,CAST(NULL AS datetime2) LastLoginAt,m.IsSuspended,m.SuspendedAt,
               CAST(NULL AS nvarchar(512)) SuspendedByAdminEmail,CAST(NULL AS nvarchar(2000)) SuspendedReason,m.DeletionRequestedAt,m.PersonalDataPurgedAt,
               CAST(NULL AS nvarchar(1024)) DeletionRecoveryAvatarUrl,CAST(NULL AS nvarchar(200)) DeletionRecoveryDisplayName,m.MessagePrivacy
        FROM dbo.MemberAccounts m
        WHERE EXISTS (SELECT 1 FROM dbo.ModernForumPost p JOIN #SelectedThread t ON t.Id=p.ThreadId WHERE p.AuthorMemberId=m.Id)
           OR EXISTS (SELECT 1 FROM dbo.ForumPolls p JOIN #SelectedThread t ON t.Id=p.ThreadId WHERE p.CreatedByMemberId=m.Id);
        """;

    private const string SanitizedLegacyUsersSql = """
        SELECT u.USER_ID,CASE WHEN u.USERNAME LIKE '%@%' THEN CONCAT('legacy-',u.USER_ID) ELSE u.USERNAME END USERNAME,u.COUNTRY,CAST(NULL AS varchar(150)) HOMEPAGE,CAST(NULL AS varchar(50)) [PASSWORD],u.ACCESS,
               CONVERT(tinyint,0) EMAIL_YES,CAST(NULL AS varchar(150)) PICTURE,CAST(NULL AS varchar(150)) PICTURE_TEXT,
               CONCAT('legacy-',u.USER_ID,'@example.invalid') EMAIL,CAST(NULL AS varchar(200)) [SIGNATURE],u.NUMBER_OF_POSTS,u.DATE_CREATED,
               u.VALIDATED,u.Q_GENDER_ID,u.Q_ALBUM_ID,u.Q_MEMBER_ID,CAST(NULL AS varchar(60)) OCCUPATION_JOB,
               CAST(NULL AS varchar(2000)) HOBBIES,CAST(NULL AS varchar(4000)) GENERAL_INFORMATION,CAST(NULL AS varchar(100)) QUEEN_FAN_SINCE,
               CAST(NULL AS smallint) PICTURE_HEIGHT,CAST(NULL AS smallint) PICTURE_WIDTH,CAST(NULL AS varchar(10)) DATE_OF_BIRTH,
               u.Q_FRIEND_ID,CAST(NULL AS varchar(80)) Q_FRIENDS_EMAIL,CAST(NULL AS varchar(150)) ADDITIONAL_LOCATION_INFORMATION,
               CONVERT(tinyint,0) PEN_PAL,CONVERT(tinyint,0) REAL_PAL,CAST(NULL AS varchar(50)) QUEENZONE_POSITION,
               CAST(NULL AS char(15)) LAST_IP,CAST(NULL AS smalldatetime) LAST_LOGIN,CONVERT(bigint,0) UPLOADED,CONVERT(bigint,0) DOWNLOADED,
               CONVERT(tinyint,0) ONLINE_NOW,CONVERT(tinyint,0) EMAIL_PM,CAST(NULL AS char(25)) IP_ADDRESS,
               CAST(NULL AS varchar(25)) IM_MESSAGE,CAST(NULL AS varchar(130)) DISPLAY_MESSAGE,CONVERT(tinyint,0) DISPLAY_AVATAR,
               CAST(NULL AS char(10)) BGCOLOR,CAST(NULL AS varchar(1000)) HOW_QUEEN,CAST(NULL AS varchar(1000)) FAVE_QUEEN_ITEM,
               u.VIEW_ADS,u.APPLICATION_NAME
        FROM dbo.USERS_T u
        WHERE EXISTS (SELECT 1 FROM dbo.ModernForumThread t JOIN #SelectedThread x ON x.Id=t.Id WHERE t.StartedByLegacyUserId=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.ModernForumPost p JOIN #SelectedThread x ON x.Id=p.ThreadId WHERE p.AuthorLegacyUserId=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.PIC_FILES_T p JOIN #SelectedPhoto x ON x.Id=p.PIC_ID WHERE p.user_id=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.NEWS_T n JOIN #SelectedNews x ON x.Id=n.NEWS_ID WHERE n.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.ARTICLE_T a JOIN #SelectedArticleFile x ON x.Id=a.ID WHERE a.User_id=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_ARTICLE_T a JOIN #SelectedLegacyArticle x ON x.Id=a.Q_ARTICLE_ID WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_STAGE_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_KNOWLEDGE_BASE_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_MUSIC_CHART_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_POLL_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_QUIZ_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_QUIZ_QUESTION_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.Q_TIMELINE_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.QUEEN_EVENT_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.QUEEN_FEATURED_SITE_T a WHERE a.USER_ID=u.USER_ID)
           OR EXISTS (SELECT 1 FROM dbo.QUEEN_QUOTE_T a WHERE a.USER_ID=u.USER_ID);
        """;
}
