using System.Data;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Schema-presence helpers for opt-in SQL Express mirror probes.
/// </summary>
internal static class LiveProbeSchema
{
    public const string ForumPostReportsTable = "ForumPostReports";

    internal const string SqlServerTableExistsSql =
        "SELECT CASE WHEN OBJECT_ID(@table, N'U') IS NULL THEN 0 ELSE 1 END";

    internal const string SqliteTableExistsSql =
        "SELECT CASE WHEN COUNT(*) = 0 THEN 0 ELSE 1 END FROM sqlite_master WHERE type = 'table' AND name = @table";

    public static async Task<bool> TableExistsAsync(
        QueenZoneDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        var isSqlite = (dbContext.Database.ProviderName ?? string.Empty)
            .Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        command.CommandText = isSqlite ? SqliteTableExistsSql : SqlServerTableExistsSql;

        var table = command.CreateParameter();
        table.ParameterName = "@table";
        table.Value = isSqlite || tableName.Contains('.', StringComparison.Ordinal)
            ? tableName
            : $"dbo.{tableName}";
        command.Parameters.Add(table);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}
