using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfBiographyRepository"/> (legacy <c>Q_BIO_LIST_SP</c> /
/// <c>Q_BIO_DISPLAY_SP</c> reads and <c>Q_BIO_T</c> writes) against a scratch SQL Server database
/// (#1672). The table and procedures below were copied from the <c>queenzone_legacy_sync</c> mirror:
/// <c>smallint</c> ids, a <c>tinyint</c> display sequence and an <c>ntext</c> body. The read-only
/// mirror probe is <c>EfBiographyRepositoryLegacyProbeTests</c> in <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class BiographyRepositorySqlServerTests : IAsyncLifetime
{
    private static readonly string[] LegacySchemaBatches =
    [
        """
        CREATE TABLE dbo.Q_BIO_T
        (
            Q_BIO_ID smallint IDENTITY(1,1) NOT NULL PRIMARY KEY,
            TITLE varchar(50) NULL,
            SUMMARY varchar(400) NULL,
            BIO_TEXT ntext NULL,
            DISPLAY_SEQUENCE tinyint NULL,
            CREATE_DATE smalldatetime NOT NULL DEFAULT (getdate())
        );
        """,
        """
        CREATE PROCEDURE [dbo].[Q_BIO_LIST_SP]
        AS
        SELECT 'Queen Biography for ' + TITLE as BIOTITLE, Q_BIO_ID, CREATE_DATE, TITLE,  DISPLAY_SEQUENCE, SUBSTRING(BIO_TEXT, 1, 200) + '... ' as SUMMARY
        FROM
        Q_BIO_T
        ORDER BY DISPLAY_SEQUENCE ASC
        """,
        """
        CREATE PROCEDURE Q_BIO_DISPLAY_SP
        @Q_BIO_ID SMALLINT
        AS
        SELECT TITLE, SUMMARY, BIO_TEXT, DISPLAY_SEQUENCE
        FROM
        Q_BIO_T
        WHERE Q_BIO_ID = @Q_BIO_ID
        """,
    ];

    private readonly string databaseName = $"QueenZoneBiographyTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfBiographyRepository repository = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            foreach (var batch in LegacySchemaBatches)
            {
                await schema.Database.ExecuteSqlRawAsync(batch);
            }
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfBiographyRepository(dbContext);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Stored_procedure_reads_order_chapters_and_materialize_legacy_types()
    {
        var body = "The early years. " + new string('x', 400);
        var later = await repository.CreateAsync(new AdminBiographyDraft("  The 1980s  ", "", "Arena rock.", 2));
        var first = await repository.CreateAsync(new AdminBiographyDraft("Formation", "How it began", body, 1));
        var last = await repository.CreateAsync(new AdminBiographyDraft("Legacy", "Afterwards", "Tributes.", 3));

        var chapters = await repository.GetChaptersAsync();
        Assert.Equal([first, later, last], chapters.Select(chapter => chapter.Id));
        Assert.Equal("The 1980s", chapters[1].Title);
        Assert.Equal([1, 2, 3], chapters.Select(chapter => (int)chapter.DisplaySequence));
        Assert.All(chapters, chapter => Assert.NotEqual(default, chapter.CreatedAt));
        // Q_BIO_LIST_SP builds SUMMARY from the first 200 characters of the ntext body.
        Assert.StartsWith("The early years.", chapters[0].Summary, StringComparison.Ordinal);
        Assert.EndsWith("...", chapters[0].Summary, StringComparison.Ordinal);

        var detail = await repository.GetByIdAsync(first);
        Assert.NotNull(detail);
        Assert.Equal("Formation", detail.Title);
        Assert.Equal("How it began", detail.Summary);
        Assert.Equal(body, detail.Body);
        Assert.Equal(1, detail.DisplaySequence);
        Assert.Null(await repository.GetByIdAsync(404));

        // An empty SUMMARY column falls back to an excerpt of the body.
        var excerpt = await repository.GetByIdAsync(later);
        Assert.Equal("Arena rock.", excerpt!.Summary);

        var nav = await repository.GetAdjacentChaptersAsync(later);
        Assert.Equal(first, nav.Previous?.Id);
        Assert.Equal(last, nav.Next?.Id);
        Assert.Equal(new BiographyChapterNav(null, null), await repository.GetAdjacentChaptersAsync(404));
    }

    [Fact]
    public async Task Update_rewrites_chapter_and_rejects_missing_id()
    {
        var id = await repository.CreateAsync(new AdminBiographyDraft("Draft", "Old", "Old body", 5));

        await repository.UpdateAsync(id, new AdminBiographyDraft("Final", "New", "New body", 4));

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal(("Final", "New", "New body", (byte)4),
            (updated.Title, updated.Summary, updated.Body, updated.DisplaySequence));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpdateAsync(404, new AdminBiographyDraft("Missing", "", "", 1)));
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy objects come from LegacySchemaBatches.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
