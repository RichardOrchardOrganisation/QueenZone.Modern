using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using QueenZone.Tools;

namespace QueenZone.Tools.Tests;

public sealed class MaintenanceSqlParameterTests
{
    [Fact]
    public void Thumbnail_ids_are_bound_as_data_in_a_fixed_query()
    {
        using var command = new SqlCommand();
        var options = GeneratePhotoThumbsOptions.Parse(
            ["--connection-string", "Server=.;Database=test;", "--dry-run", "--pic-ids", "17,23"]);
        Assert.True(options.IsValid);
        GeneratePhotoThumbsCommand.ConfigureQuery(command, options);
        Assert.Contains("OPENJSON(@Ids) WITH (Id int '$')", command.CommandText);
        Assert.DoesNotContain("17", command.CommandText);
        Assert.Equal(new[] { 17, 23 }, JsonSerializer.Deserialize<int[]>((string)command.Parameters["@Ids"].Value));
    }

    [Fact]
    public void IdSets_preserve_bigint_precision_and_use_one_unbounded_parameter()
    {
        var ids = Enumerable.Range(0, 2500).Select(i => long.MaxValue - i).ToArray();
        using var command = new SqlCommand();
        SqlIdSet.Add(command, ids);
        var parameter = Assert.Single(command.Parameters.Cast<SqlParameter>());
        Assert.Equal(SqlDbType.NVarChar, parameter.SqlDbType);
        Assert.Equal(-1, parameter.Size);
        Assert.Equal(ids, JsonSerializer.Deserialize<long[]>((string)parameter.Value));
    }

    [Fact]
    public void IdSets_preserve_guids_and_empty_sets()
    {
        using var command = new SqlCommand();
        var ids = new[] { Guid.NewGuid(), Guid.Empty };
        SqlIdSet.Add(command, ids);
        Assert.Equal(ids, JsonSerializer.Deserialize<Guid[]>((string)command.Parameters["@Ids"].Value));
        command.Parameters.Clear();
        SqlIdSet.Add(command, Array.Empty<int>());
        Assert.Equal("[]", command.Parameters["@Ids"].Value);
    }

    [Fact]
    public void BbCode_limit_is_a_parameter_and_unlimited_remains_unlimited()
    {
        using var limited = new SqlCommand();
        using var unlimited = new SqlCommand();
        ConvertLegacyBbCodeCommand.ConfigureQuery(limited, ConvertLegacyBbCodeOptions.Parse(["--connection-string", "Server=.;Database=test;", "--limit", "7"]));
        ConvertLegacyBbCodeCommand.ConfigureQuery(unlimited, ConvertLegacyBbCodeOptions.Parse(["--connection-string", "Server=.;Database=test;"]));
        Assert.Equal(limited.CommandText, unlimited.CommandText);
        Assert.Contains("TOP (@Limit)", limited.CommandText);
        Assert.Equal(7L, limited.Parameters["@Limit"].Value);
        Assert.Equal(long.MaxValue, unlimited.Parameters["@Limit"].Value);
        Assert.Equal(180, limited.CommandTimeout);
    }

    [Fact]
    public void Backfill_options_change_parameters_without_changing_SQL()
    {
        using var defaults = new SqlCommand();
        using var filtered = new SqlCommand();
        BackfillPhotoDimensionsCommand.ConfigureQuery(defaults, BackfillPhotoDimensionsOptions.Parse(["--connection-string", "Server=.;Database=test;"]));
        BackfillPhotoDimensionsCommand.ConfigureQuery(filtered, BackfillPhotoDimensionsOptions.Parse(["--connection-string", "Server=.;Database=test;", "--limit", "2", "--category-id", "9", "--pic-ids", "1,3", "--force"]));
        Assert.Equal(defaults.CommandText, filtered.CommandText);
        Assert.Equal(DBNull.Value, defaults.Parameters["@CategoryId"].Value);
        Assert.Equal(false, defaults.Parameters["@FilterIds"].Value);
        Assert.Equal(false, defaults.Parameters["@Force"].Value);
        Assert.Equal(true, defaults.Parameters["@PublicOnly"].Value);
        Assert.Equal(long.MaxValue, defaults.Parameters["@Limit"].Value);
        Assert.Equal(9, filtered.Parameters["@CategoryId"].Value);
        Assert.Equal(true, filtered.Parameters["@FilterIds"].Value);
        Assert.Equal(true, filtered.Parameters["@Force"].Value);
        Assert.Equal(2L, filtered.Parameters["@Limit"].Value);
        Assert.Equal(new[] { 1, 3 }, JsonSerializer.Deserialize<int[]>((string)filtered.Parameters["@Ids"].Value));
    }
}
