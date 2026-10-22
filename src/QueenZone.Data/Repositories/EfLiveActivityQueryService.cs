using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Queries <see cref="QueenZoneDbContext.ModernForumPosts"/> directly rather than adding a
/// method to <see cref="IForumRepository"/>, since that interface has three implementations
/// (modern, legacy, in-memory test double) and this one count does not need the repository
/// abstraction.
/// </summary>
public sealed class EfLiveActivityQueryService(QueenZoneDbContext dbContext, TimeProvider timeProvider)
    : ILiveActivityQueryService
{
    public async Task<int> GetNewForumRepliesTodayAsync(CancellationToken cancellationToken = default)
    {
        var todayStartUtc = timeProvider.GetUtcNow().UtcDateTime.Date;
        if (dbContext.Database.IsSqlServer())
        {
            // CONVERT keeps the comparison in the column's datetime2(0) type so PostedAt can be
            // a seek range regardless of client parameter scale. RECOMPILE avoids reusing
            // a plan sniffed for an old boundary, which scanned in the #2372 SQL Server test.
            var parameter = new SqlParameter("@todayStartUtc", SqlDbType.DateTime2)
            {
                Value = todayStartUtc,
            };
            var counts = await dbContext.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM dbo.ModernForumPost
                WHERE PostedAt IS NOT NULL AND PostedAt >= CONVERT(datetime2(0), @todayStartUtc) AND IsHidden = 0
                OPTION (RECOMPILE)
                """, parameter).ToListAsync(cancellationToken);
            return counts.Single();
        }

        return await dbContext.ModernForumPosts
            .Where(post => post.PostedAt != null && post.PostedAt >= todayStartUtc && !post.IsHidden)
            .CountAsync(cancellationToken);
    }
}
