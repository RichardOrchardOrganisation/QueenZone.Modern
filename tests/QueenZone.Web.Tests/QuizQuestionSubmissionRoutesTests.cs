using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed partial class QuizQuestionSubmissionRoutesTests
{
    [Fact]
    public async Task Anonymous_visitor_is_redirected_to_login_for_the_submission_form()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/submit/quiz-question");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_can_submit_a_question_and_see_the_confirmation()
    {
        using var isolated = IsolatedQuizzes();
        using var member = MemberClient(isolated, "Question Fan");

        var formPage = await member.GetStringAsync("/submit/quiz-question");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        var response = await member.PostAsync(
            "/submit/quiz-question",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("QuestionText", "Who played guitar for Queen?"),
                new KeyValuePair<string, string>("OptionTexts", "Brian May"),
                new KeyValuePair<string, string>("OptionTexts", "John Deacon"),
                new KeyValuePair<string, string>("CorrectOptionIndex", "0"),
                new KeyValuePair<string, string>("SourceNote", "Common knowledge"),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var confirmationPath = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/submit/quiz-question/confirmation/", confirmationPath, StringComparison.Ordinal);

        var confirmation = await member.GetStringAsync(confirmationPath);
        Assert.Contains("Your quiz question is under review.", confirmation, StringComparison.Ordinal);
        Assert.Contains("Who played guitar for Queen?", confirmation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submitting_with_no_correct_option_marked_shows_a_validation_error()
    {
        using var isolated = IsolatedQuizzes();
        using var member = MemberClient(isolated, "Bad Submitter");

        var formPage = await member.GetStringAsync("/submit/quiz-question");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        var response = await member.PostAsync(
            "/submit/quiz-question",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("QuestionText", "An unanswerable question?"),
                new KeyValuePair<string, string>("OptionTexts", "A"),
                new KeyValuePair<string, string>("OptionTexts", "B"),
                new KeyValuePair<string, string>("CorrectOptionIndex", "9"),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Mark exactly one option as the correct answer.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousUserCannotAccessTheAdminQueue()
    {
        using var isolated = IsolatedQuizzes();
        using var client = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/admin/quiz-question-submissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_approve_with_edits_reject_with_reason_and_the_question_bank_reflects_it()
    {
        using var isolated = IsolatedQuizzes();
        var submissionId = await SubmitQuestionAsync(isolated, "Original wording?");
        var toRejectId = await SubmitQuestionAsync(isolated, "Reject this one?");

        using var admin = isolated.CreateAdminClient();
        var queue = await admin.GetStringAsync("/admin/quiz-question-submissions");
        Assert.Contains("Original wording?", queue, StringComparison.Ordinal);

        var detail = await admin.GetStringAsync($"/admin/quiz-question-submissions/{submissionId}");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(detail);
        var approve = await admin.PostAsync(
            $"/admin/quiz-question-submissions/{submissionId}/approve",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("questionText", "Edited wording?"),
                new KeyValuePair<string, string>("optionTexts", "Freddie Mercury"),
                new KeyValuePair<string, string>("optionTexts", "Brian May"),
                new KeyValuePair<string, string>("correctOptionIndex", "0"),
                new KeyValuePair<string, string>("reviewNotes", "fixed typo"),
            ]));
        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);

        var rejectDetail = await admin.GetStringAsync($"/admin/quiz-question-submissions/{toRejectId}");
        var rejectToken = AdminHttpTestHelpers.ExtractAntiforgeryToken(rejectDetail);
        var reject = await admin.PostAsync(
            $"/admin/quiz-question-submissions/{toRejectId}/reject",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", rejectToken),
                new KeyValuePair<string, string>("rejectionReason", "Too easy."),
            ]));
        Assert.Equal(HttpStatusCode.Redirect, reject.StatusCode);

        using var scope = isolated.Services.CreateScope();
        var submissions = scope.ServiceProvider.GetRequiredService<IQuizQuestionSubmissionRepository>();
        var approved = await submissions.GetByIdAsync(submissionId);
        var rejected = await submissions.GetByIdAsync(toRejectId);
        Assert.Equal(QuizQuestionSubmissionStatus.Approved, approved!.Status);
        Assert.Equal("Edited wording?", approved.QuestionText);
        Assert.Equal(QuizQuestionSubmissionStatus.Rejected, rejected!.Status);
        Assert.Equal("Too easy.", rejected.RejectionReason);

        var bank = await submissions.GetApprovedAndAvailableAsync();
        Assert.Contains(bank, item => item.Id == submissionId);
        Assert.DoesNotContain(bank, item => item.Id == toRejectId);
    }

    [Fact]
    public async Task Admin_can_add_an_approved_question_from_the_bank_into_an_existing_quiz()
    {
        using var isolated = IsolatedQuizzes();
        var submissionId = await SubmitQuestionAsync(isolated, "Bank question?");

        using var admin = isolated.CreateAdminClient();
        await ApproveAsIsAsync(admin, submissionId);

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quizId = await quizzes.CreateAsync(
            new AdminQuizDraft(
                "Existing quiz",
                null,
                [new QuizQuestionDraft("Seed question?", 1, [new QuizOptionDraft("X", true), new QuizOptionDraft("Y", false)])]),
            Guid.NewGuid());

        var bankPage = await admin.GetStringAsync($"/admin/quizzes/{quizId}/question-bank");
        Assert.Contains("Bank question?", bankPage, StringComparison.Ordinal);
        var bankToken = AdminHttpTestHelpers.ExtractAntiforgeryToken(bankPage);

        var addResponse = await admin.PostAsync(
            $"/admin/quizzes/{quizId}/question-bank?handler=Add",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = bankToken,
                ["submissionId"] = submissionId.ToString(),
            }));
        Assert.Equal(HttpStatusCode.Redirect, addResponse.StatusCode);

        var quiz = await quizzes.GetByIdAsync(quizId);
        Assert.Equal(2, quiz!.Questions.Count);
        Assert.Contains(quiz.Questions, question => question.Text == "Bank question?");

        var submissions = scope.ServiceProvider.GetRequiredService<IQuizQuestionSubmissionRepository>();
        var submission = await submissions.GetByIdAsync(submissionId);
        Assert.Equal(quizId, submission!.AddedToQuizId);
        Assert.DoesNotContain(await submissions.GetApprovedAndAvailableAsync(), item => item.Id == submissionId);
    }

    [Fact]
    public async Task Question_bank_add_is_blocked_once_the_quiz_has_results()
    {
        using var isolated = IsolatedQuizzes();
        var submissionId = await SubmitQuestionAsync(isolated, "Locked bank question?");

        using var admin = isolated.CreateAdminClient();
        await ApproveAsIsAsync(admin, submissionId);

        using var scope = isolated.Services.CreateScope();
        var quizzes = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quizId = await quizzes.CreateAsync(
            new AdminQuizDraft(
                "Quiz with results",
                null,
                [new QuizQuestionDraft("Seed?", 1, [new QuizOptionDraft("X", true), new QuizOptionDraft("Y", false)])]),
            Guid.NewGuid());

        // Fetch a valid antiforgery token before the quiz is locked — once results exist, the
        // question-bank page renders no form (nothing to submit), so no token is available then.
        var bankPageBeforeLock = await admin.GetStringAsync($"/admin/quizzes/{quizId}/question-bank");
        var bankToken = AdminHttpTestHelpers.ExtractAntiforgeryToken(bankPageBeforeLock);

        await quizzes.PublishAsync(quizId);
        await quizzes.RecordAttemptAsync(quizId, Guid.NewGuid(), 1, 1, 1);

        await admin.PostAsync(
            $"/admin/quizzes/{quizId}/question-bank?handler=Add",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = bankToken,
                ["submissionId"] = submissionId.ToString(),
            }));

        var afterAttempt = await admin.GetStringAsync($"/admin/quizzes/{quizId}/question-bank");
        Assert.Contains("cannot accept new questions", afterAttempt, StringComparison.Ordinal);
        var quiz = await quizzes.GetByIdAsync(quizId);
        Assert.Single(quiz!.Questions);
    }

    private static QueenZoneWebApplicationFactory IsolatedQuizzes()
    {
        var quizStore = new SharedQuizStore();
        var submissionStore = new InMemoryQuizQuestionSubmissionRepository();
        return QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedQuizStore>();
            services.RemoveAll<IQuizRepository>();
            services.AddSingleton(quizStore);
            services.AddSingleton<IQuizRepository>(_ => new InMemoryQuizRepository(quizStore));

            services.RemoveAll<IQuizQuestionSubmissionRepository>();
            services.AddSingleton<IQuizQuestionSubmissionRepository>(submissionStore);
        });
    }

    private static HttpClient MemberClient(QueenZoneWebApplicationFactory factory, string displayName)
    {
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, displayName);
        return client;
    }

    private static async Task<Guid> SubmitQuestionAsync(QueenZoneWebApplicationFactory factory, string questionText)
    {
        using var member = MemberClient(factory, "Submitter");
        var formPage = await member.GetStringAsync("/submit/quiz-question");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(formPage);
        var response = await member.PostAsync(
            "/submit/quiz-question",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("QuestionText", questionText),
                new KeyValuePair<string, string>("OptionTexts", "A"),
                new KeyValuePair<string, string>("OptionTexts", "B"),
                new KeyValuePair<string, string>("CorrectOptionIndex", "0"),
            ]));
        var location = response.Headers.Location!.OriginalString;
        return Guid.Parse(location.Split('/').Last());
    }

    private static async Task ApproveAsIsAsync(HttpClient admin, Guid submissionId)
    {
        var detail = await admin.GetStringAsync($"/admin/quiz-question-submissions/{submissionId}");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(detail);
        var questionText = QuestionTextareaRegex().Match(detail).Groups["text"].Value;
        var response = await admin.PostAsync(
            $"/admin/quiz-question-submissions/{submissionId}/approve",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("questionText", questionText),
                new KeyValuePair<string, string>("optionTexts", "A"),
                new KeyValuePair<string, string>("optionTexts", "B"),
                new KeyValuePair<string, string>("correctOptionIndex", "0"),
            ]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("""name="questionText"[^>]*>(?<text>[^<]*)<""")]
    private static partial Regex QuestionTextareaRegex();
}
