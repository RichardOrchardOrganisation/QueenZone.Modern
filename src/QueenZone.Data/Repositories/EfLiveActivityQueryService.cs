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
            // Keep visibility literal for IX_ModernForumPost_PostedAt_Visible. Compile
            // for this boundary instead of reusing a plan for a much older date.
            // The SQL Server regression checks filtered-index eligibility in both
            // SIMPLE and FORCED parameterization modes (#2372).
            var counts = await dbContext.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value]
                FROM dbo.ModernForumPost
                WHERE PostedAt IS NOT NULL AND PostedAt >= {todayStartUtc} AND IsHidden = 0
                OPTION (RECOMPILE)
                """).ToListAsync(cancellationToken);
            // Materialize before Single: server-side composition would wrap the hint.
            return counts.Single();
        }

        return await dbContext.ModernForumPosts
            .Where(post => post.PostedAt != null && post.PostedAt >= todayStartUtc && !post.IsHidden)
            .CountAsync(cancellationToken);
    }
}
