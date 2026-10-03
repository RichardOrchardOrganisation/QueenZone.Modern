using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class HomePresentationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, "0 new forum replies today")]
    [InlineData(1, "1 new forum reply today")]
    [InlineData(2, "2 new forum replies today")]
    public void ForumRepliesToday_PreservesAbsentCountAndPluralisation(int? count, string? expected) =>
        Assert.Equal(expected, HomePresentation.ForumRepliesToday(count));

    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "")]
    [InlineData(1, " 1 member has played today.")]
    [InlineData(2, " 2 members have played today.")]
    public void SprintPlayersNote_PreservesEmptyAndSingularOrPluralCopy(int players, string expected) =>
        Assert.Equal(expected, HomePresentation.SprintPlayersNote(players));

    [Fact]
    public void Counts_KeepTheCurrentCultureNumberFormat()
    {
        Assert.Equal($"{1234:N0} new forum replies today", HomePresentation.ForumRepliesToday(1234));
        Assert.Equal($" {1234:N0} members have played today.", HomePresentation.SprintPlayersNote(1234));
    }
}
