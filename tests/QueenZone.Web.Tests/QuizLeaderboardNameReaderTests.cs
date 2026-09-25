using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class QuizLeaderboardNameReaderTests
{
    [Fact]
    public async Task Ef_reader_loads_top_and_viewer_names_in_one_query()
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
        var top = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        db.MemberAccounts.AddRange(
            new MemberAccount { Id = top, Email = "top@example.test", NormalizedEmail = "TOP@EXAMPLE.TEST", DisplayName = "Top" },
            new MemberAccount { Id = viewer, Email = "viewer@example.test", NormalizedEmail = "VIEWER@EXAMPLE.TEST", DisplayName = "Viewer" });
        await db.SaveChangesAsync();
        counter.Reads = 0;

        var names = await QuizLeaderboardNameReader.LoadAsync(
            new EfMemberAccountRepository(db), [top, top], viewer, CancellationToken.None);

        Assert.Equal(1, counter.Reads);
        Assert.Equal("Top", QuizLeaderboardNameReader.DisplayName(names, top));
        Assert.Equal("Viewer", QuizLeaderboardNameReader.DisplayName(names, viewer));
        Assert.Equal("Member", QuizLeaderboardNameReader.DisplayName(names, Guid.NewGuid()));
        Assert.Equal(2, names.Count);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
