using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminRelativeTimeTests
{
    [Theory]
    [InlineData(-1, "just now")]
    [InlineData(0, "just now")]
    [InlineData(1, "just now")]
    [InlineData(2, "2 mins ago")]
    [InlineData(59, "59 mins ago")]
    [InlineData(60, "1 hour ago")]
    [InlineData(119, "1 hour ago")]
    [InlineData(120, "2 hours ago")]
    [InlineData(1439, "23 hours ago")]
    [InlineData(1440, "yesterday")]
    [InlineData(2879, "yesterday")]
    [InlineData(2880, "2 days ago")]
    [InlineData(4320, "3 days ago")]
    public void AdminFormatting_PreservesBoundaryWordingWithoutReadingTheClock(int minutes, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, HomeRelativeTime.FormatAdmin(now.UtcDateTime.AddMinutes(-minutes), now));
    }

    [Fact]
    public void AdminFormatting_TreatsDatabaseUnspecifiedTimestampsAsUtc()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var databaseTime = DateTime.SpecifyKind(now.UtcDateTime.AddHours(-2), DateTimeKind.Unspecified);
        Assert.Equal("2 hours ago", HomeRelativeTime.FormatAdmin(databaseTime, now));
    }
}
