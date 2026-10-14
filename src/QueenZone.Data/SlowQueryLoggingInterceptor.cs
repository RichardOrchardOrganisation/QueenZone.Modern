using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QueenZone.Data;

/// <summary>
/// Logs every EF Core command slower than <see cref="SlowQueryLoggingOptions.SlowQueryThresholdMilliseconds"/>
/// at Warning (so it is exported to Application Insights). Logs command text and parameter
/// names only, never parameter values. Reader duration is time to execute the reader, not to
/// finish enumerating it.
/// </summary>
public sealed class SlowQueryLoggingInterceptor(
    IOptions<SlowQueryLoggingOptions> options,
    ILogger<SlowQueryLoggingInterceptor> logger) : DbCommandInterceptor
{
    private const int MaxCommandTextLength = 500;

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogIfSlow(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogIfSlow(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        LogIfSlow(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        var threshold = options.Value.SlowQueryThresholdMilliseconds;
        if (threshold <= 0 || eventData.Duration.TotalMilliseconds < threshold)
        {
            return;
        }

        var text = command.CommandText;
        if (text.Length > MaxCommandTextLength)
        {
            text = text[..MaxCommandTextLength];
        }

        logger.LogWarning(
            "Slow SQL command took {DurationMs}ms (threshold {ThresholdMs}ms), type {CommandType}, "
            + "parameters [{ParameterNames}]: {CommandText}",
            (int)eventData.Duration.TotalMilliseconds,
            threshold,
            command.CommandType,
            string.Join(", ", command.Parameters.Cast<DbParameter>().Select(p => p.ParameterName)),
            text);
    }
}
