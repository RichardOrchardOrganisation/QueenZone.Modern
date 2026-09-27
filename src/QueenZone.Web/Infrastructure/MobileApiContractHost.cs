using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>
/// Opt-in Testing-only bootstrap for the mobile API consumer-contract suite (#869).
/// Seeds deterministic members, issues Bearer tokens through
/// <see cref="MobileAuthTokenIssuer"/>, and writes a fixture JSON once Kestrel
/// has bound a loopback address. Never registered for E2E or production-like hosts,
/// and never enabled unless <see cref="EnableEnvironmentVariable"/> is set.
/// </summary>
public static class MobileApiContractHost
{
    public const string EnableEnvironmentVariable = "QUEENZONE_MOBILE_CONTRACT_HOST";
    public const string FixturePathEnvironmentVariable = "QUEENZONE_MOBILE_CONTRACT_FIXTURE";

    public static readonly Guid MemberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherMemberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid SuspendedMemberId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public const string MemberEmail = "contract-member@example.test";
    public const string OtherMemberEmail = "contract-other@example.test";
    public const string SuspendedMemberEmail = "contract-suspended@example.test";
    public const string MemberDisplayName = "Contract Member";
    public const string OtherMemberDisplayName = "Contract Other";
    public const string SuspendedMemberDisplayName = "Contract Suspended";

    public const int PublishedNewsId = 1003;
    public const string AttachTopicSubject = "Journey attach topic";
    public const string DiscussionTopicSubject = "QueenZone modernisation begins";
    public const string UnreadSeedBody = "Journey unread seed from Contract Other.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public static bool IsEnabled(IHostEnvironment environment)
    {
        if (!QueenZoneEnvironments.UsesInMemoryData(environment))
        {
            return false;
        }

        var flag = Environment.GetEnvironmentVariable(EnableEnvironmentVariable);
        return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveFixturePath()
    {
        var configured = Environment.GetEnvironmentVariable(FixturePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "queenzone-mobile-api-contract-host.json"));
    }

    public static string ReadBoundAddress(IServer server)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("The contract host has no bound server addresses.");

        var resolved = new List<Uri>();
        var sawPlaceholder = false;
        foreach (var value in addresses)
        {
            var candidate = value.Replace("*", "127.0.0.1", StringComparison.Ordinal)
                .Replace("+", "127.0.0.1", StringComparison.Ordinal)
                .Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal);
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (uri.Port == 0)
            {
                sawPlaceholder = true;
                continue;
            }

            resolved.Add(uri);
        }

        var bound = resolved.FirstOrDefault(uri => uri.Scheme == Uri.UriSchemeHttp)
            ?? resolved.FirstOrDefault();
        if (bound is not null)
        {
            return bound.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        }

        if (sawPlaceholder)
        {
            throw new InvalidOperationException(
                "The contract host is still bound to an ephemeral placeholder (port 0).");
        }

        throw new InvalidOperationException("The contract host did not bind a loopback URL.");
    }

    public static async Task<MobileApiContractSeed> SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var members = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var forumWrite = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();
        var adminNews = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
        var privateMessages = scope.ServiceProvider.GetRequiredService<IPrivateMessageRepository>();

        await EnsureMemberAsync(
            members,
            MemberId,
            MemberEmail,
            MemberDisplayName,
            isSuspended: false,
            cancellationToken);
        await EnsureMemberAsync(
            members,
            OtherMemberId,
            OtherMemberEmail,
            OtherMemberDisplayName,
            isSuspended: false,
            cancellationToken);
        await EnsureMemberAsync(
            members,
            SuspendedMemberId,
            SuspendedMemberEmail,
            SuspendedMemberDisplayName,
            isSuspended: true,
            cancellationToken);

        var created = await forumWrite.CreateThreadAsync(
            new NewForumThread(
                CategoryId: 1,
                AuthorMemberId: MemberId,
                AuthorDisplayName: MemberDisplayName,
                Subject: "Contract poll topic",
                Body: "<p>Seeded for consumer-contract 409 coverage.</p>",
                CreatedAt: DateTimeOffset.UtcNow,
                Poll: new NewForumPoll(
                    "Best Queen album?",
                    false,
                    null,
                    null,
                    ["Night at the Opera", "Sheer Heart Attack"],
                    MemberId)),
            cancellationToken);

        var poll = await polls.GetPollWithResultsAsync(created.TopicId, MemberId, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Contract host poll seed did not materialize.");

        var seededAt = DateTimeOffset.UtcNow;
        var attach = await forumWrite.CreateThreadAsync(
            new NewForumThread(
                CategoryId: 1,
                AuthorMemberId: MemberId,
                AuthorDisplayName: MemberDisplayName,
                Subject: AttachTopicSubject,
                Body: "<p>Unlocked topic for the Maestro attach journey.</p>",
                CreatedAt: seededAt),
            cancellationToken);

        var discussionTopicId = await EnsurePublishedNewsDiscussionAsync(
            adminNews,
            forumWrite,
            seededAt,
            cancellationToken);

        var unread = await privateMessages.SendNewOrExistingAsync(
            OtherMemberId,
            MemberId,
            UnreadSeedBody,
            seededAt,
            cancellationToken);
        if (!unread.Succeeded)
        {
            throw new InvalidOperationException(
                unread.ErrorMessage ?? "Contract host could not seed an unread inbox conversation.");
        }

        return new MobileApiContractSeed(
            MemberToken: issuer.IssueAccessToken(MemberId, MemberEmail, MemberDisplayName),
            OtherMemberToken: issuer.IssueAccessToken(OtherMemberId, OtherMemberEmail, OtherMemberDisplayName),
            SuspendedMemberToken: issuer.IssueAccessToken(
                SuspendedMemberId,
                SuspendedMemberEmail,
                SuspendedMemberDisplayName),
            PollTopicId: created.TopicId,
            PollOptionId: poll.Options[0].OptionId,
            AttachTopicId: attach.TopicId,
            DiscussionTopicId: discussionTopicId);
    }

    public static MobileApiContractFixture BuildFixture(string baseUrl, MobileApiContractSeed seed) =>
        new(
            NormalizeBaseUrl(baseUrl),
            QueenZoneEnvironments.Testing,
            new MobileApiContractMemberFixture(
                MemberId.ToString("D"),
                MemberEmail,
                MemberDisplayName,
                seed.MemberToken),
            new MobileApiContractMemberFixture(
                OtherMemberId.ToString("D"),
                OtherMemberEmail,
                OtherMemberDisplayName,
                seed.OtherMemberToken),
            new MobileApiContractMemberFixture(
                SuspendedMemberId.ToString("D"),
                SuspendedMemberEmail,
                SuspendedMemberDisplayName,
                seed.SuspendedMemberToken),
            seed.PollTopicId,
            seed.PollOptionId.ToString("D"),
            seed.AttachTopicId,
            seed.DiscussionTopicId);

    public static void WriteFixture(string path, MobileApiContractFixture fixture)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(fixture, JsonOptions);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    public static MobileApiContractFixture ReadFixture(string path) =>
        JsonSerializer.Deserialize<MobileApiContractFixture>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Contract fixture at '{path}' was empty.");

    private static async Task<int> EnsurePublishedNewsDiscussionAsync(
        IAdminNewsRepository adminNews,
        IForumWriteRepository forumWrite,
        DateTimeOffset seededAt,
        CancellationToken cancellationToken)
    {
        var article = await adminNews.GetByIdAsync(PublishedNewsId, cancellationToken)
            ?? throw new InvalidOperationException($"Contract host news {PublishedNewsId} was not found.");
        if (article.ForumTopicId is int linkedTopicId)
        {
            return linkedTopicId;
        }

        var discussion = await forumWrite.CreateThreadAsync(
            new NewForumThread(
                CategoryId: 1,
                AuthorMemberId: OtherMemberId,
                AuthorDisplayName: OtherMemberDisplayName,
                Subject: DiscussionTopicSubject,
                Body: "<p>Opening post for the published modernisation article.</p>",
                CreatedAt: seededAt),
            cancellationToken);
        await forumWrite.CreatePostAsync(
            new NewForumPost(
                discussion.TopicId,
                OtherMemberId,
                OtherMemberDisplayName,
                "<p>First reply so the story shows Join the discussion.</p>",
                seededAt.AddMinutes(1)),
            cancellationToken);

        if (await adminNews.TrySetForumTopicIdAsync(PublishedNewsId, discussion.TopicId, cancellationToken))
        {
            return discussion.TopicId;
        }

        var linked = await adminNews.GetByIdAsync(PublishedNewsId, cancellationToken);
        return linked?.ForumTopicId
            ?? throw new InvalidOperationException(
                $"Contract host could not link news {PublishedNewsId} to topic {discussion.TopicId}.");
    }

    private static async Task EnsureMemberAsync(
        IMemberAccountRepository members,
        Guid id,
        string email,
        string displayName,
        bool isSuspended,
        CancellationToken cancellationToken)
    {
        var existing = await members.FindByIdAsync(id, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        await members.CreateAsync(
            new MemberAccount
            {
                Id = id,
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow,
                IsSuspended = isSuspended,
                SuspendedAt = isSuspended ? DateTime.UtcNow : null,
                SuspendedReason = isSuspended ? "Contract host suspended member" : null,
            },
            cancellationToken);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.Contains("+", StringComparison.Ordinal)
            || trimmed.Contains("*", StringComparison.Ordinal)
            || trimmed.Contains("0.0.0.0", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(trimmed.Replace("+", "127.0.0.1").Replace("*", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1"), UriKind.Absolute, out var rewritten))
            {
                return rewritten.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
            }
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                Host = uri.Host switch
                {
                    "0.0.0.0" or "+" or "*" or "[::]" => IPAddress.Loopback.ToString(),
                    _ => uri.Host,
                },
            };
            return builder.Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        }

        return trimmed;
    }
}

public sealed record MobileApiContractSeed(
    string MemberToken,
    string OtherMemberToken,
    string SuspendedMemberToken,
    int PollTopicId,
    Guid PollOptionId,
    int AttachTopicId,
    int DiscussionTopicId);

public sealed record MobileApiContractMemberFixture(
    string Id,
    string Email,
    string DisplayName,
    string AccessToken);

public sealed record MobileApiContractFixture(
    string BaseUrl,
    string Environment,
    MobileApiContractMemberFixture Member,
    MobileApiContractMemberFixture OtherMember,
    MobileApiContractMemberFixture SuspendedMember,
    int PollTopicId,
    string PollOptionId,
    int AttachTopicId,
    int DiscussionTopicId);

/// <summary>
/// Writes the contract fixture after the Testing host is listening. Registered only
/// when <see cref="MobileApiContractHost.IsEnabled"/> is true.
/// </summary>
public sealed class MobileApiContractHostedService(
    IServiceProvider services,
    IServer server,
    IHostApplicationLifetime lifetime,
    ILogger<MobileApiContractHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(OnStarted);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task BootstrapAsync(
        IServiceProvider services,
        IServer server,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = MobileApiContractHost.ReadBoundAddress(server);
        var seed = await MobileApiContractHost.SeedAsync(services, cancellationToken);
        var fixture = MobileApiContractHost.BuildFixture(baseUrl, seed);
        var path = MobileApiContractHost.ResolveFixturePath();
        MobileApiContractHost.WriteFixture(path, fixture);
        logger.LogInformation(
            "Mobile API contract host ready. baseUrl={BaseUrl} fixture={FixturePath}",
            fixture.BaseUrl,
            path);
        await Console.Out.WriteLineAsync($"QUEENZONE_MOBILE_CONTRACT_READY {fixture.BaseUrl}");
        await Console.Out.FlushAsync(cancellationToken);
    }

    private void OnStarted()
    {
        try
        {
            BootstrapAsync(services, server, logger).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Mobile API contract host bootstrap failed.");
            lifetime.StopApplication();
        }
    }
}
