using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QueenZone.Web.Tests;

internal sealed class QueryCounter : DbCommandInterceptor
{
    private readonly List<string> readerCommands = [];

    public int ReaderCount => readerCommands.Count;

    public IReadOnlyList<string> ReaderCommands => readerCommands;

    public void Reset() => readerCommands.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        readerCommands.Add(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        readerCommands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
