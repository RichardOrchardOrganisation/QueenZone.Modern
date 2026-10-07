using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Creates the EF-managed <c>DiscographyStreamingLinks</c> table from its real migration, next to
/// the hand-built legacy discography tables.
/// </summary>
internal static class DiscographyStreamingLinkSchema
{
    public static async Task CreateAsync(QueenZoneDbContext dbContext)
    {
        var commands = dbContext.GetService<IMigrationsSqlGenerator>()
            .Generate(new AddDiscographyStreamingLinks().UpOperations);
        foreach (var command in commands)
        {
            await dbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }
}
