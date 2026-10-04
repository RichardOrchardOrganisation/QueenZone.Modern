using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public sealed class CrosswordReplayTests : E2EPageTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Completed_solver_can_replay_without_changing_the_saved_attempt(bool member)
    {
        if (member) await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Member-Id"] = Guid.NewGuid().ToString() });
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        var letters = string.Concat(CrosswordSampleData.Load().Single(puzzle => puzzle.Slug == "meet-the-band").Grid.Rows);
        await FillGrid(letters);
        await Expect(Page.Locator("[data-completion]")).ToBeVisibleAsync();
        var saved = await OriginalLocal();
        var remote = member ? await Page.EvaluateAsync<string>("async () => { const c=JSON.parse(document.querySelector('[data-puzzle]').textContent); return await (await fetch(location.pathname+'?handler=Progress',{headers:{'X-Crossword-Member':c.memberId}})).text(); }") : null;
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Play again", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-practice]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-timer]")).ToHaveTextAsync("0:00");
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("");
        await Page.Locator("[data-cell='0']").ClickAsync(); await Page.Keyboard.TypeAsync("D");
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-practice]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("D");
        var rankedWrites = 0;
        Page.Request += (_, request) => { if (request.Url.Contains("handler=Complete") || request.Url.Contains("handler=Save")) rankedWrites++; };
        await FillGrid(letters);
        await Expect(Page.Locator("[data-completion]")).ToBeVisibleAsync();
        Assert.That(rankedWrites, Is.Zero, "Practice must not write the ranked attempt.");
        Assert.That(await OriginalLocal(), Is.EqualTo(saved));
        if (member) Assert.That(await Page.EvaluateAsync<string>("async () => { const c=JSON.parse(document.querySelector('[data-puzzle]').textContent); return await (await fetch(location.pathname+'?handler=Progress',{headers:{'X-Crossword-Member':c.memberId}})).text(); }"), Is.EqualTo(remote));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Resume saved attempt" }).ClickAsync();
        await Expect(Page.Locator("[data-practice]")).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-completion]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task In_progress_reset_is_confirmed_and_persists_offline_as_practice()
    {
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("navigator.serviceWorker.controller !== null");
        await Page.ReloadAsync();
        await Page.WaitForFunctionAsync("async () => (await caches.match(location.href))?.headers.get('X-QueenZone-Crossword-Shell') === 'public'");
        await Page.Locator("[data-cell='0']").ClickAsync(); await Page.Keyboard.TypeAsync("Z");
        EventHandler<IDialog> cancel = async (_, dialog) => await dialog.DismissAsync(); Page.Dialog += cancel;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset current attempt" }).ClickAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("Z"); Page.Dialog -= cancel;
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Context.SetOfflineAsync(true);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset current attempt" }).ClickAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("");
        await Page.Locator("[data-cell='0']").ClickAsync(); await Page.Keyboard.TypeAsync("D");
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-practice]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("D");
        await Context.SetOfflineAsync(false);
        await Expect(Page.Locator("[data-practice]")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Resume saved attempt" }).ClickAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("Z");
    }
    private Task<string> OriginalLocal() => Page.EvaluateAsync<string>("() => { const c=JSON.parse(document.querySelector('[data-puzzle]').textContent); return localStorage.getItem('qz:crossword:v1:'+encodeURIComponent(c.puzzle.id)+':'+(c.memberId?'member:'+encodeURIComponent(c.memberId):'guest')); }");
    private async Task FillGrid(string letters)
    {
        for (var cell = 0; cell < letters.Length; cell++)
        {
            if (letters[cell] == '#') continue;
            await Page.Locator($"[data-cell='{cell}']").ClickAsync();
            await Page.Locator("[data-input]").FillAsync(letters[cell].ToString());
        }
    }
}
