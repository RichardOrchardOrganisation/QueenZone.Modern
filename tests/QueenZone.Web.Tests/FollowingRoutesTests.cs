using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;
using QueenZone.Web.Pages;

namespace QueenZone.Web.Tests;

public sealed class FollowingRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public FollowingRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Get_Following_RedirectsGuestToSignInWithReturnUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/following");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login?returnUrl=%2Ffollowing", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_Following_ShowsNotFollowingEmptyState()
    {
        var (client, _) = await CreateMemberAsync(factory, "follow-empty@example.com", "Empty Follow");

        var html = await client.GetStringAsync("/following");

        Assert.Contains(FollowingModel.NotFollowingEmpty, html);
        Assert.DoesNotContain(FollowingModel.NoActivityEmpty, html);
        Assert.Contains("href=\"/following\"", html);
        Assert.Contains(">Following</a>", html);
    }

    [Fact]
    public async Task Get_Following_ShowsNoActivityEmptyState_WhenFollowedMembersHaveNoPublicItems()
    {
        using var host = WithActivity([]);
        var (client, viewer) = await CreateMemberAsync(host, "follow-no-items@example.com", "No Items Viewer");
        var quiet = await CreateAccountAsync(host, "follow-quiet@example.com", "Quiet Member");
        await host.Services.GetRequiredService<IMemberFollowRepository>()
            .FollowAsync(viewer.Id, quiet.Id, DateTimeOffset.UtcNow);

        var html = await client.GetStringAsync("/following");

        Assert.Contains(FollowingModel.NoActivityEmpty, html);
        Assert.DoesNotContain(FollowingModel.NotFollowingEmpty, html);
    }

    [Fact]
    public async Task Get_Following_ShowsMixedNewestFirstItems_AttributedToAuthors()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var carolId = Guid.NewGuid();
        var items = new List<MemberPublicActivityItem>
        {
            Item(MemberPublicActivityType.ForumPost, "Alice forum", aliceId, "Feed Alice", DateTimeOffset.Parse("2026-08-03T12:00:00Z"), 201, 101, "alice-forum"),
            Item(MemberPublicActivityType.Article, "Bob article", bobId, "Feed Bob", DateTimeOffset.Parse("2026-08-03T11:00:00Z"), slug: "bob-article"),
            Item(MemberPublicActivityType.ForumPost, "Carol forum", carolId, "Feed Carol", DateTimeOffset.Parse("2026-08-03T13:00:00Z"), 203, 103, "carol-forum"),
        };
        using var host = WithActivity(items);
        var recorder = (RecordingMemberPublicActivityRepository)host.Services.GetRequiredService<IMemberPublicActivityRepository>();
        var (client, viewer) = await CreateMemberAsync(host, "feed-viewer@example.com", "Feed Viewer");
        await CreateAccountAsync(host, "feed-alice@example.com", "Feed Alice", aliceId);
        await CreateAccountAsync(host, "feed-bob@example.com", "Feed Bob", bobId);
        await CreateAccountAsync(host, "feed-carol@example.com", "Feed Carol", carolId);
        var follows = host.Services.GetRequiredService<IMemberFollowRepository>();
        await follows.FollowAsync(viewer.Id, aliceId, DateTimeOffset.UtcNow);
        await follows.FollowAsync(viewer.Id, bobId, DateTimeOffset.UtcNow);

        var html = await client.GetStringAsync("/following");

        Assert.Contains("Alice forum", html);
        Assert.Contains("Bob article", html);
        Assert.DoesNotContain("Carol forum", html);
        Assert.Contains($"href=\"/members/{aliceId}\"", html);
        Assert.Contains("Feed Alice", html);
        Assert.Contains($"href=\"/members/{bobId}\"", html);
        Assert.Contains("#post-201", html);
        Assert.True(html.IndexOf("Alice forum", StringComparison.Ordinal) < html.IndexOf("Bob article", StringComparison.Ordinal));
        Assert.Equal(1, recorder.FeedPageCalls);
        Assert.Equal(0, recorder.SinglePageCalls);
        Assert.Equal(
            new[] { aliceId, bobId }.OrderBy(id => id).ToArray(),
            recorder.LastFeedAuthorIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task Get_Following_UnfollowRemovesItems_FollowAddsThem()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var items = new List<MemberPublicActivityItem>
        {
            Item(MemberPublicActivityType.ForumPost, "Alice stays", aliceId, "Change Alice", DateTimeOffset.UtcNow, 1, 1, "alice-stays"),
            Item(MemberPublicActivityType.ForumPost, "Bob arrives", bobId, "Change Bob", DateTimeOffset.UtcNow.AddMinutes(-1), 2, 2, "bob-arrives"),
        };
        using var host = WithActivity(items);
        var (client, viewer) = await CreateMemberAsync(host, "follow-change@example.com", "Change Viewer");
        await CreateAccountAsync(host, "follow-change-alice@example.com", "Change Alice", aliceId);
        await CreateAccountAsync(host, "follow-change-bob@example.com", "Change Bob", bobId);
        var follows = host.Services.GetRequiredService<IMemberFollowRepository>();
        await follows.FollowAsync(viewer.Id, aliceId, DateTimeOffset.UtcNow);
        await follows.FollowAsync(viewer.Id, bobId, DateTimeOffset.UtcNow);

        var both = await client.GetStringAsync("/following");
        Assert.Contains("Alice stays", both);
        Assert.Contains("Bob arrives", both);

        await follows.UnfollowAsync(viewer.Id, bobId);
        var afterUnfollow = await client.GetStringAsync("/following");
        Assert.Contains("Alice stays", afterUnfollow);
        Assert.DoesNotContain("Bob arrives", afterUnfollow);

        await follows.FollowAsync(viewer.Id, bobId, DateTimeOffset.UtcNow);
        var afterFollow = await client.GetStringAsync("/following");
        Assert.Contains("Bob arrives", afterFollow);
    }

    [Fact]
    public async Task Get_Following_DropsBlockedAndDeletedMembers_WithoutQueryingWhenNoneRemain()
    {
        var blockedId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var items = new List<MemberPublicActivityItem>
        {
            Item(MemberPublicActivityType.ForumPost, "Blocked post", blockedId, "Blocked Member", DateTimeOffset.UtcNow, 1, 1, "blocked-post"),
            Item(MemberPublicActivityType.ForumPost, "Deleted post", deletedId, "Deleted Member", DateTimeOffset.UtcNow, 2, 2, "deleted-post"),
        };
        using var host = WithActivity(items);
        var recorder = (RecordingMemberPublicActivityRepository)host.Services.GetRequiredService<IMemberPublicActivityRepository>();
        var (client, viewer) = await CreateMemberAsync(host, "follow-filter@example.com", "Filter Viewer");
        await CreateAccountAsync(host, "follow-blocked@example.com", "Blocked Member", blockedId);
        await CreateAccountAsync(host, "follow-deleted@example.com", "Deleted Member", deletedId);
        var follows = host.Services.GetRequiredService<IMemberFollowRepository>();
        await follows.FollowAsync(viewer.Id, blockedId, DateTimeOffset.UtcNow);
        await follows.FollowAsync(viewer.Id, deletedId, DateTimeOffset.UtcNow);
        await host.Services.GetRequiredService<IPrivateMessageModerationRepository>()
            .BlockAsync(viewer.Id, blockedId, DateTimeOffset.UtcNow);
        await host.Services.GetRequiredService<IMemberAccountRepository>()
            .RequestDeletionAsync(deletedId, DateTime.UtcNow);

        var html = await client.GetStringAsync("/following");

        Assert.Contains(FollowingModel.NotFollowingEmpty, html);
        Assert.DoesNotContain("Blocked post", html);
        Assert.DoesNotContain("Deleted post", html);
        Assert.Equal(0, recorder.FeedPageCalls);
        Assert.Equal(0, recorder.SinglePageCalls);
    }

    [Fact]
    public async Task Get_Following_OmitsBlockedMemberWhenOtherFollowsRemain()
    {
        var visibleId = Guid.NewGuid();
        var blockedId = Guid.NewGuid();
        var items = new List<MemberPublicActivityItem>
        {
            Item(MemberPublicActivityType.ForumPost, "Visible post", visibleId, "Visible Member", DateTimeOffset.UtcNow, 1, 1, "visible-post"),
            Item(MemberPublicActivityType.ForumPost, "Blocked leftover", blockedId, "Blocked Member", DateTimeOffset.UtcNow.AddMinutes(-1), 2, 2, "blocked-leftover"),
        };
        using var host = WithActivity(items);
        var (client, viewer) = await CreateMemberAsync(host, "follow-leftover@example.com", "Leftover Viewer");
        await CreateAccountAsync(host, "follow-visible@example.com", "Visible Member", visibleId);
        await CreateAccountAsync(host, "follow-leftover-blocked@example.com", "Blocked Member", blockedId);
        var follows = host.Services.GetRequiredService<IMemberFollowRepository>();
        await follows.FollowAsync(viewer.Id, visibleId, DateTimeOffset.UtcNow);
        await follows.FollowAsync(viewer.Id, blockedId, DateTimeOffset.UtcNow);
        await host.Services.GetRequiredService<IPrivateMessageModerationRepository>()
            .BlockAsync(viewer.Id, blockedId, DateTimeOffset.UtcNow);

        var html = await client.GetStringAsync("/following");

        Assert.Contains("Visible post", html);
        Assert.DoesNotContain("Blocked leftover", html);
    }

    [Fact]
    public async Task Get_Following_PaginatesAtTwentyItems()
    {
        var authorId = Guid.NewGuid();
        var items = Enumerable.Range(1, FollowingModel.FeedPageSize + 1)
            .Select(index => Item(
                MemberPublicActivityType.ForumPost,
                $"Topic {index}",
                authorId,
                "Page Author",
                DateTimeOffset.UtcNow.AddMinutes(-index),
                index,
                1000 + index,
                $"topic-{index}"))
            .ToList();
        using var host = WithActivity(items);
        var (client, viewer) = await CreateMemberAsync(host, "follow-page@example.com", "Page Viewer");
        await CreateAccountAsync(host, "follow-page-author@example.com", "Page Author", authorId);
        await host.Services.GetRequiredService<IMemberFollowRepository>()
            .FollowAsync(viewer.Id, authorId, DateTimeOffset.UtcNow);

        var firstPage = await client.GetStringAsync("/following");
        Assert.Contains("Topic 1", firstPage);
        Assert.DoesNotContain("Topic 21", firstPage);
        Assert.Contains("Following feed pagination", firstPage);

        var secondPage = await client.GetStringAsync("/following?pageNumber=2");
        Assert.Contains("Topic 21", secondPage);
        Assert.DoesNotContain("Topic 1</a>", secondPage);
    }

    private WebApplicationFactory<Program> WithActivity(IReadOnlyList<MemberPublicActivityItem> items) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMemberPublicActivityRepository>();
            services.AddSingleton<IMemberPublicActivityRepository>(new RecordingMemberPublicActivityRepository(items));
        }));

    private static MemberPublicActivityItem Item(
        string type,
        string title,
        Guid authorId,
        string authorName,
        DateTimeOffset publishedAt,
        int? contentId = null,
        int? parentId = null,
        string? slug = null) =>
        new(
            type,
            title,
            $"{title} summary",
            publishedAt,
            ContentId: contentId,
            ParentId: parentId,
            Slug: slug,
            AuthorId: authorId,
            AuthorDisplayName: authorName);

    private static async Task<(HttpClient Client, MemberAccount Member)> CreateMemberAsync(
        WebApplicationFactory<Program> host,
        string email,
        string displayName,
        Guid? memberId = null)
    {
        var member = await CreateAccountAsync(host, email, displayName, memberId);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, member.Id.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, member.DisplayName);
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.EmailHeader, member.Email);
        return (client, member);
    }

    private static Task<MemberAccount> CreateAccountAsync(
        WebApplicationFactory<Program> host,
        string email,
        string displayName,
        Guid? memberId = null) =>
        host.Services.GetRequiredService<IMemberAccountRepository>().CreateAsync(new MemberAccount
        {
            Id = memberId ?? Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });

    private sealed class RecordingMemberPublicActivityRepository(IReadOnlyList<MemberPublicActivityItem> items)
        : IMemberPublicActivityRepository
    {
        public int FeedPageCalls { get; private set; }

        public int SinglePageCalls { get; private set; }

        public IReadOnlyList<Guid> LastFeedAuthorIds { get; private set; } = [];

        public Task<MemberPublicActivityPage> GetPageAsync(
            Guid memberId,
            int? linkedLegacyUserId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            SinglePageCalls++;
            throw new InvalidOperationException("Following feed must not N+1 GetPageAsync per follow.");
        }

        public Task<MemberPublicActivityPage> GetFeedPageAsync(
            IReadOnlyCollection<Guid> memberIds,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            FeedPageCalls++;
            LastFeedAuthorIds = memberIds.ToList();
            var matching = items
                .Where(item => item.AuthorId is Guid authorId && memberIds.Contains(authorId))
                .OrderByDescending(item => item.PublishedAt)
                .ToList();
            var pageItems = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new MemberPublicActivityPage(pageItems, matching.Count, page, pageSize));
        }
    }
}
