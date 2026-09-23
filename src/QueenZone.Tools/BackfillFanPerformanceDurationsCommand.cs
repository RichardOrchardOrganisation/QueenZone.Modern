using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>One-off, resumable blob inspection for legacy Q_STAGE_T rows.</summary>
[ExcludeFromCodeCoverage] // Requires a SQL Server legacy stage table and private Azure blobs.
internal static class BackfillFanPerformanceDurationsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var apply = false;
        string? sql = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        string? storage = Environment.GetEnvironmentVariable("ConnectionStrings__BlobStorage")
            ?? Environment.GetEnvironmentVariable("AzureStorage__ConnectionString");
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply": apply = true; break;
                case "--connection-string" when i + 1 < args.Length: sql = args[++i]; break;
                case "--storage-connection-string" when i + 1 < args.Length: storage = args[++i]; break;
                default: return Usage($"Unknown or incomplete argument: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(sql) || string.IsNullOrWhiteSpace(storage))
        {
            return Usage("SQL and blob storage connection strings are required.");
        }

        var blobService = new BlobServiceClient(storage);
        await using var connection = new SqlConnection(sql);
        await connection.OpenAsync();
        var rows = new List<(int Id, string Name, long Size)>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT CAST(Q_STAGE_ID AS int), URL, thesize FROM dbo.Q_STAGE_T WHERE DurationSeconds IS NULL ORDER BY Q_STAGE_ID";
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) || !long.TryParse(reader.GetValue(2).ToString(), out var size) ? 0 : size));
            }
        }

        var succeeded = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            try
            {
                var name = SongFileUrl.GetBlobName(row.Name);
                if (!SongFileUrl.IsSafeBlobName(row.Name) || string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException("Unsafe or empty blob name");
                }

                var blob = blobService.GetBlobContainerClient(SongFileUrl.ContainerName).GetBlobClient(name);
                await using var stream = await blob.OpenReadAsync();
                var prefix = new byte[Mp3Duration.PrefixBytes];
                var read = 0;
                while (read < prefix.Length)
                {
                    var count = await stream.ReadAsync(prefix.AsMemory(read));
                    if (count == 0) break;
                    read += count;
                }

                var length = stream.CanSeek ? stream.Length : row.Size;
                var duration = Mp3Duration.TryGetSeconds(prefix.AsSpan(0, read), length);
                if (duration is null)
                {
                    throw new InvalidDataException("No readable MPEG duration");
                }

                if (apply)
                {
                    await using var update = connection.CreateCommand();
                    update.CommandText = "UPDATE dbo.Q_STAGE_T SET DurationSeconds = @Duration WHERE Q_STAGE_ID = @Id AND DurationSeconds IS NULL";
                    update.Parameters.AddWithValue("@Duration", duration.Value);
                    update.Parameters.AddWithValue("@Id", row.Id);
                    await update.ExecuteNonQueryAsync();
                }

                succeeded++;
                Console.WriteLine($"{row.Id}: {duration} seconds{(apply ? " saved" : " (dry run)")}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                Console.Error.WriteLine($"{row.Id}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"Processed {rows.Count}; resolved {succeeded}; failed {failed}. Re-run is safe: populated rows are skipped.");
        return failed == 0 ? 0 : 1;
    }

    private static int Usage(string error)
    {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine("Usage: dotnet run --project src/QueenZone.Tools -- backfill-fan-performance-durations [--connection-string <sql>] [--storage-connection-string <blob>] [--apply]");
        Console.Error.WriteLine("Default is dry run; --apply updates only rows with null DurationSeconds. Run after the EF migration.");
        return 2;
    }
}
