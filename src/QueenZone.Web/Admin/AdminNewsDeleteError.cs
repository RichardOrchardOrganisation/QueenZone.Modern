using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Web;

internal static class AdminNewsDeleteError
{
    private static readonly Func<Exception?, bool> DefaultForeignKeyViolationClassifier =
        inner => inner is SqlException { Number: 547 };
    private static readonly object ClassifierLock = new();
    private static Func<Exception?, bool> foreignKeyViolationClassifier = DefaultForeignKeyViolationClassifier;

    internal static bool IsForeignKeyViolation(DbUpdateException exception) =>
        IsForeignKeyViolation(exception.InnerException);

    internal static bool IsDeleteForeignKeyViolation(Exception exception) =>
        exception is SqlException { Number: 547 }
        || exception is DbUpdateException db && IsForeignKeyViolation(db);

    internal static IDisposable UseForeignKeyViolationClassifier(Func<Exception?, bool> classifier)
    {
        lock (ClassifierLock)
        {
            var previous = foreignKeyViolationClassifier;
            foreignKeyViolationClassifier = classifier;
            return new ResetScope(previous);
        }
    }

    private static bool IsForeignKeyViolation(Exception? innerException)
    {
        lock (ClassifierLock)
        {
            return foreignKeyViolationClassifier(innerException);
        }
    }

    private static void RestoreClassifier(Func<Exception?, bool> previous)
    {
        lock (ClassifierLock)
        {
            foreignKeyViolationClassifier = previous;
        }
    }

    private sealed class ResetScope(Func<Exception?, bool> previous) : IDisposable
    {
        public void Dispose() => RestoreClassifier(previous);
    }
}
