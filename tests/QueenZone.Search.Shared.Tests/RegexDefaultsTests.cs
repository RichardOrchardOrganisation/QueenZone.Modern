using System.Text.RegularExpressions;
using QueenZone.Data;

namespace QueenZone.Search.Shared.Tests;

public sealed class RegexDefaultsTests
{
    [Fact]
    public void ApplyProcessDefault_stores_TimeSpan_not_string()
    {
        RegexDefaults.ApplyProcessDefault();

        var stored = AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT");
        Assert.IsType<TimeSpan>(stored);
        Assert.Equal(TimeSpan.FromSeconds(2), stored);
        Assert.Equal(TimeSpan.FromMilliseconds(RegexDefaults.MatchTimeoutMilliseconds), RegexDefaults.MatchTimeout);

        // Regex honours a TimeSpan timeout; a string in AppContext throws InvalidCastException.
        var regex = new Regex("a", RegexOptions.None, RegexDefaults.MatchTimeout);
        Assert.Equal(RegexDefaults.MatchTimeout, regex.MatchTimeout);
    }
}
