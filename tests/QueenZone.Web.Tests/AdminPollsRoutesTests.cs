using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminPollsRoutesTests : IClassFixture<WebHostVariantCache>, IAsyncLifetime
{
    private readonly VariantWebApplicationFactory factory;
    private readonly VariantWebApplicationFactory throwingPublish;

    public AdminPollsRoutesTests(WebHostVariantCache variants)
    {
        factory = variants.Get(WebHostVariants.IsolatedHomePolls);
        throwingPublish = variants.Get(WebHostVariants.IsolatedHomePollsThrowingPublish);
    }

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AnonymousUserCannotAccessAdminPolls()
    {
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/polls");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_publish_close_hide_and_delete_a_draft()
    {
        using var client = factory.CreateAdminClient();

        var list = await client.GetAsync("/admin/polls");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("Home polls", listBody, StringComparison.Ordinal);
        Assert.Contains("/admin/polls/new", listBody, StringComparison.Ordinal);

        var createdOk = await PostCreateAsync(client, "Admin poll?", "One", "Two");
        Assert.Equal(HttpStatusCode.Redirect, createdOk.StatusCode);

        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        var all = await polls.GetAllAsync();
        Assert.Single(all);
        var pollId = all[0].Id;
        Assert.False(all[0].IsCurrent);

        var published = await PostActionAsync(client, "Publish", pollId);
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Equal(pollId, (await polls.GetCurrentAsync(null))!.PollId);

        var closed = await PostActionAsync(client, "Close", pollId);
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        Assert.True((await polls.GetCurrentAsync(null))!.IsClosed);

        var hidden = await PostActionAsync(client, "Hide", pollId);
        Assert.Equal(HttpStatusCode.Redirect, hidden.StatusCode);
        Assert.Null(await polls.GetCurrentAsync(null));

        var deleted = await PostActionAsync(client, "Delete", pollId);
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Empty(await polls.GetAllAsync());
    }

    [Fact]
    public async Task Admin_publishing_a_second_poll_makes_it_the_only_current()
    {
        using var client = factory.CreateAdminClient();

        await PostCreateAsync(client, "First poll?", "A", "B");
        await PostCreateAsync(client, "Second poll?", "C", "D");

        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        var all = await polls.GetAllAsync();
        var first = all.Single(item => item.Question == "First poll?");
        var second = all.Single(item => item.Question == "Second poll?");

        var publishedFirst = await PostActionAsync(client, "Publish", first.Id);
        Assert.Equal(HttpStatusCode.Redirect, publishedFirst.StatusCode);
        Assert.Equal(first.Id, (await polls.GetCurrentAsync(null))!.PollId);

        var publishedSecond = await PostActionAsync(client, "Publish", second.Id);
        Assert.Equal(HttpStatusCode.Redirect, publishedSecond.StatusCode);
        Assert.Equal("/admin/polls", publishedSecond.Headers.Location!.OriginalString);
        Assert.DoesNotContain("/error/", publishedSecond.Headers.Location.OriginalString, StringComparison.Ordinal);

        var current = await polls.GetCurrentAsync(null);
        Assert.Equal(second.Id, current!.PollId);
        Assert.False((await polls.GetByIdAsync(first.Id))!.IsCurrent);
        Assert.True((await polls.GetByIdAsync(second.Id))!.IsCurrent);

        var page = await client.GetStringAsync("/admin/polls");
        Assert.Contains("Published poll. It is now the Home poll.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Page Not Found", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_DbUpdateException_redirects_with_tempdata_error_not_404()
    {
        await throwingPublish.ResetAsync();
        using var client = throwingPublish.CreateAdminClient();

        await PostCreateAsync(client, "Draft?", "Yes", "No");
        using var scope = throwingPublish.Services.CreateScope();
        var pollId = (await scope.ServiceProvider.GetRequiredService<IHomePollRepository>().GetAllAsync())[0].Id;
        var published = await PostActionAsync(client, "Publish", pollId);
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Equal("/admin/polls", published.Headers.Location!.OriginalString);
        Assert.DoesNotContain("/error/", published.Headers.Location.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("404", published.Headers.Location.OriginalString, StringComparison.Ordinal);

        var page = await client.GetStringAsync("/admin/polls");
        Assert.Contains(AdminHomePollPublishError.Message, page, StringComparison.Ordinal);
        Assert.DoesNotContain("Page Not Found", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_cannot_edit_or_delete_after_the_first_vote()
    {
        using var client = factory.CreateAdminClient();
        await PostCreateAsync(client, "Locked?", "Yes", "No");

        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        var pollId = (await polls.GetAllAsync())[0].Id;
        await polls.PublishAsync(pollId);
        var current = await polls.GetCurrentAsync(null);
        await polls.CastVoteAsync(current!.Options[0].OptionId, Guid.NewGuid());

        var edit = await client.GetAsync($"/admin/polls/{pollId}/edit");
        var editBody = await edit.Content.ReadAsStringAsync();
        Assert.Contains("locked", editBody, StringComparison.OrdinalIgnoreCase);

        var save = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            $"/admin/polls/{pollId}/edit",
            $"/admin/polls/{pollId}",
            new Dictionary<string, string>
            {
                ["question"] = "Changed?",
                ["optionTexts"] = "X",
            });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var savedBody = await save.Content.ReadAsStringAsync();
        Assert.Contains("cannot be changed", savedBody, StringComparison.OrdinalIgnoreCase);

        var deleted = await PostActionAsync(client, "Delete", pollId);
        var afterDelete = await client.GetStringAsync("/admin/polls");
        Assert.Contains("cannot be deleted", afterDelete, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await polls.GetByIdAsync(pollId));
        Assert.Equal("Locked?", (await polls.GetByIdAsync(pollId))!.Question);
    }

    [Fact]
    public async Task AuthorizedAdminGetsNotFoundForMissingPoll()
    {
        using var client = factory.CreateAdminClient();

        var response = await client.GetAsync($"/admin/polls/{Guid.NewGuid()}/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostCreateAsync(
        HttpClient client,
        string question,
        string option1,
        string option2)
    {
        var formPage = await client.GetStringAsync("/admin/polls/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("question", question),
            new KeyValuePair<string, string>("optionTexts", option1),
            new KeyValuePair<string, string>("optionTexts", option2),
        ]);
        return await client.PostAsync("/admin/polls", content);
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string handler, Guid id)
    {
        var listPage = await client.GetStringAsync("/admin/polls");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage);
        return await client.PostAsync(
            $"/admin/polls?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["id"] = id.ToString(),
            }));
    }
}
