using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfLinksRepository : ILinksRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly string publicLinksSql;
    private readonly string validationLinksSql;

    public EfLinksRepository(QueenZoneDbContext dbContext)
        : this(dbContext, PublicLinksSql, ValidationLinksSql)
    {
    }

    internal EfLinksRepository(QueenZoneDbContext dbContext, string publicLinksSql, string validationLinksSql)
    {
        this.dbContext = dbContext;
        this.publicLinksSql = publicLinksSql;
        this.validationLinksSql = validationLinksSql;
    }

    internal static string PublicLinksSql => """
            IF OBJECT_ID(N'dbo.QueenLinkChecks', N'U') IS NULL
            BEGIN
                SELECT
                    CAST(c.Q_LINK_CAT_ID AS int) AS CategoryId,
                    c.CAT_NAME AS CategoryName,
                    CAST(s.QUEEN_FEATURED_SITE_ID AS int) AS Id,
                    s.QUEEN_FEATURED_SITE_TITLE AS Title,
                    s.QUEEN_FEATURED_SITE_URL AS Url,
                    s.SITE_COMMENT AS Comment,
                    CAST(ISNULL(s.FEATURED_SITE, 0) AS int) AS FeaturedSite
                FROM dbo.Q_LINK_CAT_T AS c
                INNER JOIN dbo.QUEEN_FEATURED_SITE_T AS s
                    ON s.Q_LINK_CAT_ID = c.Q_LINK_CAT_ID
                WHERE
                    ISNULL(CAST(s.DISPLAY AS int), 0) <> 0
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_TITLE)), '') IS NOT NULL
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_URL)), '') IS NOT NULL
                ORDER BY c.CAT_NAME ASC, s.FEATURED_SITE DESC, s.QUEEN_FEATURED_SITE_TITLE ASC
            END
            ELSE
            BEGIN
                SELECT
                    CAST(c.Q_LINK_CAT_ID AS int) AS CategoryId,
                    c.CAT_NAME AS CategoryName,
                    CAST(s.QUEEN_FEATURED_SITE_ID AS int) AS Id,
                    s.QUEEN_FEATURED_SITE_TITLE AS Title,
                    s.QUEEN_FEATURED_SITE_URL AS Url,
                    s.SITE_COMMENT AS Comment,
                    CAST(ISNULL(s.FEATURED_SITE, 0) AS int) AS FeaturedSite
                FROM dbo.Q_LINK_CAT_T AS c
                INNER JOIN dbo.QUEEN_FEATURED_SITE_T AS s
                    ON s.Q_LINK_CAT_ID = c.Q_LINK_CAT_ID
                LEFT JOIN dbo.QueenLinkChecks AS checks
                    ON checks.QueenFeaturedSiteId = s.QUEEN_FEATURED_SITE_ID
                WHERE
                    ISNULL(CAST(s.DISPLAY AS int), 0) <> 0
                    AND ISNULL(checks.IsConfirmedDead, 0) = 0
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_TITLE)), '') IS NOT NULL
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_URL)), '') IS NOT NULL
                ORDER BY c.CAT_NAME ASC, s.FEATURED_SITE DESC, s.QUEEN_FEATURED_SITE_TITLE ASC
            END
            """;

    internal static string ValidationLinksSql => """
            IF OBJECT_ID(N'dbo.QueenLinkChecks', N'U') IS NULL
            BEGIN
                SELECT
                    CAST(s.QUEEN_FEATURED_SITE_ID AS int) AS Id,
                    s.QUEEN_FEATURED_SITE_TITLE AS Title,
                    s.QUEEN_FEATURED_SITE_URL AS Url,
                    s.SITE_COMMENT AS Comment,
                    CAST(s.Q_LINK_CAT_ID AS int) AS CategoryId,
                    CAST(ISNULL(s.FEATURED_SITE, 0) AS int) AS FeaturedSite,
                    0 AS ConsecutiveFailureCount,
                    CAST(0 AS bit) AS IsConfirmedDead
                FROM dbo.QUEEN_FEATURED_SITE_T AS s
                WHERE
                    ISNULL(CAST(s.DISPLAY AS int), 0) <> 0
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_TITLE)), '') IS NOT NULL
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_URL)), '') IS NOT NULL
                ORDER BY s.QUEEN_FEATURED_SITE_ID ASC
            END
            ELSE
            BEGIN
                SELECT
                    CAST(s.QUEEN_FEATURED_SITE_ID AS int) AS Id,
                    s.QUEEN_FEATURED_SITE_TITLE AS Title,
                    s.QUEEN_FEATURED_SITE_URL AS Url,
                    s.SITE_COMMENT AS Comment,
                    CAST(s.Q_LINK_CAT_ID AS int) AS CategoryId,
                    CAST(ISNULL(s.FEATURED_SITE, 0) AS int) AS FeaturedSite,
                    ISNULL(checks.ConsecutiveFailureCount, 0) AS ConsecutiveFailureCount,
                    ISNULL(checks.IsConfirmedDead, 0) AS IsConfirmedDead
                FROM dbo.QUEEN_FEATURED_SITE_T AS s
                LEFT JOIN dbo.QueenLinkChecks AS checks
                    ON checks.QueenFeaturedSiteId = s.QUEEN_FEATURED_SITE_ID
                WHERE
                    ISNULL(CAST(s.DISPLAY AS int), 0) <> 0
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_TITLE)), '') IS NOT NULL
                    AND NULLIF(LTRIM(RTRIM(s.QUEEN_FEATURED_SITE_URL)), '') IS NOT NULL
                ORDER BY s.QUEEN_FEATURED_SITE_ID ASC
            END
            """;

    public async Task<IReadOnlyList<QueenLinkCategory>> GetCategoriesWithLinksAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<LinkRow>(publicLinksSql)
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.CategoryName)
                && !string.IsNullOrWhiteSpace(row.Title)
                && !string.IsNullOrWhiteSpace(row.Url))
            .Select(row => new
            {
                row.CategoryId,
                row.CategoryName,
                Link = new QueenLink(
                    row.Id,
                    row.Title.Trim(),
                    row.Url.Trim(),
                    string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment.Trim(),
                    row.CategoryId,
                    row.FeaturedSite != 0).ToPublicLink()
            })
            .Where(row => row.Link is not null)
            .GroupBy(row => new { row.CategoryId, row.CategoryName })
            .Select(group => new QueenLinkCategory(
                group.Key.CategoryId,
                group.Key.CategoryName.Trim(),
                group.Select(row => row.Link!).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<QueenLinkValidationItem>> GetLinksForValidationAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<ValidationLinkRow>(validationLinksSql)
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Title)
                && !string.IsNullOrWhiteSpace(row.Url))
            .Select(row => new QueenLinkValidationItem(
                new QueenLink(
                    row.Id,
                    row.Title.Trim(),
                    row.Url.Trim(),
                    string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment.Trim(),
                    row.CategoryId,
                    row.FeaturedSite != 0),
                row.ConsecutiveFailureCount,
                row.IsConfirmedDead))
            .ToList();
    }

    public async Task UpsertCheckResultsAsync(
        IReadOnlyList<QueenLinkCheckUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return;
        }

        // Batch load existing rows once (avoid N FindAsync round-trips for tool bulk checks).
        var ids = updates.Select(update => update.QueenFeaturedSiteId).Distinct().ToList();
        var existing = await dbContext.QueenLinkChecks
            .Where(entity => ids.Contains(entity.QueenFeaturedSiteId))
            .ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(entity => entity.QueenFeaturedSiteId);

        foreach (var update in updates)
        {
            if (!byId.TryGetValue(update.QueenFeaturedSiteId, out var entity))
            {
                entity = new QueenLinkCheckEntity
                {
                    QueenFeaturedSiteId = update.QueenFeaturedSiteId,
                };
                dbContext.QueenLinkChecks.Add(entity);
                byId[update.QueenFeaturedSiteId] = entity;
            }

            entity.Url = update.Url;
            entity.LastCheckedAtUtc = update.CheckedAtUtc;
            entity.IsAvailable = update.IsAvailable;
            entity.IsConfirmedDead = update.IsConfirmedDead;
            entity.ConsecutiveFailureCount = update.ConsecutiveFailureCount;
            entity.LastStatusCode = update.LastStatusCode;
            entity.LastError = string.IsNullOrWhiteSpace(update.LastError) ? null : update.LastError;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal sealed class LinkRow
    {
        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public string? Comment { get; set; }

        public int FeaturedSite { get; set; }
    }

    internal sealed class ValidationLinkRow
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public string? Comment { get; set; }

        public int CategoryId { get; set; }

        public int FeaturedSite { get; set; }

        public int ConsecutiveFailureCount { get; set; }

        public bool IsConfirmedDead { get; set; }
    }
}
