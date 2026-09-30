using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class LiveProbeSchemaTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;

    public LiveProbeSchemaTests()
    {
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        dbContext.Database.EnsureCreated();
    }

    [Fact]
    public void SqlServerTableExistsSql_UsesObjectId()
    {
        Assert.Contains("OBJECT_ID(@table, N'U')", LiveProbeSchema.SqlServerTableExistsSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TableExistsAsync_IsTrueWhenForumPostReportsIsPresent()
    {
        Assert.True(await LiveProbeSchema.TableExistsAsync(dbContext, LiveProbeSchema.ForumPostReportsTable));
    }

    [Fact]
    public async Task TableExistsAsync_IsFalseWhenForumPostReportsIsMissing()
    {
        await dbContext.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "ForumPostReportAuditLog";""");
        await dbContext.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "ForumPostReports";""");

        Assert.False(await LiveProbeSchema.TableExistsAsync(dbContext, LiveProbeSchema.ForumPostReportsTable));
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
