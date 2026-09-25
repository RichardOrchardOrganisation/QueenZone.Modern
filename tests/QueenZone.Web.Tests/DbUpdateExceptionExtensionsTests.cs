using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class DbUpdateExceptionExtensionsTests
{
    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void Sql_server_unique_error_numbers_are_unique_violations(int number)
    {
        var sql = SiteSearchSqlTimeoutTests.CreateSqlException(number, "Violation");
        Assert.True(new DbUpdateException("conflict", sql).IsUniqueConstraintViolation());
    }

    [Fact]
    public void Other_sql_server_errors_are_not_unique_violations()
    {
        var sql = SiteSearchSqlTimeoutTests.CreateSqlException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint");
        Assert.False(new DbUpdateException("conflict", sql).IsUniqueConstraintViolation());
    }

    [Theory]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: Votes.PollId, Votes.MemberId'.")]
    [InlineData("Cannot insert duplicate key row in object 'dbo.Votes' with unique index 'IX_Votes'.")]
    [InlineData("Duplicate key value violates constraint")]
    public void Provider_unique_messages_are_unique_violations(string message) =>
        Assert.True(new DbUpdateException("conflict", new InvalidOperationException(message)).IsUniqueConstraintViolation());

    [Fact]
    public void Violation_nested_deeper_in_the_inner_chain_is_found()
    {
        var nested = new InvalidOperationException(
            "wrapper",
            SiteSearchSqlTimeoutTests.CreateSqlException(2627, "Violation of UNIQUE KEY constraint"));
        Assert.True(new DbUpdateException("conflict", nested).IsUniqueConstraintViolation());
    }

    [Fact]
    public void Exception_without_a_unique_inner_is_not_a_violation()
    {
        Assert.False(new DbUpdateException("conflict").IsUniqueConstraintViolation());
        Assert.False(new DbUpdateException("conflict", new TimeoutException("timeout")).IsUniqueConstraintViolation());
    }
}
