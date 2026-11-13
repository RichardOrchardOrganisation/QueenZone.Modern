using QueenZone.Data;

namespace QueenZone.Web.Tests;

public abstract class AdminFanPerformanceRepositoryContractTests
{
    protected abstract IAdminFanPerformanceRepository Repository { get; }
    protected abstract IFanPerformanceRepository PublicRepository { get; }
    protected static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0);
    private const string Editor = "editor@example.test";

    private Task<int> Create(string title = "Title", bool visible = true) => Repository.CreateAsync(
        new(title, "Band", "Description", "song.mp3", 1024, Now, visible, 120), Editor);

    [Fact]
    public async Task Writes_RoundTrip_AndHideFromPublicReads()
    {
        var id = await Create();
        var row = await Repository.GetByIdAsync(id);
        Assert.NotNull(row);
        Assert.Equal("Title", row.Title);
        Assert.Equal("Band", row.PerformedBy);
        Assert.Equal("Description", row.Description);
        Assert.Equal("song.mp3", row.AudioFileName);
        Assert.Equal(1024, row.FileSizeBytes);
        Assert.Equal(120, row.DurationSeconds);
        Assert.Equal(Now, row.DateAdded);
        Assert.True(row.IsVisible);
        Assert.Equal(id, (await PublicRepository.GetByIdAsync(id))!.Id);
        await Repository.UpdateAsync(id, new("Edited", "Other band", "Updated", Now.AddDays(1)), Editor, row.ToConcurrencyToken());
        row = await Repository.GetByIdAsync(id);
        Assert.Equal("Edited", row!.Title);
        Assert.Equal("Other band", row.PerformedBy);
        Assert.Equal("Updated", row.Description);
        Assert.Equal(Now.AddDays(1), row.DateAdded);
        await Repository.SetVisibilityAsync(id, false, Editor, true);
        Assert.False((await Repository.GetByIdAsync(id))!.IsVisible);
        Assert.Null(await PublicRepository.GetByIdAsync(id));
        Assert.Empty(await PublicRepository.GetPageAsync(1, 10));
        Assert.Equal(0, await PublicRepository.GetVisibleCountAsync());
        await Repository.DeleteAsync(id, Editor);
        Assert.Null(await Repository.GetByIdAsync(id));
        await Repository.DeleteAsync(id, Editor);
    }

    [Fact]
    public async Task StaleTokens_RejectSecondWriter_AndFreshTokensSucceed()
    {
        var id = await Create();
        var token = (await Repository.GetByIdAsync(id))!.ToConcurrencyToken();
        await Repository.UpdateAsync(id, new("First writer", "Band", "Description", Now), Editor, token);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.UpdateAsync(id, new("Stale writer", "Band", "Description", Now), Editor, token));
        var row = (await Repository.GetByIdAsync(id))!;
        Assert.Equal("First writer", row.Title);
        await Repository.UpdateAsync(id, new("Fresh writer", "Band", "Description", Now), Editor, row.ToConcurrencyToken());
        Assert.Equal("Fresh writer", (await Repository.GetByIdAsync(id))!.Title);
        await Repository.SetVisibilityAsync(id, false, Editor, true);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => Repository.SetVisibilityAsync(id, true, Editor, true));
        Assert.False((await Repository.GetByIdAsync(id))!.IsVisible);
        await Repository.SetVisibilityAsync(id, true, Editor, false);
        Assert.True((await Repository.GetByIdAsync(id))!.IsVisible);
    }

    [Fact]
    public async Task AdminList_IncludesHidden_FiltersAndClamps()
    {
        var visible = await Create("Visible");
        var hidden = await Create("Hidden", false);
        var all = await Repository.GetPageAsync(new(), 0, 999);
        Assert.Equal(1, all.Page);
        Assert.Equal(200, all.PageSize);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(new[] { hidden, visible }, all.Items.Select(row => row.Id));
        Assert.Equal(visible, Assert.Single((await Repository.GetPageAsync(new(true), 1, 10)).Items).Id);
        Assert.Equal(hidden, Assert.Single((await Repository.GetPageAsync(new(Search: "Hidden"), 1, 10)).Items).Id);
        Assert.Equal(1, (await Repository.GetPageAsync(new(), -1, 0)).PageSize);
        Assert.Equal(visible, Assert.Single((await Repository.GetPageAsync(new(), 2, 1)).Items).Id);
    }

    [Fact]
    public async Task MissingReads_ReturnNull_AndMissingUpdatesThrow()
    {
        Assert.Null(await Repository.GetByIdAsync(404));
        Assert.Equal("Fan performance 404 was not found.", (await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.UpdateAsync(404, new("Title", "Band", "Description", Now), Editor))).Message);
        Assert.Equal("Fan performance 404 was not found.", (await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.SetVisibilityAsync(404, false, Editor))).Message);
    }
}

public sealed class InMemoryAdminFanPerformanceRepositoryContractTests : AdminFanPerformanceRepositoryContractTests
{
    private readonly SharedFanPerformanceStore store = new();
    protected override IAdminFanPerformanceRepository Repository => new InMemoryAdminFanPerformanceRepository(store);
    protected override IFanPerformanceRepository PublicRepository => new InMemoryFanPerformanceRepository(store);
}
