using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Data.SqlClient;

namespace QueenZone.Data;

/// <summary>
/// Admin writes against legacy <c>Q_STAGE_T</c>. The table stays outside EF schema ownership.
/// </summary>
[ExcludeFromCodeCoverage] // Requires SQL Server legacy stage table; covered via in-memory admin tests.
public sealed class EfAdminFanPerformanceRepository(QueenZoneDbContext dbContext) : IAdminFanPerformanceRepository
{
    public async Task<AdminFanPerformancePage> GetPageAsync(
        AdminFanPerformanceListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (safePage - 1) * safePageSize;

        var where = new StringBuilder("WHERE 1 = 1");
        if (filter.IsVisible is bool)
        {
            where.Append(" AND DISPLAY = @Display");
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Append(" AND (TITLE LIKE @Search OR PERFORMED_BY LIKE @Search OR DESCRIPTION LIKE @Search)");
        }

        var countSql = $"""
            SELECT COUNT(1)
            FROM dbo.Q_STAGE_T
            {where}
            """;

        var total = await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            countSql,
            command => AddFilterParameters(command, filter),
            cancellationToken: cancellationToken);

        var listSql = $"""
            SELECT
                CAST(Q_STAGE_ID AS int) AS Id,
                TITLE AS Title,
                PERFORMED_BY AS PerformedBy,
                DESCRIPTION AS Description,
                URL AS AudioFileName,
                thesize AS FileSizeText,
                DATE_ADDED AS DateAdded,
                DurationSeconds,
                CAST(CASE WHEN DISPLAY = 1 THEN 1 ELSE 0 END AS bit) AS IsVisible
            FROM dbo.Q_STAGE_T
            {where}
            ORDER BY DATE_ADDED DESC, Q_STAGE_ID DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await EfSql.QuerySqlAsync<AdminFanPerformanceRow>(
            dbContext,
            listSql,
            command =>
            {
                AddFilterParameters(command, filter);
                command.Parameters.Add(EfSql.Input("@Offset", offset));
                command.Parameters.Add(EfSql.Input("@PageSize", safePageSize));
            },
            cancellationToken: cancellationToken);

        IReadOnlyList<AdminFanPerformanceItem> items = rows.Select(MapRow).ToList();
        return new AdminFanPerformancePage(items, total, safePage, safePageSize);
    }

    public async Task<AdminFanPerformanceItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                CAST(Q_STAGE_ID AS int) AS Id,
                TITLE AS Title,
                PERFORMED_BY AS PerformedBy,
                DESCRIPTION AS Description,
                URL AS AudioFileName,
                thesize AS FileSizeText,
                DATE_ADDED AS DateAdded,
                DurationSeconds,
                CAST(CASE WHEN DISPLAY = 1 THEN 1 ELSE 0 END AS bit) AS IsVisible
            FROM dbo.Q_STAGE_T
            WHERE Q_STAGE_ID = @Id
            """;

        var rows = await EfSql.QuerySqlAsync<AdminFanPerformanceRow>(
            dbContext,
            sql,
            command => command.Parameters.Add(EfSql.Input("@Id", id)),
            cancellationToken: cancellationToken);

        var row = rows.FirstOrDefault();
        return row is null ? null : MapRow(row);
    }

    public async Task<int> CreateAsync(
        AdminFanPerformanceCreateRequest request,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        const string sql = """
            INSERT INTO dbo.Q_STAGE_T
            (
                TITLE, PERFORMED_BY, DESCRIPTION, URL, thesize, DATE_ADDED, DISPLAY, DurationSeconds
            )
            VALUES
            (
                @Title, @PerformedBy, @Description, @Url, @TheSize, @DateAdded, @Display, @DurationSeconds
            );
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        return await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Title", request.Title.Trim()));
                command.Parameters.Add(EfSql.Input("@PerformedBy", request.PerformedBy.Trim()));
                command.Parameters.Add(EfSql.Input(
                    "@Description",
                    string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim()));
                command.Parameters.Add(EfSql.Input("@Url", request.AudioFileName.Trim()));
                command.Parameters.Add(EfSql.Input("@TheSize", request.FileSizeBytes.ToString()));
                command.Parameters.Add(EfSql.Input("@DateAdded", request.DateAdded));
                command.Parameters.Add(EfSql.Input("@Display", request.IsVisible ? 1 : 0));
                command.Parameters.Add(EfSql.Input("@DurationSeconds", request.DurationSeconds is int seconds ? seconds : DBNull.Value));
            },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateAsync(
        int id,
        AdminFanPerformanceUpdateRequest request,
        string editorEmail,
        AdminFanPerformanceConcurrencyToken? expected = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        var sql = """
            UPDATE dbo.Q_STAGE_T
            SET TITLE = @Title,
                PERFORMED_BY = @PerformedBy,
                DESCRIPTION = @Description,
                DATE_ADDED = @DateAdded
            WHERE Q_STAGE_ID = @Id
            """;
        if (expected is not null)
        {
            sql += """
                 AND TITLE = @ExpectedTitle
                 AND PERFORMED_BY = @ExpectedPerformedBy
                 AND ISNULL(DESCRIPTION, '') = @ExpectedDescription
                 AND DATE_ADDED = @ExpectedDateAdded
                 AND DISPLAY = @ExpectedDisplay
                """;
        }

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Title", request.Title.Trim()));
                command.Parameters.Add(EfSql.Input("@PerformedBy", request.PerformedBy.Trim()));
                command.Parameters.Add(EfSql.Input(
                    "@Description",
                    string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim()));
                command.Parameters.Add(EfSql.Input("@DateAdded", request.DateAdded));
                command.Parameters.Add(EfSql.Input("@Id", id));
                if (expected is not null)
                {
                    command.Parameters.Add(EfSql.Input("@ExpectedTitle", expected.Title.Trim()));
                    command.Parameters.Add(EfSql.Input("@ExpectedPerformedBy", expected.PerformedBy.Trim()));
                    command.Parameters.Add(EfSql.Input("@ExpectedDescription", expected.Description.Trim()));
                    command.Parameters.Add(EfSql.Input("@ExpectedDateAdded", expected.DateAdded));
                    command.Parameters.Add(EfSql.Input("@ExpectedDisplay", expected.IsVisible ? 1 : 0));
                }
            },
            cancellationToken: cancellationToken);

        await EnsureWriteAsync(id, rows, cancellationToken);
    }

    public async Task SetVisibilityAsync(
        int id,
        bool isVisible,
        string editorEmail,
        bool? expectedIsVisible = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        var sql = """
            UPDATE dbo.Q_STAGE_T
            SET DISPLAY = @Display
            WHERE Q_STAGE_ID = @Id
            """;
        if (expectedIsVisible is bool)
        {
            sql += " AND DISPLAY = @ExpectedDisplay";
        }

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Display", isVisible ? 1 : 0));
                command.Parameters.Add(EfSql.Input("@Id", id));
                if (expectedIsVisible is bool expected)
                {
                    command.Parameters.Add(EfSql.Input("@ExpectedDisplay", expected ? 1 : 0));
                }
            },
            cancellationToken: cancellationToken);

        await EnsureWriteAsync(id, rows, cancellationToken);
    }

    private async Task EnsureWriteAsync(int id, int affected, CancellationToken cancellationToken)
    {
        if (affected == 1)
        {
            return;
        }

        var exists = await GetByIdAsync(id, cancellationToken) is not null;
        QueenZoneConcurrency.EnsureUpdated(affected, exists, $"Fan performance {id} was not found.");
    }

    private static void AddFilterParameters(SqlCommand command, AdminFanPerformanceListFilter filter)
    {
        if (filter.IsVisible is bool isVisible)
        {
            command.Parameters.Add(EfSql.Input("@Display", isVisible ? 1 : 0));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            command.Parameters.Add(EfSql.Input("@Search", "%" + filter.Search.Trim() + "%"));
        }
    }

    private static AdminFanPerformanceItem MapRow(AdminFanPerformanceRow row) =>
        new(
            row.Id,
            row.Title,
            row.PerformedBy,
            row.Description ?? string.Empty,
            row.AudioFileName?.Trim() ?? string.Empty,
            ParseFileSize(row.FileSizeText),
            row.DateAdded,
            row.IsVisible,
            row.DurationSeconds);

    private static long ParseFileSize(string? thesize) =>
        long.TryParse(thesize, out var parsed) ? parsed : 0;

    private sealed class AdminFanPerformanceRow
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string PerformedBy { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? AudioFileName { get; set; }

        public string? FileSizeText { get; set; }

        public DateTime DateAdded { get; set; }

        public bool IsVisible { get; set; }

        public int? DurationSeconds { get; set; }
    }
}
