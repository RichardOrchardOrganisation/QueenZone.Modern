using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfMemberLookupRepository"/> against the real legacy
/// <c>USERS_T</c> table (#1672 / #1890). Skips when
/// <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>MemberLookupRepositorySqlServerTests</c>.
/// Selects only <c>USER_ID</c> / <c>EMAIL</c> / repository <c>USERNAME</c> — never
/// <c>PASSWORD</c> or other credential columns.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfMemberLookupRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_member_lookup_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfMemberLookupRepository(dbContext);

        // char(40) USERNAME must trim; int USER_ID must materialize.
        var userIds = await dbContext.Database
            .SqlQueryRaw<int>("SELECT TOP 1 USER_ID AS [Value] FROM dbo.USERS_T ORDER BY USER_ID")
            .ToListAsync();
        Assert.NotEmpty(userIds);

        var userId = userIds[0];
        var byId = await repository.FindByUserIdAsync(userId);
        Assert.NotNull(byId);
        Assert.Equal(userId, byId.UserId);
        Assert.NotNull(byId.Username);
        Assert.Null(await repository.FindByUserIdAsync(int.MaxValue));

        var emailUserIds = await dbContext.Database
            .SqlQueryRaw<int>(
                """
                SELECT TOP 1 USER_ID AS [Value]
                FROM dbo.USERS_T
                WHERE EMAIL IS NOT NULL AND LEN(LTRIM(RTRIM(EMAIL))) > 0
                ORDER BY USER_ID
                """)
            .ToListAsync();
        if (emailUserIds.Count == 0)
        {
            return;
        }

        var emailUserId = emailUserIds[0];
        var emails = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT EMAIL AS [Value] FROM dbo.USERS_T WHERE USER_ID = {0}",
                emailUserId)
            .ToListAsync();
        Assert.NotEmpty(emails);

        var email = emails[0];
        var byEmail = await repository.FindByEmailAsync(email);
        Assert.NotNull(byEmail);
        Assert.Equal(emailUserId, byEmail.UserId);

        var all = await repository.FindAllByEmailAsync(email);
        Assert.Contains(all, match => match.UserId == emailUserId);
    }
}
