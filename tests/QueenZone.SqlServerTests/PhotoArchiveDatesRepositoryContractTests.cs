using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Configurations;
using QueenZone.Data.Entities;

namespace QueenZone.SqlServerTests;

public sealed class PhotoArchiveDatesRepositoryContractTests : IAsyncLifetime
{
    private QueenZoneDbContext db = null!;
    private IPhotoArchiveDatesRepository Repository { get; set; } = null!;
    [Fact]
    public async Task Dates_compare_and_swap_preserves_first_write_and_accepts_fresh_expected()
    {
        var id = await SeedPhotoAsync();
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(id));
        var first = new PhotoArchiveDates(new PartialDate(1986, 7, 12), new PartialDate(1986, 7));
        await Repository.SetAsync(id, first, PhotoArchiveDates.Unknown);
        Assert.Equal(first, await Repository.GetAsync(id));
        var second = new PhotoArchiveDates(new PartialDate(1985), new PartialDate(1985, 7, 13));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.SetAsync(id, second, PhotoArchiveDates.Unknown));
        Assert.Equal(first, await Repository.GetAsync(id));
        await Repository.SetAsync(id, second, await Repository.GetAsync(id));
        Assert.Equal(second, await Repository.GetAsync(id));
        await Repository.SetAsync(id, PhotoArchiveDates.Unknown, second);
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(id));
    }

    [Fact]
    public async Task Missing_photo_reads_unknown_and_cannot_be_written()
    {
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(999));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SetAsync(999,
            new(new PartialDate(1986), null), PhotoArchiveDates.Unknown));
        Assert.Equal("Photo 999 was not found.", error.Message);
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(999));
    }

    [Fact]
    public async Task Two_contexts_compare_and_swap_rejects_stale_dates()
    {
        var id = await SeedPhotoAsync();
        await using var secondContext = CreateContext();
        IPhotoArchiveDatesRepository second = new EfPhotoArchiveDatesRepository(secondContext);
        var original = await second.GetAsync(id);
        Assert.Equal(PhotoArchiveDates.Unknown, original);
        var first = new PhotoArchiveDates(new PartialDate(1986, 7, 12), new PartialDate(1986, 7));
        await Repository.SetAsync(id, first, original);
        var next = new PhotoArchiveDates(new PartialDate(1985), null);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => second.SetAsync(id, next, original));
        Assert.Equal(first, await second.GetAsync(id));
        await second.SetAsync(id, next, await second.GetAsync(id));
        Assert.Equal(next, await Repository.GetAsync(id));
    }

    private Task<int> SeedPhotoAsync() => new EfAdminPhotoRepository(db).CreateAsync(
        new(9, "Wembley", null, 1986, new DateTime(1986, 7, 12), true, "photo.jpg", "thumb.jpg", 150, 100, 1920, 1080), "editor@example.test");

    private readonly string databaseName = $"QueenZonePhotoDatesContract_{Guid.NewGuid():N}";
    private string ConnectionString => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
        ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
    {
        InitialCatalog = databaseName,
    }.ConnectionString;

    private QueenZoneDbContext CreateContext() => new(new DbContextOptionsBuilder<QueenZoneDbContext>()
        .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var schema = new PhotoSchemaContext(new DbContextOptionsBuilder<PhotoSchemaContext>()
            .UseSqlServer(ConnectionString).Options);
        await schema.Database.EnsureCreatedAsync();
        await schema.Database.ExecuteSqlRawAsync(LegacyPhotoSchema.CreateTablesSql);
        await schema.Database.ExecuteSqlRawAsync("""
            ALTER TABLE dbo.PIC_FILES_T ADD
                PIC_EVENT_DATE date NULL, PIC_EVENT_PRECISION char(1) NULL,
                PIC_TAKEN_DATE date NULL, PIC_TAKEN_PRECISION char(1) NULL;
            """);
        db = CreateContext();
        Repository = new EfPhotoArchiveDatesRepository(db);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new PhotoSchemaContext(new DbContextOptionsBuilder<PhotoSchemaContext>()
            .UseSqlServer(ConnectionString).Options);
        await schema.Database.EnsureDeletedAsync();
        await db.DisposeAsync();
    }

    private sealed class PhotoSchemaContext(DbContextOptions<PhotoSchemaContext> options) : DbContext(options)
    {
        public DbSet<PhotoAdminAuditLogEntity> PhotoAdminAuditLogs => Set<PhotoAdminAuditLogEntity>();
        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.ApplyConfiguration(new PhotoAdminAuditLogEntityConfiguration());
    }
}
