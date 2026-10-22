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
            // Type the parameter to the production datetime2(0) column so PostedAt is a seek range.
            // RECOMPILE because a plan sniffed for an old boundary scans (#2372).
            var parameter = new SqlParameter("@todayStartUtc", SqlDbType.DateTime2)
            {
                Precision = 0,
                Scale = 0,
                Value = todayStartUtc,
            };
            var counts = await dbContext.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM dbo.ModernForumPost
                WHERE PostedAt IS NOT NULL AND PostedAt >= @todayStartUtc AND IsHidden = 0
                OPTION (RECOMPILE)
                """, parameter).ToListAsync(cancellationToken);
            return counts.Single();
        }

        return await dbContext.ModernForumPosts
            .Where(post => post.PostedAt != null && post.PostedAt >= todayStartUtc && !post.IsHidden)
            .CountAsync(cancellationToken);
    }
}
