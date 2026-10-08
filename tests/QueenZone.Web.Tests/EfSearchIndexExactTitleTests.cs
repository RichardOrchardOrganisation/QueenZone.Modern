using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfSearchIndexExactTitleTests
{
    [Fact]
    public async Task FindByExactTitleAsync_issues_one_sargable_read_without_body()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        await using var db = new QueenZoneDbContext(options);
        db.Database.EnsureCreated();
        var service = new EfSearchIndexService(db, new SearchIndexRevision());
        await service.UpsertAsync(new SearchDocumentEntity
        {
            SourceKey = "news:rhapsody",
            ContentType = SiteSearchContentType.News,
            Title = "Bohemian Rhapsody",
            Body = "This body must never be selected for related-content lookup.",
            Summary = "News",
            Url = "/news/1/bohemian-rhapsody",
        });
        counter.Reads = 0;
        counter.Commands.Clear();

        var matches = await service.FindByExactTitleAsync("Bohemian Rhapsody");

        Assert.Equal(1, counter.Reads);
        var sql = Assert.Single(counter.Commands);
        Assert.DoesNotContain("LOWER(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Body", sql, StringComparison.Ordinal);
        var match = Assert.Single(matches);
        Assert.Equal("Bohemian Rhapsody", match.Title);
        Assert.Equal("/news/1/bohemian-rhapsody", match.Url);
        Assert.Equal(SiteSearchContentType.News, match.ContentType);
    }

    [Fact]
    public async Task FindByExactTitleAsync_blank_title_does_not_query()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        await using var db = new QueenZoneDbContext(options);
        db.Database.EnsureCreated();
        var service = new EfSearchIndexService(db, new SearchIndexRevision());
        counter.Reads = 0;
        counter.Commands.Clear();

        var matches = await service.FindByExactTitleAsync("  ");

        Assert.Empty(matches);
        Assert.Equal(0, counter.Reads);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }

        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
