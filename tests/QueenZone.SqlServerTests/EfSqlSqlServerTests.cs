using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Targeted SQL Server coverage for <see cref="EfSql"/> helpers that existing
/// repository SqlServerTests do not hit: connection-string scalars,
/// <see cref="EfSql.QuerySingleSqlAsync{T}"/> empty-result, output-parameter
/// factories, and <c>GetNullable*</c> null/DBNull. Production call sites already
/// cover query/scalar/non-query/procedure paths (#1896).
/// </summary>
public sealed class EfSqlSqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneEfSqlTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("""
                CREATE PROCEDURE dbo.EfSqlScratch_GetRow
                    @Id int,
                    @Found bit OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET @Found = CASE WHEN @Id = 1 THEN 1 ELSE 0 END;
                    SELECT @Id AS Id, CAST(NULL AS nvarchar(10)) AS Title
                    WHERE @Id = 1;
                END
                """);
            await schema.Database.ExecuteSqlRawAsync("""
                CREATE PROCEDURE dbo.EfSqlScratch_Scalar
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT CAST(NULL AS int);
                END
                """);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (dbContext is not null)
        {
            await dbContext.DisposeAsync();
        }

        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
    }

    [Fact]
    public void Output_and_nullable_helpers_map_values_null_and_dbnull()
    {
        var disco = EfSql.OutputByte("@DISCO");
        Assert.Equal(SqlDbType.TinyInt, disco.SqlDbType);
        Assert.Equal(ParameterDirection.Output, disco.Direction);

        var input = EfSql.Input("@x", null);
        Assert.Equal(DBNull.Value, input.Value);

        var n = EfSql.OutputInt("@n");
        Assert.Null(EfSql.GetNullableInt(n));
        n.Value = DBNull.Value;
        Assert.Null(EfSql.GetNullableInt(n));
        n.Value = 12;
        Assert.Equal(12, EfSql.GetNullableInt(n));

        var flag = EfSql.OutputBool("@HasPoll");
        Assert.Equal(SqlDbType.Bit, flag.SqlDbType);
        Assert.Equal(ParameterDirection.Output, flag.Direction);
        Assert.Null(EfSql.GetNullableBool(flag));
        flag.Value = DBNull.Value;
        Assert.Null(EfSql.GetNullableBool(flag));
        flag.Value = true;
        Assert.True(EfSql.GetNullableBool(flag));

        var text = EfSql.OutputString("@s", 40);
        Assert.Null(EfSql.GetNullableString(text));
        text.Value = DBNull.Value;
        Assert.Null(EfSql.GetNullableString(text));
        text.Value = "Queen";
        Assert.Equal("Queen", EfSql.GetNullableString(text));
    }

    [Fact]
    public void CreatePhotoQueries_returns_production_sql_server_shapes()
    {
        var queries = EfProductionSql.CreatePhotoQueries();
        Assert.False(queries.UseSqliteFilterExpressions);
        Assert.Contains("dbo.PIC_CAT_T", queries.CategoriesWithCountsSql, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (1)", queries.CategoriesWithCountsSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteScalarBoolSqlAsync_maps_true_false_and_null()
    {
        Assert.True(await EfSql.ExecuteScalarBoolSqlAsync(ConnectionString, "SELECT CAST(1 AS bit)"));
        Assert.False(await EfSql.ExecuteScalarBoolSqlAsync(ConnectionString, "SELECT CAST(0 AS bit)"));
        Assert.False(await EfSql.ExecuteScalarBoolSqlAsync(ConnectionString, "SELECT CAST(NULL AS bit)"));
    }

    [Fact]
    public async Task QuerySingleSqlAsync_maps_row_and_throws_when_empty()
    {
        var row = await EfSql.QuerySingleSqlAsync<ScratchRow>(
            ConnectionString,
            "SELECT 42 AS Id, CAST(NULL AS nvarchar(10)) AS Title, N'extra' AS Unmapped");
        Assert.Equal(42, row.Id);
        Assert.Null(row.Title);

        var empty = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfSql.QuerySingleSqlAsync<ScratchRow>(
                ConnectionString,
                "SELECT 1 AS Id WHERE 1 = 0"));
        Assert.Equal("Expected a single result row.", empty.Message);
    }

    [Fact]
    public async Task Context_helpers_cover_timeout_transaction_null_scalar_and_already_open()
    {
        var found = EfSql.OutputBool("@Found");
        var hit = await EfSql.QueryProcSingleOrDefaultAsync<ScratchRow>(
            dbContext,
            "dbo.EfSqlScratch_GetRow",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Id", 1));
                command.Parameters.Add(found);
            },
            commandTimeoutSeconds: 15);
        Assert.NotNull(hit);
        Assert.Equal(1, hit.Id);
        Assert.Null(hit.Title);
        Assert.True(EfSql.GetNullableBool(found));

        var missing = EfSql.OutputBool("@Found");
        Assert.Null(await EfSql.QueryProcSingleOrDefaultAsync<ScratchRow>(
            dbContext,
            "dbo.EfSqlScratch_GetRow",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Id", 2));
                command.Parameters.Add(missing);
            }));
        Assert.False(EfSql.GetNullableBool(missing));

        Assert.Equal(0, await EfSql.ExecuteScalarProcAsync(
            dbContext,
            "dbo.EfSqlScratch_Scalar",
            commandTimeoutSeconds: 15));
        Assert.Equal(0, await EfSql.ExecuteScalarSqlAsync(
            dbContext,
            "SELECT CAST(NULL AS int)",
            commandTimeoutSeconds: 15));

        await dbContext.Database.OpenConnectionAsync();
        var alreadyOpen = await EfSql.QuerySqlAsync<ScratchRow>(
            dbContext,
            "SELECT 7 AS Id, CAST(NULL AS nvarchar(10)) AS Title",
            commandTimeoutSeconds: 15);
        Assert.Equal(7, alreadyOpen.Single().Id);
        Assert.Null(alreadyOpen.Single().Title);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var enlisted = await EfSql.QuerySqlAsync<ScratchRow>(
            dbContext,
            "SELECT 9 AS Id",
            commandTimeoutSeconds: 15);
        Assert.Equal(9, enlisted.Single().Id);
        Assert.True(await EfSql.ExecuteNonQuerySqlAsync(
            dbContext,
            "SELECT 1",
            commandTimeoutSeconds: 15) >= -1);
        await transaction.CommitAsync();
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);

    private sealed class ScratchRow
    {
        public int Id { get; set; }

        public string? Title { get; set; }
    }
}
