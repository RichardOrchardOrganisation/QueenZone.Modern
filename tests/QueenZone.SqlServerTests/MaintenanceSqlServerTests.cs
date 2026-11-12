using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Tools;

namespace QueenZone.SqlServerTests;

public sealed class MaintenanceSqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneMaintenanceTests_{Guid.NewGuid():N}";
    private string ConnectionString => new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
        ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
    {
        InitialCatalog = databaseName,
    }.ConnectionString;

    private ScratchContext CreateContext() => new(new DbContextOptionsBuilder<ScratchContext>()
        .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.PIC_FILES_T
                (PIC_ID int PRIMARY KEY, Url varchar(200), Thumb_URL varchar(200), DISPLAY int,
                 PIC_WIDTH int NULL, PIC_HEIGHT int NULL, Cat_ID int);
            INSERT dbo.PIC_FILES_T VALUES
                (1, 'one', '', 1, NULL, 0, 9), (2, 'two', '', 0, 0, 0, 9),
                (3, 'three', '', 1, 800, 600, 9), (4, 'four', '', 1, 0, 0, 10);
            CREATE TABLE dbo.ModernForumPost
                (Id bigint PRIMARY KEY, BodyHtml nvarchar(max), Attachment varchar(200) NULL,
                 FileSize int NULL, AttachCount int);
            INSERT dbo.ModernForumPost VALUES
                (1, '[b]one[/b]', 'missing', 10, 2), (2, 'plain', 'keep', 20, 1),
                (9007199254740993, '[i]large ID[/i]', 'missing', 30, 1);
            CREATE TABLE dbo.ForumPostAttachments (Id uniqueidentifier PRIMARY KEY, PostId bigint);
            INSERT dbo.ForumPostAttachments VALUES
                ('11111111-1111-1111-1111-111111111111', 1),
                ('22222222-2222-2222-2222-222222222222', 2);
            CREATE TABLE dbo.NEWS_T (NEWS_ID int PRIMARY KEY, IMAGE_BLOB_KEY varchar(200) NULL);
            INSERT dbo.NEWS_T VALUES (1, 'missing'), (2, 'keep');
            CREATE TABLE dbo.EditorialArticles
                (Id uniqueidentifier PRIMARY KEY, ImageBlobKey varchar(200) NULL, LiveImageBlobKey varchar(200) NULL);
            INSERT dbo.EditorialArticles VALUES
                ('11111111-1111-1111-1111-111111111111', 'draft', 'live'),
                ('22222222-2222-2222-2222-222222222222', 'draft', 'live');
            """);
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Photo_queries_preserve_filters_order_and_limits()
    {
        Assert.Equal(new long[] { 1, 4 }, await ReadIdsAsync(command =>
            BackfillPhotoDimensionsCommand.ConfigureQuery(command, BackfillPhotoDimensionsOptions.Parse(["--connection-string", "Server=.;Database=test;"]))));
        Assert.Equal(new long[] { 1, 2, 4 }, await ReadIdsAsync(command =>
            BackfillPhotoDimensionsCommand.ConfigureQuery(command, BackfillPhotoDimensionsOptions.Parse(
                ["--connection-string", "Server=.;Database=test;", "--include-hidden"]))));
        Assert.Equal(new long[] { 1, 3 }, await ReadIdsAsync(command =>
            BackfillPhotoDimensionsCommand.ConfigureQuery(command, BackfillPhotoDimensionsOptions.Parse(["--connection-string", "Server=.;Database=test;", "--force", "--category-id", "9", "--pic-ids", "3,1,4", "--limit", "2"]))));
        Assert.Equal(new long[] { 1 }, await ReadIdsAsync(command =>
            BackfillPhotoDimensionsCommand.ConfigureQuery(command, BackfillPhotoDimensionsOptions.Parse(["--connection-string", "Server=.;Database=test;", "--limit", "1"]))));
        Assert.Equal(new long[] { 1, 3 }, await ReadIdsAsync(command =>
            GeneratePhotoThumbsCommand.ConfigureQuery(command, GeneratePhotoThumbsOptions.Parse(["--dry-run", "--connection-string", "Server=.;Database=test;", "--pic-ids", "3,1,1"]))));
        Assert.Empty(await ReadIdsAsync(command =>
            GeneratePhotoThumbsCommand.ConfigureQuery(command, GeneratePhotoThumbsOptions.Parse(["--dry-run", "--connection-string", "Server=.;Database=test;"]))));
    }

    [Fact]
    public async Task BbCode_query_preserves_marker_filter_and_bigint_limit()
    {
        Assert.Equal(new long[] { 1, 9007199254740993 }, await ReadIdsAsync(command =>
            ConvertLegacyBbCodeCommand.ConfigureQuery(command, ConvertLegacyBbCodeOptions.Parse(["--connection-string", "Server=.;Database=test;"]))));
        Assert.Equal(new long[] { 1 }, await ReadIdsAsync(command =>
            ConvertLegacyBbCodeCommand.ConfigureQuery(command, ConvertLegacyBbCodeOptions.Parse(["--connection-string", "Server=.;Database=test;", "--limit", "1"]))));
    }

    [Fact]
    public async Task Snapshot_photo_selection_keeps_all_batches_in_the_source_session()
    {
        await using var source = new SqlConnection(ConnectionString);
        await using var target = new SqlConnection(ConnectionString);
        await source.OpenAsync();
        await target.OpenAsync();
        await using var session = new SqlSnapshotCopySession(new DevSnapshotConfig(), source, target);
        await using (var create = source.CreateCommand())
        {
            create.CommandText = "CREATE TABLE #SelectedPhoto (Id int NOT NULL PRIMARY KEY);";
            await create.ExecuteNonQueryAsync();
        }

        await session.SetSelectedPhotosAsync([]);
        await session.SetSelectedPhotosAsync(Enumerable.Range(1, 501).ToArray());
        Assert.Equal(501, await SqlSnapshotService.ScalarAsync<int>(source, "SELECT COUNT(*) FROM #SelectedPhoto;"));
        Assert.Equal(125751, await SqlSnapshotService.ScalarAsync<int>(source, "SELECT SUM(Id) FROM #SelectedPhoto;"));
    }

    [Fact]
    public async Task Snapshot_cleanup_only_changes_selected_references_and_recounts_attachments()
    {
        await using var source = new SqlConnection(ConnectionString);
        await using var target = new SqlConnection(ConnectionString);
        await source.OpenAsync();
        await target.OpenAsync();
        await using var session = new SqlSnapshotCopySession(new DevSnapshotConfig(), source, target);
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await session.RemoveMissingForumBlobReferencesAsync(
            [new(1, null), new(9007199254740993, null), new(null, first), new(null, first)]);
        await session.RemoveMissingEditorialBlobReferencesAsync(
            [new(1, null, false), new(null, first, false), new(null, second, true)]);
        await session.RemoveMissingForumBlobReferencesAsync([]);
        await session.RemoveMissingEditorialBlobReferencesAsync([]);

        Assert.Equal(2, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.ModernForumPost WHERE Attachment IS NULL AND FileSize IS NULL AND AttachCount=0;"));
        Assert.Equal(1, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.ModernForumPost WHERE Id=2 AND Attachment='keep' AND FileSize=20 AND AttachCount=1;"));
        Assert.Equal(1, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.ForumPostAttachments WHERE PostId=2;"));
        Assert.Equal(1, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.NEWS_T WHERE NEWS_ID=1 AND IMAGE_BLOB_KEY IS NULL;"));
        Assert.Equal(1, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.NEWS_T WHERE NEWS_ID=2 AND IMAGE_BLOB_KEY='keep';"));
        Assert.Equal(1, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.EditorialArticles WHERE ImageBlobKey IS NULL AND LiveImageBlobKey='live';"));
        Assert.Equal(1, await SqlSnapshotService.ScalarAsync<int>(target,
            "SELECT COUNT(*) FROM dbo.EditorialArticles WHERE ImageBlobKey='draft' AND LiveImageBlobKey IS NULL;"));
    }

    private async Task<List<long>> ReadIdsAsync(Action<SqlCommand> configure)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        configure(command);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<long>();
        while (await reader.ReadAsync()) ids.Add(Convert.ToInt64(reader.GetValue(0)));
        return ids;
    }

    private sealed class ScratchContext(DbContextOptions<ScratchContext> options) : DbContext(options);
}
