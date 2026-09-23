using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class HelpRequestServiceTests
{
    [Fact]
    public async Task SubmitAsync_StoresGuestRequest()
    {
        var harness = CreateHarness();

        var result = await harness.Service.SubmitAsync(
            memberId: null,
            HelpRequestTopic.Technical,
            "Cannot open a forum topic",
            "The topic page returns an error when I click the latest thread.",
            "Alex Fan",
            "alex@example.com",
            websiteHoneypot: null,
            issuedStamp: harness.Service.IssueFormStamp(),
            clientIp: "203.0.113.20");

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.SilentlyDropped);
        Assert.NotNull(result.Request);
        Assert.Equal("Alex Fan", result.Request!.Name);
        Assert.Equal("alex@example.com", result.Request.Email);
        Assert.Null(result.Request.MemberId);
        Assert.Equal(HelpRequestStatus.Open, result.Request.Status);
        Assert.Equal(1, await harness.Repository.CountOpenAsync());
    }

    [Fact]
    public async Task SubmitAsync_EmailsSupportAfterStoringRequest()
    {
        var sender = new RecordingEmailSender();
        var harness = CreateHarness(emailSender: sender);

        var result = await harness.Service.SubmitAsync(
            null, HelpRequestTopic.Technical, "Cannot open a forum topic",
            "The topic page returns an error when I click the latest thread.",
            "Alex Fan", "alex@example.com", null, harness.Service.IssueFormStamp(), "203.0.113.20");

        Assert.True(result.Succeeded);
        Assert.Equal("support@queenzone.org", sender.To);
        Assert.Equal("alex@example.com", sender.ReplyTo);
        Assert.Contains("Cannot open a forum topic", sender.Body);
    }

    [Fact]
    public async Task SubmitAsync_UsesMemberSnapshotAndHidesGuestFields()
    {
        var harness = CreateHarness();
        var member = await CreateMemberAsync(harness.Members, "member@example.com", "Member Fan");

        var result = await harness.Service.SubmitAsync(
            member.Id,
            HelpRequestTopic.Account,
            "Need my display name changed",
            "Please update my public display name on the archive.",
            name: "Ignored",
            email: "ignored@example.com",
            websiteHoneypot: null,
            issuedStamp: harness.Service.IssueFormStamp(),
            clientIp: "203.0.113.21");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Member Fan", result.Request!.Name);
        Assert.Equal("member@example.com", result.Request.Email);
        Assert.Equal(member.Id, result.Request.MemberId);
    }

    [Fact]
    public async Task SubmitAsync_DropsHoneypotWithoutStoring()
    {
        var harness = CreateHarness();

        var result = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "Buy cheap watches now",
            "This is definitely a long enough spam advertisement body.",
            "Bot",
            "bot@example.com",
            websiteHoneypot: "https://spam.example",
            issuedStamp: harness.Service.IssueFormStamp(),
            clientIp: "203.0.113.22");

        Assert.True(result.Succeeded);
        Assert.True(result.SilentlyDropped);
        Assert.Null(result.Request);
        Assert.Equal(0, await harness.Repository.CountOpenAsync());
    }

    [Fact]
    public async Task SubmitAsync_DropsTooFastStampWhenDwellRequired()
    {
        var harness = CreateHarness(dwellSeconds: 3);
        var stamp = harness.Service.IssueFormStamp();

        var result = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "Need help immediately",
            "This should be dropped because the form was submitted too quickly.",
            "Alex Fan",
            "alex@example.com",
            websiteHoneypot: null,
            issuedStamp: stamp,
            clientIp: "203.0.113.23");

        Assert.True(result.Succeeded);
        Assert.True(result.SilentlyDropped);
        Assert.Equal(0, await harness.Repository.CountOpenAsync());
    }

    [Fact]
    public async Task SubmitAsync_RequiresGuestNameAndEmail()
    {
        var harness = CreateHarness();

        var missingName = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "Need help with login",
            "I cannot sign in with my usual Google account any more.",
            name: " ",
            email: "alex@example.com",
            websiteHoneypot: null,
            issuedStamp: harness.Service.IssueFormStamp(),
            clientIp: "203.0.113.24");

        Assert.False(missingName.Succeeded);
        Assert.Contains("Name", missingName.Error, StringComparison.OrdinalIgnoreCase);

        var missingEmail = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "Need help with login",
            "I cannot sign in with my usual Google account any more.",
            name: "Alex Fan",
            email: "not-an-email",
            websiteHoneypot: null,
            issuedStamp: harness.Service.IssueFormStamp(),
            clientIp: "203.0.113.24");

        Assert.False(missingEmail.Succeeded);
        Assert.Contains("email", missingEmail.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_EnforcesDailyCaps()
    {
        var harness = CreateHarness(maxPerEmail: 1, maxPerMember: 1);
        var member = await CreateMemberAsync(harness.Members, "capped@example.com", "Capped Fan");

        var firstGuest = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "First guest request here",
            "This is the first help request from this guest email address.",
            "Guest",
            "guestcap@example.com",
            null,
            harness.Service.IssueFormStamp(),
            "203.0.113.25");
        Assert.True(firstGuest.Succeeded, firstGuest.Error);

        var secondGuest = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "Second guest request here",
            "This should be blocked by the guest email daily cap.",
            "Guest",
            "guestcap@example.com",
            null,
            harness.Service.IssueFormStamp(),
            "203.0.113.26");
        Assert.False(secondGuest.Succeeded);
        Assert.Contains("per day", secondGuest.Error, StringComparison.OrdinalIgnoreCase);

        var firstMember = await harness.Service.SubmitAsync(
            member.Id,
            HelpRequestTopic.Account,
            "First member request here",
            "This is the first help request from the signed-in member.",
            null,
            null,
            null,
            harness.Service.IssueFormStamp(),
            "203.0.113.27");
        Assert.True(firstMember.Succeeded, firstMember.Error);

        var secondMember = await harness.Service.SubmitAsync(
            member.Id,
            HelpRequestTopic.Account,
            "Second member request here",
            "This should be blocked by the member daily cap.",
            null,
            null,
            null,
            harness.Service.IssueFormStamp(),
            "203.0.113.27");
        Assert.False(secondMember.Succeeded);
        Assert.Contains("per day", secondMember.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_DeniesAnonymousWhenIpMissing()
    {
        var harness = CreateHarness();

        var result = await harness.Service.SubmitAsync(
            null,
            HelpRequestTopic.Other,
            "Need help with login",
            "I cannot sign in with my usual Google account any more.",
            "Alex Fan",
            "alex@example.com",
            null,
            harness.Service.IssueFormStamp(),
            clientIp: null);

        Assert.False(result.Succeeded);
        Assert.Contains("Too many", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveClientIp_UsesRemoteAddressThenAutomatedTestFallback()
    {
        var withIp = new DefaultHttpContext();
        withIp.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.40");
        Assert.Equal("203.0.113.40", HelpRequestService.ResolveClientIp(withIp));

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(QueenZoneEnvironments.Testing));
        var testing = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        Assert.Equal("test", HelpRequestService.ResolveClientIp(testing));
    }

    private static async Task<MemberAccount> CreateMemberAsync(
        InMemoryMemberAccountRepository members,
        string email,
        string displayName)
    {
        return await members.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static Harness CreateHarness(
        int dwellSeconds = 0,
        int maxPerEmail = 2,
        int maxPerMember = 5,
        IEmailSender? emailSender = null)
    {
        var repository = new InMemoryHelpRequestRepository();
        var members = new InMemoryMemberAccountRepository();
        var timeProvider = TimeProvider.System;
        var options = Options.Create(new HelpRequestOptions
        {
            MinimumDwellSeconds = dwellSeconds,
            MaxPerEmailPerDay = maxPerEmail,
            MaxPerMemberPerDay = maxPerMember,
            MaxAnonymousPerIpPerHour = 10,
        });
        var stamp = new HelpRequestFormStamp(new EphemeralDataProtectionProvider(), timeProvider);
        var limiter = new HelpRequestRateLimiter(
            new MemoryCache(new MemoryCacheOptions()),
            timeProvider,
            options);
        var service = new HelpRequestService(repository, members, stamp, limiter, timeProvider, options, emailSender);
        return new Harness(service, repository, members);
    }

    private sealed record Harness(
        HelpRequestService Service,
        InMemoryHelpRequestRepository Repository,
        InMemoryMemberAccountRepository Members);

    private sealed class RecordingEmailSender : IEmailSender
    {
        public string? To { get; private set; }
        public string? ReplyTo { get; private set; }
        public string? Body { get; private set; }

        public Task SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            To = email.ToAddress;
            ReplyTo = email.ReplyToAddress;
            Body = email.TextBody;
            return Task.CompletedTask;
        }
    }
}
