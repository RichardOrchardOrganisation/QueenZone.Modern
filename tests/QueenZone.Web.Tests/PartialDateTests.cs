using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PartialDateTests
{
    [Theory]
    [InlineData("1986", 1986, null, null, "Y", "1986", "1986")]
    [InlineData("1986-07", 1986, 7, null, "M", "1986-07", "July 1986")]
    [InlineData(" 1986-07-12 ", 1986, 7, 12, "D", "1986-07-12", "12 July 1986")]
    [InlineData("1980-02-29", 1980, 2, 29, "D", "1980-02-29", "29 February 1980")]
    public void TryParse_reads_year_month_and_day_precision(
        string text, int year, int? month, int? day, string precision, string iso, string display)
    {
        Assert.True(PartialDate.TryParse(text, out var date));

        Assert.Equal(new PartialDate(year, month, day), date);
        Assert.Equal(precision, date.Precision);
        Assert.Equal(iso, date.ToIso());
        Assert.Equal(display, date.ToDisplay());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12 July 1986")]
    [InlineData("1986-7-12")]
    [InlineData("1986-13")]
    [InlineData("1986-00-10")]
    [InlineData("1981-02-29")]
    [InlineData("1986-04-31")]
    [InlineData("1899")]
    [InlineData("2999")]
    [InlineData("1986-07-12T00:00")]
    [InlineData("1986-07-")]
    public void TryParse_rejects_anything_that_is_not_a_real_iso_date_in_range(string? text) =>
        Assert.False(PartialDate.TryParse(text, out _));

    [Fact]
    public void Database_round_trip_keeps_precision()
    {
        foreach (var iso in new[] { "1986", "1986-07", "1986-07-12" })
        {
            PartialDate.TryParse(iso, out var date);

            var restored = PartialDate.FromDatabase(date.ToDatabaseDate(), date.Precision);

            Assert.Equal(date, restored);
        }
    }

    [Fact]
    public void FromDatabase_ignores_missing_dates_and_unknown_precision()
    {
        Assert.Null(PartialDate.FromDatabase(null, "D"));
        Assert.Null(PartialDate.FromDatabase(new DateTime(1986, 7, 12), null));
        Assert.Null(PartialDate.FromDatabase(new DateTime(1986, 7, 12), "X"));
    }

    [Fact]
    public void Photo_date_accuracy_is_exact_only_to_the_day()
    {
        Assert.Equal(PhotoDateAccuracy.Unknown, PhotoArchiveDates.Unknown.PhotoDateAccuracy);
        Assert.Equal(PhotoDateAccuracy.Approximate, new PhotoArchiveDates(null, new PartialDate(1985)).PhotoDateAccuracy);
        Assert.Equal(PhotoDateAccuracy.Approximate, new PhotoArchiveDates(null, new PartialDate(1985, 7)).PhotoDateAccuracy);
        Assert.Equal(PhotoDateAccuracy.Exact, new PhotoArchiveDates(null, new PartialDate(1985, 7, 13)).PhotoDateAccuracy);
    }

    [Fact]
    public async Task InMemory_repository_defaults_to_unknown_and_swaps_only_on_expected_value()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var repository = new InMemoryPhotoArchiveDatesRepository(store);
        var event1 = new PhotoArchiveDates(new PartialDate(1986, 7, 12), null);

        Assert.Equal(PhotoArchiveDates.Unknown, await repository.GetAsync(101));
        await repository.SetAsync(101, event1, PhotoArchiveDates.Unknown);

        Assert.Equal(event1, await repository.GetAsync(101));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            () => repository.SetAsync(101, PhotoArchiveDates.Unknown, PhotoArchiveDates.Unknown));
        await repository.SetAsync(101, PhotoArchiveDates.Unknown, event1);
        Assert.Equal(PhotoArchiveDates.Unknown, await repository.GetAsync(101));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SetAsync(999_999, event1, PhotoArchiveDates.Unknown));
    }

    [Fact]
    public void Sql_server_statements_use_the_new_columns_and_compare_and_swap_on_all_of_them()
    {
        foreach (var column in new[] { "PIC_EVENT_DATE", "PIC_EVENT_PRECISION", "PIC_TAKEN_DATE", "PIC_TAKEN_PRECISION" })
        {
            Assert.Contains(column, EfPhotoArchiveDatesRepository.SelectSql);
            Assert.Contains(column, EfPhotoArchiveDatesRepository.UpdateSql);
            Assert.Contains($"ISNULL({column}", EfPhotoArchiveDatesRepository.UpdateSql);
        }

        Assert.Contains("WHERE PIC_ID = @PicId", EfPhotoArchiveDatesRepository.SelectSql);
        Assert.Contains("@ExpectedEventDate", EfPhotoArchiveDatesRepository.UpdateSql);
        Assert.Contains("@ExpectedTakenPrecision", EfPhotoArchiveDatesRepository.UpdateSql);
    }
}
