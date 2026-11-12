using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace QueenZone.Tools;

internal static class SqlIdSet
{
    // OPENJSON keeps the SQL text fixed and preserves bigint/Guid values without
    // expanding one SQL parameter per ID. Each query declares its SQL ID type.
    internal static void Add<T>(SqlCommand command, IReadOnlyList<T> ids) where T : struct =>
        command.Parameters.Add("@Ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(ids);
}
