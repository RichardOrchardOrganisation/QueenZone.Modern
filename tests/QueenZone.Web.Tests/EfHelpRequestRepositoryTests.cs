using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfHelpRequestRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfHelpRequestRepository repository;

    public EfHelpRequestRepositoryTests()
    {
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        dbContext.Database.EnsureCreated();
        repository = new EfHelpRequestRepository(dbContext);
    }

    [Fact]
    public async Task Create_update_and_get_persist_normalized_values()
    {
        var created = await repository.CreateAsync(Request("  Account access  ", " fan@example.com "));

        Assert.Equal("Account access", created.Subject);
        Assert.Equal("FAN@EXAMPLE.COM", created.NormalizedEmail);
        Assert.Equal(HelpRequestStatus.Open, created.Status);
        Assert.Equal(created, await repository.GetByIdAsync(created.Id));

        var updated = await repository.UpdateStatusAsync(created.Id, HelpRequestStatus.Resolved,
            " admin@example.com ", "  Fixed  ");
        Assert.NotNull(updated);
        Assert.Equal(HelpRequestStatus.Resolved, updated.Status);
        Assert.Equal("admin@example.com", updated.ReviewerEmail);
        Assert.Equal("Fixed", updated.ReviewNotes);
        Assert.NotNull(updated.ReviewedAt);
        Assert.Equal(updated, await repository.GetByIdAsync(created.Id));
        Assert.Null(await repository.UpdateStatusAsync(Guid.NewGuid(), HelpRequestStatus.Resolved, null, null));
    }

    [Fact]
    public async Task List_filters_orders_and_pages_with_sqlite_date_time_offsets()
    {
        var time = DateTimeOffset.Parse("2026-08-17T10:00:00Z");
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await repository.CreateAsync(Request("Older", submittedAt: time.AddDays(-1)));
        await repository.CreateAsync(Request("Tie second", id: secondId, submittedAt: time));
        await repository.CreateAsync(Request("Tie first", id: firstId, submittedAt: time));

        var firstPage = await repository.ListAsync("all", 1, 1);
        var secondPage = await repository.ListAsync(null, 2, 1);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(firstId, Assert.Single(firstPage.Items).Id);
        Assert.Equal(secondId, Assert.Single(secondPage.Items).Id);

        await repository.UpdateStatusAsync(firstId, HelpRequestStatus.Resolved, null, null);
        var open = await repository.ListAsync(HelpRequestStatus.Open, 1, 10);
        Assert.Equal(2, open.TotalCount);
        Assert.DoesNotContain(open.Items, item => item.Id == firstId);
        Assert.Equal(2, await repository.CountOpenAsync());
    }

    [Fact]
    public async Task Count_helpers_filter_identity_and_time_window()
    {
        var memberId = Guid.NewGuid();
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = "member@example.com",
            NormalizedEmail = "MEMBER@EXAMPLE.COM",
            DisplayName = "Member",
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var since = DateTimeOffset.Parse("2026-08-16T12:00:00Z");
        await repository.CreateAsync(Request("Member", "fan@example.com", memberId: memberId,
            submittedAt: since));
        await repository.CreateAsync(Request("Guest", "fan@example.com", submittedAt: since.AddHours(1)));
        await repository.CreateAsync(Request("Old", "fan@example.com", submittedAt: since.AddDays(-1)));
        await repository.CreateAsync(Request("Other", "other@example.com", submittedAt: since));

        Assert.Equal(2, await repository.CountByEmailSinceAsync(" fan@example.com ", since));
        Assert.Equal(1, await repository.CountByMemberSinceAsync(memberId, since));
        Assert.Equal(0, await repository.CountByMemberSinceAsync(Guid.NewGuid(), since));
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    private static HelpRequest Request(string subject, string email = "guest@example.com",
        Guid? id = null, Guid? memberId = null, DateTimeOffset? submittedAt = null) =>
        new(id ?? Guid.NewGuid(), HelpRequestTopic.Account, subject,
            "This is a sufficiently long help message.", "Guest User", email, "", memberId,
            HelpRequestStatus.Open, submittedAt ?? DateTimeOffset.Parse("2026-08-17T10:00:00Z"),
            null, null, null);
}
