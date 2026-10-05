using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class ForumPostWriteServiceTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public ForumPostWriteServiceTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CreateTopic_rejects_missing_board_before_rate_limit()
    {
        var outcome = await CreateTopicAsync(Guid.NewGuid(), 9999, "Valid title here", "Hello");

        Assert.Equal(ForumWriteStatus.CategoryNotFound, outcome.Status);
        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task CreateTopic_rejects_short_title_and_empty_body()
    {
        var outcome = await CreateTopicAsync(Guid.NewGuid(), 1, "Hi", "<script>alert(1)</script>");

        Assert.Equal(ForumWriteStatus.ValidationFailed, outcome.Status);
        Assert.Contains(outcome.FieldErrors, error => error.Field == "Subject");
        Assert.Contains(outcome.FieldErrors, error => error.Field == "Body");
    }

    [Fact]
    public async Task CreateReply_rejects_missing_and_empty_body()
    {
        var missing = await CreateReplyAsync(Guid.NewGuid(), 9999, "Hello");
        Assert.Equal(ForumWriteStatus.TopicNotFound, missing.Status);

        var empty = await CreateReplyAsync(Guid.NewGuid(), 1002, "   ");
        Assert.Equal(ForumWriteStatus.ValidationFailed, empty.Status);
        Assert.Equal(ForumPostWriteService.BodyRequiredMessage, empty.FieldErrors[0].Message);
    }

    [Fact]
    public async Task CreateTopic_persists_plain_text_as_wrapped_html()
    {
        var memberId = Guid.NewGuid();
        var outcome = await CreateTopicAsync(memberId, 1, "Wrapped storage thread", "Hello from unit test");

        Assert.True(outcome.Succeeded);
        Assert.Equal("<p>Hello from unit test</p>", outcome.SanitizedBody);
        Assert.True(outcome.TopicId > 0);
        Assert.True(outcome.PostId > 0);
    }

    [Fact]
    public async Task CreateTopic_rejects_disallowed_attachment_types()
    {
        using var stream = new MemoryStream("not-an-image"u8.ToArray());
        IFormFile file = new FormFile(stream, 0, stream.Length, "attachments", "malware.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-msdownload",
        };

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        var outcome = await service.CreateTopicAsync(new NewForumTopicRequest(Guid.NewGuid(), "Service Tester", 1, "Attachment rejection thread", "Body with a bad attachment", [file], Poll: null));

        Assert.Equal(ForumWriteStatus.ValidationFailed, outcome.Status);
        Assert.Contains(outcome.FieldErrors, error =>
            error.Field == "Attachments" && error.Message.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateReply_autosuspends_member_who_posts_a_link_moments_after_registering()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(scope, DateTime.UtcNow);
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateReplyAsync(
            memberId, "Service Tester", 1002, "Check this out https://spam.example.com", attachments: null);

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.NotNull(account);
        Assert.True(account!.IsSuspended);
        Assert.Equal(ForumPostWriteService.AutoModeratorEmail, account.SuspendedByAdminEmail);
    }

    [Fact]
    public async Task CreateReply_does_not_flag_link_post_from_an_older_account()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(scope, DateTime.UtcNow - TimeSpan.FromHours(1));
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateReplyAsync(
            memberId, "Service Tester", 1002, "Check this out https://spam.example.com", attachments: null);

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.False(account!.IsSuspended);
    }

    [Fact]
    public async Task CreateReply_does_not_flag_new_account_posting_without_a_link()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(scope, DateTime.UtcNow);
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateReplyAsync(
            memberId, "Service Tester", 1002, "Excited to join this community!", attachments: null);

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.False(account!.IsSuspended);
    }

    [Fact]
    public async Task CreateReply_autosuspends_new_account_with_auto_generated_looking_email_even_without_a_link()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(
            scope, DateTime.UtcNow, email: "outlook_D2B04D13FE864466@outlook.com");
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateReplyAsync(
            memberId, "Service Tester", 1002, "Excited to join this community!", attachments: null);

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.NotNull(account);
        Assert.True(account!.IsSuspended);
        Assert.Equal(ForumPostWriteService.AutoModeratorEmail, account.SuspendedByAdminEmail);
    }

    [Fact]
    public async Task CreateReply_does_not_flag_older_account_with_auto_generated_looking_email()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(
            scope, DateTime.UtcNow - TimeSpan.FromHours(1), email: "outlook_D2B04D13FE864466@outlook.com");
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateReplyAsync(
            memberId, "Service Tester", 1002, "Excited to join this community!", attachments: null);

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.False(account!.IsSuspended);
    }

    [Fact]
    public async Task CreateReply_does_not_flag_new_account_with_merely_unusual_email()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(
            scope, DateTime.UtcNow, email: "outlook.fan.since.1998@outlook.com");
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateReplyAsync(
            memberId, "Service Tester", 1002, "Excited to join this community!", attachments: null);

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.False(account!.IsSuspended);
    }

    [Fact]
    public async Task CreateTopic_trustedSystemAuthor_does_not_autosuspend_new_account_link_post()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(scope, DateTime.UtcNow);
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();

        var outcome = await service.CreateTopicAsync(new NewForumTopicRequest(memberId, "QueenZone", 1, "Trusted system news topic", "Excerpt\n\nhttps://www.queenzone.org/news/1/trusted", Attachments: null, Poll: null) { TrustedSystemAuthor = true });

        Assert.True(outcome.Succeeded);
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByIdAsync(memberId);
        Assert.False(account!.IsSuspended);
    }

    [Fact]
    public async Task CreateTopic_trustedSystemAuthor_bypasses_rate_limit()
    {
        using var scope = factory.Services.CreateScope();
        var memberId = await CreateMemberAsync(scope, DateTime.UtcNow.AddDays(-1));
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        for (var i = 0; i < ForumPostRateLimiter.MaxPostsPerMinute; i++)
        {
            var blocked = await service.CreateTopicAsync(new NewForumTopicRequest(memberId, "Service Tester", 1, $"Rate fill topic {i} xx", "Body", Attachments: null, Poll: null));
            Assert.True(blocked.Succeeded);
        }

        var limited = await service.CreateTopicAsync(new NewForumTopicRequest(memberId, "Service Tester", 1, "Rate limited topic xx", "Body", Attachments: null, Poll: null));
        Assert.Equal(ForumWriteStatus.RateLimited, limited.Status);

        var trusted = await service.CreateTopicAsync(new NewForumTopicRequest(memberId, "QueenZone", 1, "Trusted rate bypass topic", "Body with https://www.queenzone.org/news/2/x", Attachments: null, Poll: null) { TrustedSystemAuthor = true });
        Assert.True(trusted.Succeeded);
    }

    private static async Task<Guid> CreateMemberAsync(IServiceScope scope, DateTime createdAt, string? email = null)
    {
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var member = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email ?? $"{Guid.NewGuid():N}@example.test",
            DisplayName = "New Member",
            CreatedAt = createdAt,
        };
        await repository.CreateAsync(member);
        return member.Id;
    }

    private async Task<ForumWriteOutcome> CreateTopicAsync(
        Guid memberId,
        int categoryId,
        string title,
        string body)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        return await service.CreateTopicAsync(new NewForumTopicRequest(memberId, "Service Tester", categoryId, title, body, Attachments: null, Poll: null));
    }

    private async Task<ForumWriteOutcome> CreateReplyAsync(Guid memberId, int topicId, string body)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ForumPostWriteService>();
        return await service.CreateReplyAsync(
            memberId,
            "Service Tester",
            topicId,
            body,
            attachments: null);
    }
}
