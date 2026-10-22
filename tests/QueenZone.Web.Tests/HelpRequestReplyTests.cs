using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed partial class HelpRequestReplyTests : IClassFixture<QueenZoneWebApplicationFactory>, IDisposable
{
    private const string AdminEmail = "admin@test.local";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T10:00:00Z");

    private readonly QueenZoneWebApplicationFactory factory;
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public HelpRequestReplyTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Reply_emails_requester_logs_the_reply_and_resolves()
    {
        var (service, repository, sender) = CreateService();
        var request = await repository.CreateAsync(Sample("Cannot log in to my account"));

        var result = await service.ReplyAsync(
            request.Id, "Re: Cannot log in to my account", "  Try resetting your password.  ", AdminEmail, markResolved: true);

        Assert.Equal(HelpRequestReplyService.ReplyOutcome.Sent, result.Outcome);
        var email = Assert.Single(sender.Sent);
        Assert.Equal("guest-help@example.com", email.ToAddress);
        Assert.Equal("Re: Cannot log in to my account", email.Subject);
        Assert.Equal("support@queenzone.org", email.ReplyToAddress);
        Assert.StartsWith("Try resetting your password.", email.TextBody);
        Assert.Contains("> This is a sufficiently long help message.", email.TextBody);

        var logged = Assert.Single(await repository.ListRepliesAsync(request.Id));
        Assert.Equal("guest-help@example.com", logged.ToEmail);
        Assert.Equal("Try resetting your password.", logged.Body);
        Assert.Equal(AdminEmail, logged.SentByEmail);
        Assert.Equal(Now, logged.SentAt);

        var updated = await repository.GetByIdAsync(request.Id);
        Assert.Equal(HelpRequestStatus.Resolved, updated!.Status);
        Assert.Equal(AdminEmail, updated.ReviewerEmail);
    }

    [Fact]
    public async Task Reply_without_resolving_leaves_status_and_notes_alone()
    {
        var (service, repository, _) = CreateService();
        var request = await repository.CreateAsync(Sample("Question"));

        var result = await service.ReplyAsync(request.Id, "Re: Question", "Thanks, looking now.", AdminEmail, markResolved: false);

        Assert.Equal(HelpRequestReplyService.ReplyOutcome.Sent, result.Outcome);
        Assert.Equal(HelpRequestStatus.Open, (await repository.GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task Reply_that_fails_to_send_is_not_logged()
    {
        var (service, repository, sender) = CreateService();
        sender.Failure = new InvalidOperationException("smtp down");
        var request = await repository.CreateAsync(Sample("Question"));

        var result = await service.ReplyAsync(request.Id, "Re: Question", "A real reply", AdminEmail, markResolved: true);

        Assert.Equal(HelpRequestReplyService.ReplyOutcome.SendFailed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Empty(await repository.ListRepliesAsync(request.Id));
        Assert.Equal(HelpRequestStatus.Open, (await repository.GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task Reply_cancellation_propagates()
    {
        var (service, repository, sender) = CreateService();
        sender.Failure = new OperationCanceledException();
        var request = await repository.CreateAsync(Sample("Question"));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ReplyAsync(request.Id, "Re: Question", "A real reply", AdminEmail, markResolved: false));
    }

    [Theory]
    [InlineData("", "Body text", "A subject is required.")]
    [InlineData("Subject", " ", "Write a reply before sending.")]
    public async Task Reply_rejects_blank_content_without_sending(string subject, string body, string error)
    {
        var (service, repository, sender) = CreateService();
        var request = await repository.CreateAsync(Sample("Question"));

        var result = await service.ReplyAsync(request.Id, subject, body, AdminEmail, markResolved: false);

        Assert.Equal(HelpRequestReplyService.ReplyOutcome.Invalid, result.Outcome);
        Assert.Equal(error, result.Error);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Reply_rejects_overlong_content()
    {
        var (service, repository, sender) = CreateService();
        var request = await repository.CreateAsync(Sample("Question"));

        var subject = await service.ReplyAsync(
            request.Id, new string('s', HelpRequestReply.MaxSubjectLength + 1), "Body text", AdminEmail, false);
        var body = await service.ReplyAsync(
            request.Id, "Subject", new string('b', HelpRequestReply.MaxBodyLength + 1), AdminEmail, false);

        Assert.Equal(HelpRequestReplyService.ReplyOutcome.Invalid, subject.Outcome);
        Assert.Equal(HelpRequestReplyService.ReplyOutcome.Invalid, body.Outcome);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Reply_to_unknown_request_is_not_found()
    {
        var (service, _, sender) = CreateService();

        var result = await service.ReplyAsync(Guid.NewGuid(), "Subject", "Body text", AdminEmail, false);

        Assert.Equal(HelpRequestReplyService.ReplyOutcome.NotFound, result.Outcome);
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData("Login problem", "Re: Login problem")]
    [InlineData("  re: Login problem ", "re: Login problem")]
    public void DefaultSubject_prefixes_once(string subject, string expected) =>
        Assert.Equal(expected, HelpRequestReplyService.DefaultSubject(subject));

    [Fact]
    public async Task InMemory_replies_are_listed_oldest_first_per_request()
    {
        var repository = new InMemoryHelpRequestRepository();
        var first = await repository.CreateAsync(Sample("First"));
        var second = await repository.CreateAsync(Sample("Second"));

        await repository.AddReplyAsync(Reply(first.Id, "later", Now.AddHours(1)));
        await repository.AddReplyAsync(Reply(first.Id, "earlier", Now));
        await repository.AddReplyAsync(Reply(second.Id, "other", Now));

        Assert.Equal(["earlier", "later"], (await repository.ListRepliesAsync(first.Id)).Select(r => r.Body));
        Assert.Null(await repository.AddReplyAsync(Reply(Guid.NewGuid(), "orphan", Now)));
    }

    [Fact]
    public async Task Ef_replies_persist_order_and_cascade_with_the_request()
    {
        connection.Open();
        await using var dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        await dbContext.Database.EnsureCreatedAsync();
        var repository = new EfHelpRequestRepository(dbContext);
        var request = await repository.CreateAsync(Sample("Persisted"));

        await repository.AddReplyAsync(Reply(request.Id, "  later  ", Now.AddHours(1)));
        var earlier = await repository.AddReplyAsync(Reply(request.Id, "earlier", Now));

        Assert.NotNull(earlier);
        Assert.Equal(["earlier", "later"], (await repository.ListRepliesAsync(request.Id)).Select(r => r.Body));
        Assert.Null(await repository.AddReplyAsync(Reply(Guid.NewGuid(), "orphan", Now)));
        Assert.Empty(await repository.ListRepliesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Admin_reply_form_sends_email_and_shows_the_log()
    {
        var sender = new RecordingEmailSender();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        });
        var repository = host.Services.GetRequiredService<IHelpRequestRepository>();
        var request = await repository.CreateAsync(Sample("Route reply subject"));
        var admin = AdminHttpTestHelpers.CreateClient(host, AdminEmail);

        var detail = await admin.GetStringAsync($"/admin/help/{request.Id}");
        Assert.Contains("data-testid=\"admin-help-reply-form\"", detail);
        Assert.Contains("value=\"Re: Route reply subject\"", detail);

        var response = await admin.PostAsync(
            $"/admin/help/{request.Id}/reply",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["replySubject"] = "Re: Route reply subject",
                ["replyBody"] = "Here is the answer.",
                ["markResolved"] = "true",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/help/{request.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal("guest-help@example.com", Assert.Single(sender.Sent).ToAddress);

        var after = await admin.GetStringAsync($"/admin/help/{request.Id}");
        Assert.Contains("Reply emailed to guest-help@example.com.", after);
        Assert.Contains("Here is the answer.", after);
        Assert.Contains($"by {AdminEmail}", after);
        Assert.Equal(HelpRequestStatus.Resolved, (await repository.GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task Admin_reply_form_keeps_the_draft_when_sending_fails()
    {
        var sender = new RecordingEmailSender { Failure = new InvalidOperationException("smtp down") };
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        });
        var repository = host.Services.GetRequiredService<IHelpRequestRepository>();
        var request = await repository.CreateAsync(Sample("Route failure subject"));
        var admin = AdminHttpTestHelpers.CreateClient(host, AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/help/{request.Id}");

        await admin.PostAsync(
            $"/admin/help/{request.Id}/reply",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["replySubject"] = "Re: Route failure subject",
                ["replyBody"] = "Draft that must survive.",
            }));

        var after = await admin.GetStringAsync($"/admin/help/{request.Id}");
        Assert.Contains("The email could not be sent", after);
        Assert.Contains("Draft that must survive.", after);
        Assert.Empty(await repository.ListRepliesAsync(request.Id));
    }

    [Fact]
    public async Task Admin_reply_to_unknown_request_returns_not_found()
    {
        var admin = factory.CreateAdminClient(AdminEmail);
        var existing = await factory.Services.GetRequiredService<IHelpRequestRepository>()
            .CreateAsync(Sample("Token source"));
        var detail = await admin.GetStringAsync($"/admin/help/{existing.Id}");

        var response = await admin.PostAsync(
            $"/admin/help/{Guid.NewGuid()}/reply",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["replySubject"] = "Re: Missing",
                ["replyBody"] = "Nobody home.",
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static (HelpRequestReplyService Service, InMemoryHelpRequestRepository Repository, RecordingEmailSender Sender)
        CreateService()
    {
        var repository = new InMemoryHelpRequestRepository();
        var sender = new RecordingEmailSender();
        var service = new HelpRequestReplyService(
            repository,
            sender,
            new FixedTimeProvider(Now),
            Options.Create(new HelpRequestOptions()),
            NullLogger<HelpRequestReplyService>.Instance);
        return (service, repository, sender);
    }

    private static HelpRequest Sample(string subject) =>
        new(
            Guid.NewGuid(),
            HelpRequestTopic.Account,
            subject,
            "This is a sufficiently long help message.",
            "Guest User",
            "guest-help@example.com",
            "GUEST-HELP@EXAMPLE.COM",
            null,
            HelpRequestStatus.Open,
            Now.AddDays(-1),
            null,
            null,
            null);

    private static HelpRequestReply Reply(Guid requestId, string body, DateTimeOffset sentAt) =>
        new(Guid.NewGuid(), requestId, "guest-help@example.com", "Re: Subject", body, AdminEmail, sentAt);

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<OutboundEmail> Sent { get; } = [];

        public Exception? Failure { get; set; }

        public Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
