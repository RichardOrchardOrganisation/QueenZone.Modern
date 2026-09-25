using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class AdminQuizzesRoutesTests
{
    [Fact]
    public async Task AnonymousUserCannotAccessAdminQuizzes()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/quizzes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_publish_unpublish_and_delete_a_draft_quiz()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAdminClient();

        var list = await client.GetAsync("/admin/quizzes");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("Quizzes", listBody, StringComparison.Ordinal);
        Assert.Contains("/admin/quizzes/new", listBody, StringComparison.Ordinal);

        var created = await PostCreateAsync(client, "Admin quiz?");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var all = await quizzes.GetAllAsync();
        Assert.Single(all);
        var quizId = all[0].Id;
        Assert.False(all[0].IsPublished);
        Assert.Equal(1, all[0].QuestionCount);

        var published = await PostActionAsync(client, "Publish", quizId);
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.True((await quizzes.GetByIdAsync(quizId))!.IsPublished);

        var unpublished = await PostActionAsync(client, "Unpublish", quizId);
        Assert.Equal(HttpStatusCode.Redirect, unpublished.StatusCode);
        Assert.False((await quizzes.GetByIdAsync(quizId))!.IsPublished);

        var deleted = await PostActionAsync(client, "Delete", quizId);
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Empty(await quizzes.GetAllAsync());
    }

    [Fact]
    public async Task Create_rejects_a_question_with_no_correct_option()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAdminClient();

        var formPage = await client.GetStringAsync("/admin/quizzes/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("title", "Bad quiz"),
            new KeyValuePair<string, string>("questions[0].text", "Unanswerable?"),
            new KeyValuePair<string, string>("questions[0].points", "1"),
            new KeyValuePair<string, string>("questions[0].optionTexts", "A"),
            new KeyValuePair<string, string>("questions[0].optionTexts", "B"),
            new KeyValuePair<string, string>("questions[0].correctOptionIndex", "9"),
        ]);
        var response = await client.PostAsync("/admin/quizzes", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("exactly one option must be marked correct", body, StringComparison.Ordinal);

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        Assert.Empty(await quizzes.GetAllAsync());
    }

    [Fact]
    public async Task Admin_cannot_edit_or_delete_a_quiz_once_it_has_results()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAdminClient();
        await PostCreateAsync(client, "Locked quiz?");

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quizId = (await quizzes.GetAllAsync())[0].Id;
        await quizzes.PublishAsync(quizId);
        await quizzes.RecordAttemptAsync(quizId, Guid.NewGuid(), score: 1, correctCount: 1, questionCount: 1);

        var edit = await client.GetAsync($"/admin/quizzes/{quizId}/edit");
        var editBody = await edit.Content.ReadAsStringAsync();
        Assert.Contains("locked", editBody, StringComparison.OrdinalIgnoreCase);

        var editFormPage = await client.GetStringAsync($"/admin/quizzes/{quizId}/edit");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(editFormPage);
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("title", "Changed"),
            new KeyValuePair<string, string>("questions[0].text", "Changed?"),
            new KeyValuePair<string, string>("questions[0].points", "1"),
            new KeyValuePair<string, string>("questions[0].optionTexts", "X"),
            new KeyValuePair<string, string>("questions[0].optionTexts", "Y"),
            new KeyValuePair<string, string>("questions[0].correctOptionIndex", "0"),
        ]);
        var save = await client.PostAsync($"/admin/quizzes/{quizId}", content);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var savedBody = await save.Content.ReadAsStringAsync();
        Assert.Contains("cannot be changed", savedBody, StringComparison.OrdinalIgnoreCase);

        await PostActionAsync(client, "Delete", quizId);
        var afterDelete = await client.GetStringAsync("/admin/quizzes");
        Assert.Contains("cannot be deleted", afterDelete, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await quizzes.GetByIdAsync(quizId));
    }

    [Fact]
    public async Task AuthorizedAdminGetsNotFoundForMissingQuiz()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAdminClient();

        var response = await client.GetAsync($"/admin/quizzes/{Guid.NewGuid()}/edit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static QueenZoneWebApplicationFactory IsolatedQuizzes()
    {
        var store = new SharedQuizStore();
        return QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedQuizStore>();
            services.RemoveAll<IQuizRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IQuizRepository>(_ => new InMemoryQuizRepository(store));
        });
    }

    private static async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, string title)
    {
        var formPage = await client.GetStringAsync("/admin/quizzes/new");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("title", title),
            new KeyValuePair<string, string>("questions[0].text", "Who was the lead singer?"),
            new KeyValuePair<string, string>("questions[0].points", "1"),
            new KeyValuePair<string, string>("questions[0].optionTexts", "Freddie Mercury"),
            new KeyValuePair<string, string>("questions[0].optionTexts", "Brian May"),
            new KeyValuePair<string, string>("questions[0].correctOptionIndex", "0"),
        ]);
        return await client.PostAsync("/admin/quizzes", content);
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string handler, Guid id)
    {
        var listPage = await client.GetStringAsync("/admin/quizzes");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(listPage);
        return await client.PostAsync(
            $"/admin/quizzes?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["id"] = id.ToString(),
            }));
    }
}
