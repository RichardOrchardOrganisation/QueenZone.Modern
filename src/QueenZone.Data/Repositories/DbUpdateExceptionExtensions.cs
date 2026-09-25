using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

internal static class DbUpdateExceptionExtensions
{
    /// <summary>
    /// True when any inner exception is a unique-key or unique-index violation: SQL Server error
    /// 2601 / 2627, or the SQLite and provider messages for the same failure.
    /// </summary>
    internal static bool IsUniqueConstraintViolation(this DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqlException sql && sql.Number is 2601 or 2627)
            {
                return true;
            }

            if (inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || inner.Message.Contains("unique index", StringComparison.OrdinalIgnoreCase)
                || inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
