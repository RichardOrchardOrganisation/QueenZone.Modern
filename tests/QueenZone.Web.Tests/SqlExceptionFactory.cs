using System.Reflection;
using Microsoft.Data.SqlClient;

namespace QueenZone.Web.Tests;

/// <summary>Builds real <see cref="SqlException"/> instances; the type has no public constructor.</summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int number = 208, string message = "Invalid object name.")
    {
        var sqlClient = typeof(SqlException).Assembly;
        var errorCollectionType = sqlClient.GetType("Microsoft.Data.SqlClient.SqlErrorCollection")
            ?? throw new InvalidOperationException("SqlErrorCollection type not found.");
        var errorType = sqlClient.GetType("Microsoft.Data.SqlClient.SqlError")
            ?? throw new InvalidOperationException("SqlError type not found.");

        var collection = Activator.CreateInstance(errorCollectionType, nonPublic: true)
            ?? throw new InvalidOperationException("Unable to create SqlErrorCollection.");

        var errorCtor = errorType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var errorArgs = errorCtor.GetParameters().Select(p =>
        {
            if (p.Name is "infoNumber" or "number")
            {
                return number;
            }

            if (p.ParameterType == typeof(int))
            {
                return 0;
            }

            if (p.ParameterType == typeof(byte))
            {
                return (byte)16;
            }

            if (p.ParameterType == typeof(string))
            {
                return p.Name is "errorMessage" or "message" ? message : "server";
            }

            if (p.ParameterType == typeof(uint))
            {
                return 0u;
            }

            if (typeof(Exception).IsAssignableFrom(p.ParameterType))
            {
                return null!;
            }

            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)! : null!;
        }).ToArray();
        var error = errorCtor.Invoke(errorArgs);

        errorCollectionType
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(collection, [error]);

        var createException = typeof(SqlException)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "CreateException")
            .OrderBy(m => m.GetParameters().Length)
            .First();
        var createArgs = createException.GetParameters().Select(p =>
        {
            if (p.ParameterType == errorCollectionType)
            {
                return collection;
            }

            if (p.ParameterType == typeof(string))
            {
                return "12.0.0";
            }

            if (p.ParameterType == typeof(Guid))
            {
                return Guid.Empty;
            }

            return null!;
        }).ToArray();

        return (SqlException)createException.Invoke(null, createArgs)!;
    }
}
