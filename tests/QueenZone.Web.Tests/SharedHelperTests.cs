using QueenZone.Data;
using Xunit;

namespace QueenZone.Web.Tests;

public sealed class SharedHelperTests
{
    [Fact]
    public void LegacyDateTime_ToOffset_PinsUnspecifiedKindToUtc()
    {
        var value = new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = LegacyDateTime.ToOffset(value);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(new DateTime(2020, 5, 1, 12, 0, 0, DateTimeKind.Utc), result.UtcDateTime);
    }

    [Fact]
    public void LegacyDateTime_ToOffset_NullBecomesMinValue()
    {
        Assert.Equal(DateTimeOffset.MinValue, LegacyDateTime.ToOffset(null));
    }

    [Fact]
    public void EnsureRowVersion_SkipsCheckWhenNoExpectedVersionSupplied()
    {
        Assert.Null(Record.Exception(() => QueenZoneConcurrency.EnsureRowVersion([1], null)));
    }

    [Fact]
    public void EnsureRowVersion_ThrowsOnMismatchAndAcceptsMatch()
    {
        QueenZoneConcurrency.EnsureRowVersion([1, 2], [1, 2]);
        Assert.Throws<OptimisticConcurrencyException>(() => QueenZoneConcurrency.EnsureRowVersion([1, 2], [3]));
        Assert.Throws<OptimisticConcurrencyException>(() => QueenZoneConcurrency.EnsureRowVersion(null, [3]));
    }

    [Fact]
    public void EnsureRequiredRowVersion_ThrowsWhenExpectedMissingOrMismatched()
    {
        QueenZoneConcurrency.EnsureRequiredRowVersion<InvalidOperationException>([1, 2], [1, 2]);
        Assert.Throws<InvalidOperationException>(() =>
            QueenZoneConcurrency.EnsureRequiredRowVersion<InvalidOperationException>([1, 2], null));
        Assert.Throws<InvalidOperationException>(() =>
            QueenZoneConcurrency.EnsureRequiredRowVersion<InvalidOperationException>([1, 2], [9]));
        Assert.Throws<InvalidOperationException>(() =>
            QueenZoneConcurrency.EnsureRequiredRowVersion<InvalidOperationException>(null, [9]));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("  Hard ", "hard")]
    public void TriviaValidation_NormalizeDifficulty_TrimsAndLowercases(string? input, string? expected)
    {
        Assert.Equal(expected, TriviaValidation.NormalizeDifficulty(input));
    }

    [Fact]
    public void TriviaValidation_NormalizeDifficulty_OnlyTruncatesWhenGivenALimit()
    {
        var overLong = new string('A', TriviaValidation.MaxDifficultyLength + 5);

        Assert.Equal(overLong.ToLowerInvariant(), TriviaValidation.NormalizeDifficulty(overLong));
        Assert.Equal(
            new string('a', TriviaValidation.MaxDifficultyLength),
            TriviaValidation.NormalizeDifficulty(overLong, TriviaValidation.MaxDifficultyLength));
    }

    [Fact]
    public void TriviaValidation_NormalizeOptional_TrimsOrReturnsNull()
    {
        Assert.Null(TriviaValidation.NormalizeOptional(" \t"));
        Assert.Equal("x", TriviaValidation.NormalizeOptional(" x "));
    }

    [Fact]
    public void HtmlTags_Pattern_StripsTagsOnly()
    {
        Assert.Equal("a b", HtmlTags.Pattern().Replace("a<b>b</b>", " ").Trim().Replace("  ", " "));
        Assert.DoesNotMatch(HtmlTags.Pattern(), "1 < 2 no closing");
    }
}
