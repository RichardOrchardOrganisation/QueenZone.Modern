using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Admin writes against legacy <c>PIC_FILES_T</c> / <c>PIC_CAT_T</c>, plus modern audit rows.
/// Covered by <c>AdminPhotoRepositorySqlServerTests</c> (CI SQL Server) and the read-only
/// <c>EfAdminPhotoRepositoryLegacyProbeTests</c> mirror probe (#1672).
/// </summary>
public sealed class EfAdminPhotoRepository(QueenZoneDbContext dbContext) : IAdminPhotoRepository
{
    public async Task<AdminPhotoPage> GetPageAsync(
        AdminPhotoListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (safePage - 1) * safePageSize;

        var where = new StringBuilder("WHERE 1 = 1");
        void AppendFilter(string sql) => where.Append(' ').Append(sql);

        if (filter.CatId is int)
        {
            AppendFilter("AND p.Cat_ID = @CatId");
        }

        if (filter.IsVisible is bool)
        {
            AppendFilter("AND p.DISPLAY = @Display");
        }

        if (filter.Year is int)
        {
            AppendFilter("AND p.PICTURE_YEAR = @Year");
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            AppendFilter("AND (p.Name LIKE @Search OR p.KEYWORDS LIKE @Search)");
        }

        var countSql = $"""
            SELECT COUNT(1)
            FROM dbo.PIC_FILES_T p
            {where}
            """;

        var total = await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            countSql,
            command => AddFilterParameters(command, filter),
            cancellationToken: cancellationToken);

        var listSql = $"""
            SELECT
                p.PIC_ID AS PicId,
                p.Cat_ID AS CatId,
                c.name AS CategoryName,
                p.Name AS Title,
                p.Url AS LegacyUrl,
                p.Thumb_URL AS LegacyThumbUrl,
                ISNULL(p.t_width, 0) AS ThumbWidth,
                ISNULL(p.t_height, 0) AS ThumbHeight,
                ISNULL(p.PIC_WIDTH, 0) AS PictureWidth,
                ISNULL(p.PIC_HEIGHT, 0) AS PictureHeight,
                ISNULL(p.PICTURE_YEAR, YEAR(p.Date_time)) AS Year,
                p.Date_time AS DateTime,
                p.KEYWORDS AS Keywords,
                CAST(CASE WHEN p.DISPLAY = 1 THEN 1 ELSE 0 END AS bit) AS IsVisible
            FROM dbo.PIC_FILES_T p
            INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
            {where}
            ORDER BY p.Date_time DESC, p.PIC_ID DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await EfSql.QuerySqlAsync<AdminPhotoRow>(
            dbContext,
            listSql,
            command =>
            {
                AddFilterParameters(command, filter);
                command.Parameters.Add(EfSql.Input("@Offset", offset));
                command.Parameters.Add(EfSql.Input("@PageSize", safePageSize));
            },
            cancellationToken: cancellationToken);

        IReadOnlyList<AdminPhotoItem> items = rows.Select(MapRow).ToList();
        return new AdminPhotoPage(items, total, safePage, safePageSize);
    }

    public async Task<AdminPhotoItem?> GetByIdAsync(int picId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                p.PIC_ID AS PicId,
                p.Cat_ID AS CatId,
                c.name AS CategoryName,
                p.Name AS Title,
                p.Url AS LegacyUrl,
                p.Thumb_URL AS LegacyThumbUrl,
                ISNULL(p.t_width, 0) AS ThumbWidth,
                ISNULL(p.t_height, 0) AS ThumbHeight,
                ISNULL(p.PIC_WIDTH, 0) AS PictureWidth,
                ISNULL(p.PIC_HEIGHT, 0) AS PictureHeight,
                ISNULL(p.PICTURE_YEAR, YEAR(p.Date_time)) AS Year,
                p.Date_time AS DateTime,
                p.KEYWORDS AS Keywords,
                CAST(CASE WHEN p.DISPLAY = 1 THEN 1 ELSE 0 END AS bit) AS IsVisible
            FROM dbo.PIC_FILES_T p
            INNER JOIN dbo.PIC_CAT_T c ON c.cat_id = p.Cat_ID
            WHERE p.PIC_ID = @PicId
            """;

        var rows = await EfSql.QuerySqlAsync<AdminPhotoRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@PicId", picId)),
            cancellationToken: cancellationToken);

        var row = rows.FirstOrDefault();
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<AdminPhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT cat_id AS CatId, name AS Name
            FROM dbo.PIC_CAT_T
            ORDER BY name
            """;

        var rows = await EfSql.QuerySqlAsync<CategoryRow>(dbContext, sql, cancellationToken: cancellationToken);
        IReadOnlyList<AdminPhotoCategory> categories = rows
            .Select(row => new AdminPhotoCategory(row.CatId, row.Name, NewsSlug.Slugify(row.Name)))
            .ToList();
        return categories;
    }

    public async Task<AdminPhotoCategory?> GetCategoryByIdAsync(int catId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT cat_id AS CatId, name AS Name
            FROM dbo.PIC_CAT_T
            WHERE cat_id = @CatId
            """;

        var rows = await EfSql.QuerySqlAsync<CategoryRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@CatId", catId)),
            cancellationToken: cancellationToken);

        var row = rows.FirstOrDefault();
        return row is null ? null : new AdminPhotoCategory(row.CatId, row.Name, NewsSlug.Slugify(row.Name));
    }

    public async Task<IReadOnlyList<string>> GetReferencedBlobNamesAsync(
        int catId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT p.Url AS LegacyUrl, p.Thumb_URL AS LegacyThumbUrl
            FROM dbo.PIC_FILES_T p
            WHERE p.Cat_ID = @CatId
            """;

        var rows = await EfSql.QuerySqlAsync<LegacyUrlRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@CatId", catId)),
            cancellationToken: cancellationToken);

        return rows
            .SelectMany(row => new[] { row.LegacyUrl, row.LegacyThumbUrl })
            .Select(BlobNameFromLegacyUrl)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? BlobNameFromLegacyUrl(string? legacyUrl) =>
        string.IsNullOrWhiteSpace(legacyUrl) ? null : legacyUrl.Split('/').Last();

    public async Task<int> CreateAsync(
        AdminPhotoCreateRequest request,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.PIC_FILES_T
            (
                Name, Cat_ID, Date_time, Url, Thumb_URL, t_height, t_width,
                DISPLAY, PIC_HEIGHT, PIC_WIDTH, KEYWORDS, PICTURE_YEAR
            )
            VALUES
            (
                @Name, @CatId, @DateTime, @Url, @ThumbUrl, @ThumbHeight, @ThumbWidth,
                @Display, @PictureHeight, @PictureWidth, @Keywords, @Year
            );
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        var picId = await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Name", request.Title.Trim()));
                command.Parameters.Add(EfSql.Input("@CatId", request.CatId));
                command.Parameters.Add(EfSql.Input("@DateTime", request.DateTime));
                command.Parameters.Add(EfSql.Input("@Url", request.LegacyUrl));
                command.Parameters.Add(EfSql.Input("@ThumbUrl", request.LegacyThumbUrl));
                command.Parameters.Add(EfSql.Input("@ThumbHeight", request.ThumbHeight));
                command.Parameters.Add(EfSql.Input("@ThumbWidth", request.ThumbWidth));
                command.Parameters.Add(EfSql.Input("@Display", request.IsVisible ? 1 : 0));
                command.Parameters.Add(EfSql.Input("@PictureHeight", request.PictureHeight));
                command.Parameters.Add(EfSql.Input("@PictureWidth", request.PictureWidth));
                command.Parameters.Add(EfSql.Input("@Keywords", string.IsNullOrWhiteSpace(request.Keywords) ? DBNull.Value : request.Keywords.Trim()));
                command.Parameters.Add(EfSql.Input("@Year", request.Year));
            },
            cancellationToken: cancellationToken);

        await AppendAuditAsync(picId, "create", editorEmail, $"Created \"{request.Title.Trim()}\"", cancellationToken);
        return picId;
    }

    public async Task UpdateAsync(
        int picId,
        AdminPhotoUpdateRequest request,
        string editorEmail,
        AdminPhotoConcurrencyToken? expected = null,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            UPDATE dbo.PIC_FILES_T
            SET Name = @Name,
                Cat_ID = @CatId,
                Date_time = @DateTime,
                KEYWORDS = @Keywords,
                PICTURE_YEAR = @Year
            WHERE PIC_ID = @PicId
            """;
        if (expected is not null)
        {
            sql += """
                 AND Name = @ExpectedName
                 AND Cat_ID = @ExpectedCatId
                 AND Date_time = @ExpectedDateTime
                 AND ISNULL(KEYWORDS, '') = @ExpectedKeywords
                 AND PICTURE_YEAR = @ExpectedYear
                 AND DISPLAY = @ExpectedDisplay
                """;
        }

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Name", request.Title.Trim()));
                command.Parameters.Add(EfSql.Input("@CatId", request.CatId));
                command.Parameters.Add(EfSql.Input("@DateTime", request.DateTime));
                command.Parameters.Add(EfSql.Input("@Keywords", string.IsNullOrWhiteSpace(request.Keywords) ? DBNull.Value : request.Keywords.Trim()));
                command.Parameters.Add(EfSql.Input("@Year", request.Year));
                command.Parameters.Add(EfSql.Input("@PicId", picId));
                if (expected is not null)
                {
                    command.Parameters.Add(EfSql.Input("@ExpectedName", expected.Title.Trim()));
                    command.Parameters.Add(EfSql.Input("@ExpectedCatId", expected.CatId));
                    command.Parameters.Add(EfSql.Input("@ExpectedDateTime", expected.DateTime));
                    command.Parameters.Add(EfSql.Input("@ExpectedKeywords", expected.Keywords?.Trim() ?? string.Empty));
                    command.Parameters.Add(EfSql.Input("@ExpectedYear", expected.Year));
                    command.Parameters.Add(EfSql.Input("@ExpectedDisplay", expected.IsVisible ? 1 : 0));
                }
            },
            cancellationToken: cancellationToken);

        await EnsurePhotoWriteAsync(picId, rows, cancellationToken);

        await AppendAuditAsync(picId, "edit", editorEmail, $"Updated \"{request.Title.Trim()}\"", cancellationToken);
    }

    public async Task SetVisibilityAsync(
        int picId,
        bool isVisible,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            UPDATE dbo.PIC_FILES_T
            SET DISPLAY = @Display
            WHERE PIC_ID = @PicId
            """;
        if (expectedIsVisible is bool expected)
        {
            sql += " AND DISPLAY = @ExpectedDisplay";
        }

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Display", isVisible ? 1 : 0));
                command.Parameters.Add(EfSql.Input("@PicId", picId));
                if (expectedIsVisible is bool expected)
                {
                    command.Parameters.Add(EfSql.Input("@ExpectedDisplay", expected ? 1 : 0));
                }
            },
            cancellationToken: cancellationToken);

        await EnsurePhotoWriteAsync(picId, rows, cancellationToken);

        await AppendAuditAsync(picId, isVisible ? "show" : "hide", editorEmail, cancellationToken: cancellationToken);
    }

    public async Task UpdateAssetsAsync(
        int picId,
        AdminPhotoAssetUpdate assets,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.PIC_FILES_T
            SET Url = @Url,
                Thumb_URL = @ThumbUrl,
                t_width = @ThumbWidth,
                t_height = @ThumbHeight,
                PIC_WIDTH = @PictureWidth,
                PIC_HEIGHT = @PictureHeight
            WHERE PIC_ID = @PicId
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Url", assets.LegacyUrl));
                command.Parameters.Add(EfSql.Input("@ThumbUrl", assets.LegacyThumbUrl));
                command.Parameters.Add(EfSql.Input("@ThumbWidth", assets.ThumbWidth));
                command.Parameters.Add(EfSql.Input("@ThumbHeight", assets.ThumbHeight));
                command.Parameters.Add(EfSql.Input("@PictureWidth", assets.PictureWidth));
                command.Parameters.Add(EfSql.Input("@PictureHeight", assets.PictureHeight));
                command.Parameters.Add(EfSql.Input("@PicId", picId));
            },
            cancellationToken: cancellationToken);

        if (rows != 1)
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        await AppendAuditAsync(picId, "replace", editorEmail, "Replaced image assets", cancellationToken);
    }

    public async Task UpdateThumbnailAsync(
        int picId,
        string legacyThumbUrl,
        int thumbWidth,
        int thumbHeight,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.PIC_FILES_T
            SET Thumb_URL = @ThumbUrl,
                t_width = @ThumbWidth,
                t_height = @ThumbHeight
            WHERE PIC_ID = @PicId
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@ThumbUrl", legacyThumbUrl));
                command.Parameters.Add(EfSql.Input("@ThumbWidth", thumbWidth));
                command.Parameters.Add(EfSql.Input("@ThumbHeight", thumbHeight));
                command.Parameters.Add(EfSql.Input("@PicId", picId));
            },
            cancellationToken: cancellationToken);

        if (rows != 1)
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        await AppendAuditAsync(picId, "regenerate-thumb", editorEmail, legacyThumbUrl, cancellationToken);
    }

    private async Task EnsurePhotoWriteAsync(int picId, int affected, CancellationToken cancellationToken)
    {
        if (affected == 1)
        {
            return;
        }

        var exists = await GetByIdAsync(picId, cancellationToken) is not null;
        QueenZoneConcurrency.EnsureUpdated(affected, exists, $"Photo {picId} was not found.");
    }

    public async Task DeleteAsync(int picId, string editorEmail, CancellationToken cancellationToken = default)
    {
        var existing = await GetByIdAsync(picId, cancellationToken)
            ?? throw new InvalidOperationException($"Photo {picId} was not found.");

        const string sql = """
            DELETE FROM dbo.PIC_FILES_T
            WHERE PIC_ID = @PicId
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@PicId", picId)),
            cancellationToken: cancellationToken);

        if (rows != 1)
        {
            throw new InvalidOperationException($"Photo {picId} was not found.");
        }

        await AppendAuditAsync(picId, "delete", editorEmail, $"Deleted \"{existing.Title}\"", cancellationToken);
    }

    public async Task AppendAuditAsync(
        int picId,
        string action,
        string actorEmail,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.PhotoAdminAuditLogs.Add(new PhotoAdminAuditLogEntity
        {
            PicId = picId,
            Action = action,
            ActorEmail = actorEmail,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = details,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void AddFilterParameters(SqlCommand command, AdminPhotoListFilter filter)
    {
        if (filter.CatId is int catId)
        {
            command.Parameters.Add(EfSql.Input("@CatId", catId));
        }

        if (filter.IsVisible is bool isVisible)
        {
            command.Parameters.Add(EfSql.Input("@Display", isVisible ? 1 : 0));
        }

        if (filter.Year is int year)
        {
            command.Parameters.Add(EfSql.Input("@Year", year));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            command.Parameters.Add(EfSql.Input("@Search", "%" + filter.Search.Trim() + "%"));
        }
    }

    private static AdminPhotoItem MapRow(AdminPhotoRow row) =>
        new(
            row.PicId,
            row.CatId,
            row.CategoryName,
            NewsSlug.Slugify(row.CategoryName),
            row.Title,
            row.LegacyUrl ?? string.Empty,
            row.LegacyThumbUrl ?? string.Empty,
            PhotoImageUrl.Build(row.LegacyUrl ?? string.Empty),
            PhotoImageUrl.Build(row.LegacyThumbUrl ?? string.Empty),
            row.ThumbWidth,
            row.ThumbHeight,
            row.PictureWidth,
            row.PictureHeight,
            row.Year,
            row.DateTime,
            row.Keywords,
            row.IsVisible);

    private sealed class AdminPhotoRow
    {
        public int PicId { get; set; }

        public int CatId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? LegacyUrl { get; set; }

        public string? LegacyThumbUrl { get; set; }

        public int ThumbWidth { get; set; }

        public int ThumbHeight { get; set; }

        public int PictureWidth { get; set; }

        public int PictureHeight { get; set; }

        public int Year { get; set; }

        public DateTime DateTime { get; set; }

        public string? Keywords { get; set; }

        public bool IsVisible { get; set; }
    }

    private sealed class CategoryRow
    {
        public int CatId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class LegacyUrlRow
    {
        public string? LegacyUrl { get; set; }

        public string? LegacyThumbUrl { get; set; }
    }
}
