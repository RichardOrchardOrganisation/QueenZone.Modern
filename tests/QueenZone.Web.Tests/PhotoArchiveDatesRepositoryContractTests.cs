using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class PhotoArchiveDatesRepositoryContractTests
{
    protected abstract IPhotoArchiveDatesRepository Repository { get; }
    protected abstract Task<int> SeedPhotoAsync();

    [Fact]
    public async Task Dates_compare_and_swap_preserves_first_write_and_accepts_fresh_expected()
    {
        var id = await SeedPhotoAsync();
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(id));
        var first = new PhotoArchiveDates(new PartialDate(1986, 7, 12), new PartialDate(1986, 7));
        await Repository.SetAsync(id, first, PhotoArchiveDates.Unknown);
        Assert.Equal(first, await Repository.GetAsync(id));
        var second = new PhotoArchiveDates(new PartialDate(1985), new PartialDate(1985, 7, 13));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.SetAsync(id, second, PhotoArchiveDates.Unknown));
        Assert.Equal(first, await Repository.GetAsync(id));
        await Repository.SetAsync(id, second, await Repository.GetAsync(id));
        Assert.Equal(second, await Repository.GetAsync(id));
        await Repository.SetAsync(id, PhotoArchiveDates.Unknown, second);
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(id));
    }

    [Fact]
    public async Task Missing_photo_reads_unknown_and_cannot_be_written()
    {
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(999));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SetAsync(999,
            new(new PartialDate(1986), null), PhotoArchiveDates.Unknown));
        Assert.Equal("Photo 999 was not found.", error.Message);
        Assert.Equal(PhotoArchiveDates.Unknown, await Repository.GetAsync(999));
    }
}

public sealed class InMemoryPhotoArchiveDatesRepositoryContractTests : PhotoArchiveDatesRepositoryContractTests
{
    private readonly SharedPhotoStore store = new([new(9, "Brian May", [])]);
    protected override IPhotoArchiveDatesRepository Repository { get; }
    public InMemoryPhotoArchiveDatesRepositoryContractTests() => Repository = new InMemoryPhotoArchiveDatesRepository(store);
    protected override Task<int> SeedPhotoAsync() => new InMemoryAdminPhotoRepository(store).CreateAsync(
        new(9, "Wembley", null, 1986, new DateTime(1986, 7, 12), true, "photo.jpg", "thumb.jpg", 150, 100, 1920, 1080), "editor@example.test");
}
