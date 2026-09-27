using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class SubmissionInputTests
{
    [Theory]
    [InlineData(null, 10, null)]
    [InlineData("", 10, null)]
    [InlineData("   ", 10, null)]
    [InlineData("  Queen  ", 10, "Queen")]
    [InlineData("  Bohemian Rhapsody  ", 8, "Bohemian")]
    [InlineData("exact", 5, "exact")]
    public void NormalizeOptional_trims_truncates_and_nulls_blank_input(string? value, int maxLength, string? expected) =>
        Assert.Equal(expected, SubmissionInput.NormalizeOptional(value, maxLength));

    [Fact]
    public void IdOrNew_keeps_a_supplied_id()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, SubmissionInput.IdOrNew(id));
    }

    [Fact]
    public void IdOrNew_generates_an_id_when_missing_or_empty()
    {
        Assert.NotEqual(Guid.Empty, SubmissionInput.IdOrNew(null));
        Assert.NotEqual(Guid.Empty, SubmissionInput.IdOrNew(Guid.Empty));
    }
}
