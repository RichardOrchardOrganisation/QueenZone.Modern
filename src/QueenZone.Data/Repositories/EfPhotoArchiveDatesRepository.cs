using System.Data;
using Microsoft.Data.SqlClient;

namespace QueenZone.Data;

/// <summary>
/// SQL Server implementation over the nullable <c>PIC_EVENT_*</c> and <c>PIC_TAKEN_*</c> columns that
/// <c>AddPicFilesArchiveDates</c> adds to <c>dbo.PIC_FILES_T</c>. Raw SQL like the other legacy photo
/// repositories; the pure date logic lives in <see cref="PartialDate"/>.
/// </summary>
public sealed class EfPhotoArchiveDatesRepository(QueenZoneDbContext dbContext) : IPhotoArchiveDatesRepository
{
    internal const string SelectSql = """
        SELECT
            PIC_EVENT_DATE AS EventDate,
            PIC_EVENT_PRECISION AS EventPrecision,
            PIC_TAKEN_DATE AS TakenDate,
            PIC_TAKEN_PRECISION AS TakenPrecision
        FROM dbo.PIC_FILES_T
        WHERE PIC_ID = @PicId
        """;

    internal const string UpdateSql = """
        UPDATE dbo.PIC_FILES_T
        SET PIC_EVENT_DATE = @EventDate,
            PIC_EVENT_PRECISION = @EventPrecision,
            PIC_TAKEN_DATE = @TakenDate,
            PIC_TAKEN_PRECISION = @TakenPrecision
        WHERE PIC_ID = @PicId
          AND ISNULL(PIC_EVENT_DATE, '19000101') = ISNULL(@ExpectedEventDate, '19000101')
          AND ISNULL(PIC_EVENT_PRECISION, '') = ISNULL(@ExpectedEventPrecision, '')
          AND ISNULL(PIC_TAKEN_DATE, '19000101') = ISNULL(@ExpectedTakenDate, '19000101')
          AND ISNULL(PIC_TAKEN_PRECISION, '') = ISNULL(@ExpectedTakenPrecision, '')
        """;

    public async Task<PhotoArchiveDates> GetAsync(int picId, CancellationToken cancellationToken = default)
    {
        var rows = await EfSql.QuerySqlAsync<DatesRow>(
            dbContext,
            SelectSql,
            command => command.Parameters.Add(EfSql.Input("@PicId", picId)),
            cancellationToken: cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null
            ? PhotoArchiveDates.Unknown
            : new PhotoArchiveDates(
                PartialDate.FromDatabase(row.EventDate, row.EventPrecision),
                PartialDate.FromDatabase(row.TakenDate, row.TakenPrecision));
    }

    public async Task SetAsync(
        int picId,
        PhotoArchiveDates dates,
        PhotoArchiveDates expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(expected);

        var affected = await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            UpdateSql,
            command =>
            {
                command.Parameters.Add(EfSql.Input("@PicId", picId));
                AddDatePair(command, "EventDate", "EventPrecision", dates.EventDate);
                AddDatePair(command, "TakenDate", "TakenPrecision", dates.PhotoDate);
                AddDatePair(command, "ExpectedEventDate", "ExpectedEventPrecision", expected.EventDate);
                AddDatePair(command, "ExpectedTakenDate", "ExpectedTakenPrecision", expected.PhotoDate);
            },
            cancellationToken: cancellationToken);

        if (affected == 0)
        {
            var exists = await EfSql.ExecuteScalarSqlAsync(
                dbContext,
                "SELECT COUNT(1) FROM dbo.PIC_FILES_T WHERE PIC_ID = @PicId",
                command => command.Parameters.Add(EfSql.Input("@PicId", picId)),
                cancellationToken: cancellationToken) > 0;
            QueenZoneConcurrency.EnsureUpdated(affected, exists, $"Photo {picId} was not found.");
        }
    }

    private static void AddDatePair(SqlCommand command, string dateName, string precisionName, PartialDate? value)
    {
        command.Parameters.Add(new SqlParameter($"@{dateName}", SqlDbType.Date)
        {
            Value = value is { } date ? date.ToDatabaseDate() : DBNull.Value,
        });
        command.Parameters.Add(new SqlParameter($"@{precisionName}", SqlDbType.Char, 1)
        {
            Value = value is { } precise ? precise.Precision : DBNull.Value,
        });
    }

    private sealed class DatesRow
    {
        public DateTime? EventDate { get; set; }

        public string? EventPrecision { get; set; }

        public DateTime? TakenDate { get; set; }

        public string? TakenPrecision { get; set; }
    }
}
