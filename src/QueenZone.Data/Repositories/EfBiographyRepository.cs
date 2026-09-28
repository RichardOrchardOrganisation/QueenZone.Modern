using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Public biography reads via the legacy stored procedures, invoked through EF Core
/// (<c>SqlQuery</c> / <c>SqlQueryRaw</c>) rather than Dapper.
/// Admin writes mirror <c>Q_BIO_UPDATE_SP</c> against <c>Q_BIO_T</c>.
/// </summary>
public sealed class EfBiographyRepository : IBiographyRepository
{
    private readonly QueenZoneDbContext dbContext;
    private readonly string listSql;
    private readonly Func<short, FormattableString> detailSql;

    public EfBiographyRepository(QueenZoneDbContext dbContext)
        : this(
            dbContext,
            listSql: EfProductionSql.CreateBiographyQueries().ListSql,
            detailSql: EfProductionSql.CreateBiographyQueries().DetailSql)
    {
    }

    /// <summary>
    /// Test hook: substitute SELECT statements (e.g. SQLite) for the production EXEC calls.
    /// </summary>
    internal EfBiographyRepository(
        QueenZoneDbContext dbContext,
        string listSql,
        Func<short, FormattableString> detailSql)
    {
        this.dbContext = dbContext;
        this.listSql = listSql;
        this.detailSql = detailSql;
    }

    public async Task<IReadOnlyList<BiographyChapterItem>> GetChaptersAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<ListRow>(listSql)
            .ToListAsync(cancellationToken);

        return rows.Select(MapListRow).ToList();
    }

    public async Task<BiographyChapterItem?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQuery<DetailRow>(detailSql((short)id))
            .ToListAsync(cancellationToken);

        var row = rows.FirstOrDefault();
        return row is null ? null : MapDetailRow(id, row);
    }

    public async Task<BiographyChapterNav> GetAdjacentChaptersAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var chapters = BiographyChapterOrdering.ByDisplaySequenceAscending(
            await GetChaptersAsync(cancellationToken));
        var index = chapters.ToList().FindIndex(chapter => chapter.Id == id);
        if (index < 0)
        {
            return new BiographyChapterNav(null, null);
        }

        var previous = index > 0 ? chapters[index - 1] : null;
        var next = index < chapters.Count - 1 ? chapters[index + 1] : null;
        return new BiographyChapterNav(previous, next);
    }

    public async Task<int> CreateAsync(AdminBiographyDraft draft, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.Q_BIO_T (TITLE, SUMMARY, BIO_TEXT, DISPLAY_SEQUENCE)
            OUTPUT CAST(INSERTED.Q_BIO_ID AS int)
            VALUES (@TITLE, @SUMMARY, @BIO_TEXT, @DISPLAY_SEQUENCE);
            """;

        return await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            sql,
            command => AddDraftParameters(command, draft),
            cancellationToken: cancellationToken);
    }

    public async Task UpdateAsync(int id, AdminBiographyDraft draft, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Q_BIO_T
            SET
                TITLE = @TITLE,
                SUMMARY = @SUMMARY,
                BIO_TEXT = @BIO_TEXT,
                DISPLAY_SEQUENCE = @DISPLAY_SEQUENCE,
                CREATE_DATE = GETDATE()
            WHERE Q_BIO_ID = @Q_BIO_ID;
            """;

        var rows = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            sql,
            command =>
            {
                AddDraftParameters(command, draft);
                command.Parameters.Add(EfSql.Input("@Q_BIO_ID", (short)id));
            },
            cancellationToken: cancellationToken);

        if (rows == 0)
        {
            throw new InvalidOperationException($"Biography chapter {id} was not found.");
        }
    }

    private static void AddDraftParameters(Microsoft.Data.SqlClient.SqlCommand command, AdminBiographyDraft draft)
    {
        command.Parameters.Add(EfSql.Input("@TITLE", draft.Title));
        command.Parameters.Add(EfSql.Input("@SUMMARY", draft.Summary));
        command.Parameters.Add(EfSql.Input("@BIO_TEXT", draft.Body));
        command.Parameters.Add(EfSql.Input("@DISPLAY_SEQUENCE", draft.DisplaySequence));
    }

    private static BiographyChapterItem MapListRow(ListRow row) =>
        new(
            row.Q_BIO_ID,
            row.TITLE?.Trim() ?? string.Empty,
            ResolveSummary(row.SUMMARY),
            string.Empty,
            row.DISPLAY_SEQUENCE,
            row.CREATE_DATE);

    private static BiographyChapterItem MapDetailRow(int id, DetailRow row) =>
        new(
            id,
            row.TITLE?.Trim() ?? string.Empty,
            ResolveSummary(row.SUMMARY, row.BIO_TEXT),
            row.BIO_TEXT ?? string.Empty,
            row.DISPLAY_SEQUENCE,
            DateTime.MinValue);

    private static string ResolveSummary(string? summary, string? body = null)
    {
        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary.Trim();
        }

        return LegacyArticleText.GetExcerpt(body);
    }

    internal sealed class ListRow
    {
        public string? BIOTITLE { get; init; }

        public short Q_BIO_ID { get; init; }

        public DateTime CREATE_DATE { get; init; }

        public string? TITLE { get; init; }

        public byte DISPLAY_SEQUENCE { get; init; }

        public string? SUMMARY { get; init; }
    }

    internal sealed class DetailRow
    {
        public string? TITLE { get; init; }

        public string? SUMMARY { get; init; }

        public string? BIO_TEXT { get; init; }

        public byte DISPLAY_SEQUENCE { get; init; }
    }
}
