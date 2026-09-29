using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Looks up a legacy USERS_T account by email or user id for modern member linking.
/// Read-only: the legacy PASSWORD column is never used.
/// </summary>
public sealed class EfMemberLookupRepository : ILegacyMemberLookupRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly Func<string, FormattableString> findByEmailSql;
    private readonly Func<int, FormattableString> findByUserIdSql;

    public EfMemberLookupRepository(QueenZoneDbContext dbContext)
        : this(
            dbContext,
            EfProductionSql.CreateMemberLookupSql(),
            EfProductionSql.CreateMemberLookupByUserIdSql())
    {
    }

    internal EfMemberLookupRepository(
        QueenZoneDbContext dbContext,
        Func<string, FormattableString> findByEmailSql,
        Func<int, FormattableString>? findByUserIdSql = null)
    {
        this.dbContext = dbContext;
        this.findByEmailSql = findByEmailSql;
        this.findByUserIdSql = findByUserIdSql
            ?? (userId => throw new InvalidOperationException("FindByUserId SQL was not configured."));
    }

    public async Task<IReadOnlyList<LegacyMemberMatch>> FindAllByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQuery<UserRow>(findByEmailSql(email))
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new LegacyMemberMatch(row.USER_ID, row.USERNAME?.Trim() ?? string.Empty))
            .ToList();
    }

    public async Task<LegacyMemberMatch?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var matches = await FindAllByEmailAsync(email, cancellationToken);
        return matches.FirstOrDefault();
    }

    public async Task<LegacyMemberMatch?> FindByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQuery<UserRow>(findByUserIdSql(userId))
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : new LegacyMemberMatch(row.USER_ID, row.USERNAME?.Trim() ?? string.Empty);
    }

    internal sealed class UserRow
    {
        public int USER_ID { get; set; }

        public string? USERNAME { get; set; }
    }
}
