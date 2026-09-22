using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PhotoListFilterTests
{
    [Theory]
    [InlineData(null, PhotoSizePreset.None)]
    [InlineData("", PhotoSizePreset.None)]
    [InlineData("desktop", PhotoSizePreset.Desktop)]
    [InlineData("PHONE", PhotoSizePreset.Phone)]
    [InlineData("hd", PhotoSizePreset.Hd)]
    [InlineData("unknown", PhotoSizePreset.None)]
    public void Parse_MapsQueryValues(string? input, PhotoSizePreset expected)
    {
        Assert.Equal(expected, PhotoListFilter.Parse(input).Size);
    }

    [Fact]
    public void Matches_NeverTreatsZeroAsUsableForActivePresets()
    {
        var desktop = new PhotoListFilter(PhotoSizePreset.Desktop);
        Assert.False(desktop.Matches(0, 0));
        Assert.False(desktop.Matches(1920, 0));
        Assert.True(desktop.Matches(1920, 1080));
        Assert.False(desktop.Matches(1080, 1920));
    }

    [Fact]
    public void Matches_PhoneAndLargeAndHd()
    {
        Assert.True(new PhotoListFilter(PhotoSizePreset.Phone).Matches(1080, 1920));
        Assert.False(new PhotoListFilter(PhotoSizePreset.Phone).Matches(1920, 1080));
        Assert.True(new PhotoListFilter(PhotoSizePreset.Large).Matches(2560, 1440));
        Assert.True(new PhotoListFilter(PhotoSizePreset.Hd).Matches(1280, 720));
        Assert.False(new PhotoListFilter(PhotoSizePreset.Hd).Matches(1024, 768));
    }

    [Fact]
    public void ToSqlServerAndClause_EmptyWhenInactive()
    {
        Assert.Equal(string.Empty, PhotoListFilter.None.ToSqlServerAndClause("p"));
        var desktop = new PhotoListFilter(PhotoSizePreset.Desktop).ToSqlServerAndClause("p");
        Assert.Contains("p.PIC_WIDTH >= 1920", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("ISNULL(", desktop, StringComparison.Ordinal);
        Assert.StartsWith(" AND ", new PhotoListFilter(PhotoSizePreset.Landscape).ToSqlServerAndClause("p"), StringComparison.Ordinal);
        var hd = new PhotoListFilter(PhotoSizePreset.Hd).ToSqlServerAndClause("t");
        Assert.Contains("t.PIC_LONGEST_SIDE >= 1280", hd, StringComparison.Ordinal);
        Assert.DoesNotContain("CASE", hd, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(", hd, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoSqlFilter_ReplacesPlaceholders()
    {
        const string sql = "WHERE p.DISPLAY = 1{PHOTO_FILTER_P} AND t.x = 1{PHOTO_FILTER_T}";
        var applied = PhotoSqlFilter.ApplyProduction(sql, new PhotoListFilter(PhotoSizePreset.Desktop));
        Assert.DoesNotContain("{PHOTO_FILTER_P}", applied, StringComparison.Ordinal);
        Assert.Contains("PIC_WIDTH", applied, StringComparison.Ordinal);
        Assert.Contains("1920", applied, StringComparison.Ordinal);
    }
}
