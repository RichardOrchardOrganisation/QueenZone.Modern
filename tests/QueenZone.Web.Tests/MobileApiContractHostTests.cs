using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

[Collection(EnvironmentVariableCollection.Name)]
public sealed class MobileApiContractHostTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public MobileApiContractHostTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void IsEnabled_requires_testing_and_opt_in_flag()
    {
        Assert.False(MobileApiContractHost.IsEnabled(factory.Services.GetRequiredService<IHostEnvironment>()));

        var previous = Environment.GetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable, "1");
            Assert.True(MobileApiContractHost.IsEnabled(new StubHostEnvironment(QueenZoneEnvironments.Testing)));
            Assert.True(MobileApiContractHost.IsEnabled(factory.Services.GetRequiredService<IHostEnvironment>()));
            Environment.SetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable, "true");
            Assert.False(MobileApiContractHost.IsEnabled(new StubHostEnvironment("Production")));
            Assert.False(MobileApiContractHost.IsEnabled(new StubHostEnvironment(QueenZoneEnvironments.E2E)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task Seed_issues_bearer_tokens_for_real_me_and_poll_endpoints()
    {
        var seed = await MobileApiContractHost.SeedAsync(factory.Services);

        using var me = factory.CreateAnonymousClient(allowAutoRedirect: false);
        me.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.MemberToken);
        using var meResponse = await me.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var profile = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(MobileApiContractHost.MemberId.ToString("D"), profile.GetProperty("memberId").GetString());
        Assert.Equal(MobileApiContractHost.MemberDisplayName, profile.GetProperty("displayName").GetString());

        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var pollResponse = await anonymous.GetAsync($"/api/v1/forum/topics/{seed.PollTopicId}/poll");
        Assert.Equal(HttpStatusCode.OK, pollResponse.StatusCode);
        var poll = await pollResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Best Queen album?", poll.GetProperty("question").GetString());
        Assert.Equal(seed.PollOptionId.ToString("D"), poll.GetProperty("options")[0].GetProperty("optionId").GetString());

        using var suspended = factory.CreateAnonymousClient(allowAutoRedirect: false);
        suspended.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", seed.SuspendedMemberToken);
        using var forbidden = await suspended.PostAsJsonAsync(
            "/api/v1/forum/topics/1002/posts",
            new { body = "Suspended members cannot post from the contract host." });
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        var problem = await forbidden.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unauthorized", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Seed_links_published_news_discussion_seeds_attach_topic_and_unread_inbox()
    {
        var seed = await MobileApiContractHost.SeedAsync(factory.Services);

        using var member = factory.CreateAnonymousClient(allowAutoRedirect: false);
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.MemberToken);

        using var news = await member.GetAsync($"/api/v1/content/news/{MobileApiContractHost.PublishedNewsId}");
        Assert.Equal(HttpStatusCode.OK, news.StatusCode);
        var article = await news.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seed.DiscussionTopicId, article.GetProperty("topicId").GetInt32());
        Assert.True(article.GetProperty("discussionReplyCount").GetInt32() >= 1);

        using var discussion = await member.GetAsync($"/api/v1/forum/topics/{seed.DiscussionTopicId}");
        Assert.Equal(HttpStatusCode.OK, discussion.StatusCode);
        var topic = await discussion.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(MobileApiContractHost.DiscussionTopicSubject, topic.GetProperty("title").GetString());
        Assert.False(topic.GetProperty("isLocked").GetBoolean());

        using var attach = await member.GetAsync($"/api/v1/forum/topics/{seed.AttachTopicId}");
        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        var attachTopic = await attach.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(MobileApiContractHost.AttachTopicSubject, attachTopic.GetProperty("title").GetString());
        Assert.False(attachTopic.GetProperty("isLocked").GetBoolean());

        using var unreadResponse = await member.GetAsync("/api/v1/me/messages/unread-count");
        Assert.Equal(HttpStatusCode.OK, unreadResponse.StatusCode);
        var unread = await unreadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(unread.GetProperty("unreadConversationCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Seed_is_idempotent_for_the_published_news_discussion_link()
    {
        var first = await MobileApiContractHost.SeedAsync(factory.Services);
        var second = await MobileApiContractHost.SeedAsync(factory.Services);

        Assert.Equal(first.DiscussionTopicId, second.DiscussionTopicId);
        Assert.NotEqual(first.AttachTopicId, second.AttachTopicId);
    }

    [Fact]
    public async Task WriteFixture_is_camel_case_and_omits_secrets()
    {
        var seed = await MobileApiContractHost.SeedAsync(factory.Services);
        var path = Path.Combine(Path.GetTempPath(), $"qz-contract-fixture-{Guid.NewGuid():N}.json");
        try
        {
            var fixture = MobileApiContractHost.BuildFixture("http://127.0.0.1:5099/", seed);
            MobileApiContractHost.WriteFixture(path, fixture);

            Assert.True(File.Exists(path));
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"baseUrl\"", json, StringComparison.Ordinal);
            Assert.Contains("\"accessToken\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("ConnectionString", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SigningKey", json, StringComparison.OrdinalIgnoreCase);

            var roundTrip = MobileApiContractHost.ReadFixture(path);
            Assert.Equal("http://127.0.0.1:5099", roundTrip.BaseUrl);
            Assert.Equal(QueenZoneEnvironments.Testing, roundTrip.Environment);
            Assert.Equal(seed.PollTopicId, roundTrip.PollTopicId);
            Assert.Equal(seed.AttachTopicId, roundTrip.AttachTopicId);
            Assert.Equal(seed.DiscussionTopicId, roundTrip.DiscussionTopicId);
            Assert.Equal(MobileApiContractHost.MemberId.ToString("D"), roundTrip.Member.Id);
            Assert.False(string.IsNullOrWhiteSpace(roundTrip.Member.AccessToken));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ReadBoundAddress_uses_loopback_http_binding()
    {
        var server = new StubServer("http://127.0.0.1:0", "http://127.0.0.1:5146");
        var ex = Assert.Throws<InvalidOperationException>(() => MobileApiContractHost.ReadBoundAddress(
            new StubServer("http://127.0.0.1:0")));
        Assert.Contains("ephemeral placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("http://127.0.0.1:5146", MobileApiContractHost.ReadBoundAddress(server));
        Assert.Equal(
            "http://127.0.0.1:5146",
            MobileApiContractHost.ReadBoundAddress(new StubServer("http://127.0.0.1:0", "http://127.0.0.1:5146")));
    }

    [Fact]
    public async Task BootstrapAsync_writes_fixture_from_bound_address()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qz-contract-bootstrap-{Guid.NewGuid():N}.json");
        var previousPath = Environment.GetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, path);
            await MobileApiContractHostedService.BootstrapAsync(
                factory.Services,
                new StubServer("http://127.0.0.1:5099"),
                NullLogger.Instance);

            var fixture = MobileApiContractHost.ReadFixture(path);
            Assert.Equal("http://127.0.0.1:5099", fixture.BaseUrl);
            Assert.Equal(MobileApiContractHost.MemberEmail, fixture.Member.Email);
            Assert.False(string.IsNullOrWhiteSpace(fixture.SuspendedMember.AccessToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, previousPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task HostedService_writes_fixture_when_application_starts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qz-contract-hosted-{Guid.NewGuid():N}.json");
        var previousPath = Environment.GetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable);
        var lifetime = new StubLifetime();
        try
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, path);
            var hosted = new MobileApiContractHostedService(
                factory.Services,
                new StubServer("http://127.0.0.1:5098"),
                lifetime,
                NullLogger<MobileApiContractHostedService>.Instance);

            await hosted.StartAsync(CancellationToken.None);
            lifetime.NotifyStarted();
            Assert.True(File.Exists(path));
            var fixture = MobileApiContractHost.ReadFixture(path);
            Assert.Equal("http://127.0.0.1:5098", fixture.BaseUrl);
            await hosted.StopAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, previousPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task HostedService_stops_the_host_when_bootstrap_fails()
    {
        var lifetime = new StubLifetime();
        var hosted = new MobileApiContractHostedService(
            factory.Services,
            new StubServer("http://127.0.0.1:0"),
            lifetime,
            NullLogger<MobileApiContractHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        lifetime.NotifyStarted();
        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public void AddMobileApiContractHost_registers_only_when_testing_and_opted_in()
    {
        var previous = Environment.GetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable, "1");
            var enabled = new ServiceCollection();
            enabled.AddMobileApiContractHost(new StubHostEnvironment(QueenZoneEnvironments.Testing));
            Assert.Contains(enabled, descriptor => descriptor.ServiceType == typeof(IHostedService));

            var production = new ServiceCollection();
            production.AddMobileApiContractHost(new StubHostEnvironment("Production"));
            Assert.DoesNotContain(production, descriptor => descriptor.ServiceType == typeof(IHostedService));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.EnableEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void BuildFixture_rewrites_wildcard_hosts_to_loopback()
    {
        var seed = new MobileApiContractSeed("a", "b", "c", 1, Guid.Parse("44444444-4444-4444-4444-444444444444"), 2, 3);
        var wildcard = MobileApiContractHost.BuildFixture("http://0.0.0.0:5099/", seed);
        Assert.Equal("http://127.0.0.1:5099", wildcard.BaseUrl);
        var plus = MobileApiContractHost.BuildFixture("http://+:5146", seed);
        Assert.Equal("http://127.0.0.1:5146", plus.BaseUrl);
    }

    [Fact]
    public void ResolveFixturePath_uses_override_or_temp_default()
    {
        var previous = Environment.GetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, null);
            Assert.EndsWith("queenzone-mobile-api-contract-host.json", MobileApiContractHost.ResolveFixturePath());

            var overridePath = Path.Combine(Path.GetTempPath(), "custom-contract.json");
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, overridePath);
            Assert.Equal(Path.GetFullPath(overridePath), MobileApiContractHost.ResolveFixturePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(MobileApiContractHost.FixturePathEnvironmentVariable, previous);
        }
    }

    private sealed class StubHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "QueenZone.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => stopped.Token;

        public bool StopRequested { get; private set; }

        public void NotifyStarted() => started.Cancel();

        public void StopApplication() => StopRequested = true;
    }

    private sealed class StubServer : IServer
    {
        public StubServer(params string[] addresses)
        {
            var feature = new ServerAddressesFeature();
            foreach (var address in addresses)
            {
                feature.Addresses.Add(address);
            }

            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
