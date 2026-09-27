using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PhotoListFilterRepositoryTests
{
    [Fact]
    public async Task InMemory_GetCategoryPage_FiltersByDesktopPreset()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var brian = (await repository.GetCategoriesAsync()).Single(c => c.Slug == "brian-may");

        var page = await repository.GetCategoryPageAsync(
            brian.CatId,
            1,
            24,
            new PhotoListFilter(PhotoSizePreset.Desktop));

        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(101, page.Items[0].PicId);
    }

    [Fact]
    public async Task InMemory_DetailNavigation_UsesFilteredNeighbors()
    {
        var repository = new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var queen = (await repository.GetCategoriesAsync()).Single(c => c.Slug == "queen");
        var filter = new PhotoListFilter(PhotoSizePreset.Desktop);

        // Queen seed: 201=2560x1440 desktop; 204=1200x800 no; 203=800x600 no; 202=1080x1920 phone.
        var nav = await repository.GetDetailNavigationAsync(queen.CatId, 201, filter);
        Assert.NotNull(nav);
        Assert.Equal(0, nav.Index);
        Assert.Equal(1, nav.Count);
        Assert.Null(nav.PreviousPicId);
        Assert.Null(nav.NextPicId);

        var excluded = await repository.GetDetailNavigationAsync(queen.CatId, 202, filter);
        Assert.NotNull(excluded);
        Assert.False(excluded.MatchedRequestedFilter);
        Assert.Equal(202, excluded.Photo.PicId);
        Assert.Equal(0, excluded.Index);
        Assert.Equal(4, excluded.Count);
        Assert.Null(excluded.PreviousPicId);
        Assert.Equal(201, excluded.NextPicId);
    }

    [Fact]
    public async Task EfSqlite_GetCategoryPage_FiltersByLandscape()
    {
        await using var fixture = await SqlitePhotoFixture.CreateAsync();
        var repository = new EfPhotoRepository(fixture.DbContext, PhotoSqlQueries.CreateSqliteFixture());

        var page = await repository.GetCategoryPageAsync(3, 1, 10, new PhotoListFilter(PhotoSizePreset.Landscape));
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, item => Assert.True(item.PictureWidth > item.PictureHeight));

        var desktop = await repository.GetCategoryPageAsync(3, 1, 10, new PhotoListFilter(PhotoSizePreset.Desktop));
        Assert.Equal(1, desktop.TotalCount);
        Assert.Equal(11, desktop.Items[0].PicId);

        var nav = await repository.GetDetailNavigationAsync(3, 11, new PhotoListFilter(PhotoSizePreset.Desktop));
        Assert.NotNull(nav);
        Assert.Equal(0, nav.Index);
        Assert.Equal(1, nav.Count);
    }

    [Fact]
    public async Task EfSqlite_FilterMiss_RunsOneNavigationQueryAndKeepsUnfilteredNeighbors()
    {
        var commands = new List<string>();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new RecordingReaderInterceptor(commands))
            .Options;
        await using var db = new QueenZoneDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE PhotoCategories (cat_id INTEGER NOT NULL, name TEXT NOT NULL);
            CREATE TABLE PhotoItems (
                NAME TEXT NOT NULL,
                DATE_TIME TEXT NOT NULL,
                URL TEXT NOT NULL,
                THUMB_URL TEXT NOT NULL,
                T_HEIGHT INTEGER NOT NULL,
                T_WIDTH INTEGER NOT NULL,
                PIC_WIDTH INTEGER NOT NULL,
                PIC_HEIGHT INTEGER NOT NULL,
                pic_id INTEGER NOT NULL,
                category_name TEXT,
                cat_id INTEGER NOT NULL,
                submitted_by_display_name TEXT
            );
            INSERT INTO PhotoCategories (cat_id, name) VALUES (7, 'Sizes');
            INSERT INTO PhotoItems (NAME, DATE_TIME, URL, THUMB_URL, T_HEIGHT, T_WIDTH, PIC_WIDTH, PIC_HEIGHT, pic_id, category_name, cat_id, submitted_by_display_name)
            VALUES
                ('Small', '2020-01-03', 's.jpg', 's-t.jpg', 40, 60, 640, 480, 3, 'Sizes', 7, NULL),
                ('Desktop', '2020-01-02', 'd.jpg', 'd-t.jpg', 100, 150, 1920, 1080, 2, 'Sizes', 7, NULL),
                ('Phone', '2020-01-01', 'p.jpg', 'p-t.jpg', 150, 100, 1080, 1920, 1, 'Sizes', 7, NULL);
            """);

        var repository = new EfPhotoRepository(db, PhotoSqlQueries.CreateSqliteFixture());
        var desktop = new PhotoListFilter(PhotoSizePreset.Desktop);

        commands.Clear();
        var matched = await repository.GetDetailNavigationAsync(7, 2, desktop);
        Assert.NotNull(matched);
        Assert.True(matched.MatchedRequestedFilter);
        Assert.Equal(0, matched.Index);
        Assert.Equal(1, matched.Count);
        Assert.Null(matched.PreviousPicId);
        Assert.Null(matched.NextPicId);
        Assert.Equal(1, commands.Count(sql => sql.Contains("AS TotalCount", StringComparison.Ordinal)));
        Assert.Contains(commands, sql => sql.Contains("1920", StringComparison.Ordinal));

        commands.Clear();
        var missed = await repository.GetDetailNavigationAsync(7, 3, desktop);
        Assert.NotNull(missed);
        Assert.False(missed.MatchedRequestedFilter);
        Assert.Equal(3, missed.Photo.PicId);
        Assert.Equal(0, missed.Index);
        Assert.Equal(3, missed.Count);
        Assert.Null(missed.PreviousPicId);
        Assert.Equal(2, missed.NextPicId);
        var navigationSql = commands.Where(sql => sql.Contains("AS TotalCount", StringComparison.Ordinal)).ToList();
        Assert.Single(navigationSql);
        Assert.DoesNotContain("1920", navigationSql[0], StringComparison.Ordinal);

        commands.Clear();
        Assert.Null(await repository.GetDetailNavigationAsync(7, 999, desktop));
        Assert.DoesNotContain(commands, sql => sql.Contains("AS TotalCount", StringComparison.Ordinal));

        var ids = await repository.PickRandomPublishedPhotoIdsAsync(7, 3);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Contains(id, new[] { 1, 2, 3 }));
        var loaded = await repository.GetPublishedByIdsAsync(7, ids);
        Assert.Equal(ids, loaded.Select(item => item.PicId));
    }

    private sealed class RecordingReaderInterceptor(List<string> commands) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

/// <summary>Minimal SQLite photo fixture for filter SQL (shares schema with EfPublicReadRepositoryTests).</summary>
internal sealed class SqlitePhotoFixture : IAsyncDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection connection;

    private SqlitePhotoFixture(Microsoft.Data.Sqlite.SqliteConnection connection, QueenZoneDbContext dbContext)
    {
        this.connection = connection;
        DbContext = dbContext;
    }

    public QueenZoneDbContext DbContext { get; }

    public static async Task<SqlitePhotoFixture> CreateAsync()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new QueenZoneDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE PhotoCategories (cat_id INTEGER NOT NULL, name TEXT NOT NULL);
            CREATE TABLE PhotoItems (
                NAME TEXT NOT NULL,
                DATE_TIME TEXT NOT NULL,
                URL TEXT NOT NULL,
                THUMB_URL TEXT NOT NULL,
                T_HEIGHT INTEGER NOT NULL,
                T_WIDTH INTEGER NOT NULL,
                PIC_WIDTH INTEGER NOT NULL,
                PIC_HEIGHT INTEGER NOT NULL,
                pic_id INTEGER NOT NULL,
                category_name TEXT,
                cat_id INTEGER NOT NULL,
                submitted_by_display_name TEXT
            );
            INSERT INTO PhotoCategories (cat_id, name) VALUES (3, 'Live 1986');
            INSERT INTO PhotoItems (NAME, DATE_TIME, URL, THUMB_URL, T_HEIGHT, T_WIDTH, PIC_WIDTH, PIC_HEIGHT, pic_id, category_name, cat_id, submitted_by_display_name)
            VALUES
                ('Newest', '1986-07-12 00:00:00', 'n.jpg', 'n-t.jpg', 100, 150, 1920, 1080, 11, 'Live 1986', 3, 'LiveAidFan'),
                ('Middle', '1986-07-11 00:00:00', 'm.jpg', 'm-t.jpg', 100, 150, 800, 600, 10, 'Live 1986', 3, NULL),
                ('Oldest', '1986-07-10 00:00:00', 'o.jpg', 'o-t.jpg', 100, 150, 0, 0, 9, 'Live 1986', 3, NULL);
            """);
        return new SqlitePhotoFixture(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
