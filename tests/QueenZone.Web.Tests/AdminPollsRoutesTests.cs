using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminPollsRoutesTests
{
    [Fact]
    public async Task AnonymousUserCannotAccessAdminPolls()
    {
        using var isolated = IsolatedHomePolls();
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/polls");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_publish_close_hide_and_delete_a_draft()
    {
        using var isolated = IsolatedHomePolls();
        using var client = isolated.CreateAdminClient();

        var list = await client.GetAsync("/admin/polls");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("Home polls", listBody, StringComparison.Ordinal);
        Assert.Contains("/admin/polls/new", listBody, StringComparison.Ordinal);

        var createdOk = await PostCreateAsync(client, "Admin poll?", "One", "Two");
        Assert.Equal(HttpStatusCode.Redirect, createdOk.StatusCode);

        using var scope = isolated.Services.CreateScope();
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
        using var isolated = IsolatedHomePolls();
        using var client = isolated.CreateAdminClient();

        await PostCreateAsync(client, "First poll?", "A", "B");
        await PostCreateAsync(client, "Second poll?", "C", "D");

        using var scope = isolated.Services.CreateScope();
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
        var store = new SharedHomePollStore();
        var inner = new InMemoryHomePollRepository(store);
        using var isolated = QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedHomePollStore>();
            services.RemoveAll<IHomePollRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IHomePollRepository>(
                new ThrowingPublishHomePollRepository(inner, CreateUniqueConstraintException()));
        });
        using var client = isolated.CreateAdminClient();

        await PostCreateAsync(client, "Draft?", "Yes", "No");
        using var scope = isolated.Services.CreateScope();
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
        using var isolated = IsolatedHomePolls();
        using var client = isolated.CreateAdminClient();
        await PostCreateAsync(client, "Locked?", "Yes", "No");

        using var scope = isolated.Services.CreateScope();
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
        using var isolated = IsolatedHomePolls();
        using var client = isolated.CreateAdminClient();

        var response = await client.GetAsync($"/admin/polls/{Guid.NewGuid()}/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static QueenZoneWebApplicationFactory IsolatedHomePolls()
    {
        var store = new SharedHomePollStore();
        return QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedHomePollStore>();
            services.RemoveAll<IHomePollRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IHomePollRepository>(_ => new InMemoryHomePollRepository(store));
        });
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

    private static DbUpdateException CreateUniqueConstraintException() =>
        new(
            "Cannot insert duplicate key row in object 'dbo.HomePolls' with unique index 'UX_HomePolls_IsCurrent'. The duplicate key value is (1).",
            SqlExceptionFactory.Create(
                2601,
                "Cannot insert duplicate key row in object 'dbo.HomePolls' with unique index 'UX_HomePolls_IsCurrent'. The duplicate key value is (1)."));

    private sealed class ThrowingPublishHomePollRepository(
        IHomePollRepository inner,
        Exception exception) : IHomePollRepository
    {
        public Task<HomePollResults?> GetCurrentAsync(
            Guid? viewerMemberId,
            CancellationToken cancellationToken = default) =>
            inner.GetCurrentAsync(viewerMemberId, cancellationToken);

        public Task<IReadOnlyList<HomePollAdminItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            inner.GetAllAsync(cancellationToken);

        public Task<HomePollAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<Guid> CreateAsync(
            AdminHomePollDraft draft,
            Guid createdByMemberId,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(draft, createdByMemberId, cancellationToken);

        public Task UpdateAsync(Guid id, AdminHomePollDraft draft, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(id, draft, cancellationToken);

        public Task PublishAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task CloseAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.CloseAsync(id, cancellationToken);

        public Task HideAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.HideAsync(id, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task CastVoteAsync(Guid optionId, Guid memberId, CancellationToken cancellationToken = default) =>
            inner.CastVoteAsync(optionId, memberId, cancellationToken);
    }
}
