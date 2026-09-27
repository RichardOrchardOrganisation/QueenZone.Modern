using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

internal sealed class NoOpNotificationDispatcher : INotificationDispatcher
{
    public static NoOpNotificationDispatcher Instance { get; } = new();

    public Task NotifyForumReplyAsync(
        int topicId,
        int postId,
        Guid authorMemberId,
        string topicTitle,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyPrivateMessageAsync(
        Guid conversationId,
        Guid recipientMemberId,
        Guid senderMemberId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task NotifyNewsPublishedAsync(
        int articleId,
        string title,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class ThrowingNotificationDispatcher(Exception exception) : INotificationDispatcher
{
    public Task NotifyForumReplyAsync(
        int topicId,
        int postId,
        Guid authorMemberId,
        string topicTitle,
        CancellationToken cancellationToken = default) =>
        throw exception;

    public Task NotifyPrivateMessageAsync(
        Guid conversationId,
        Guid recipientMemberId,
        Guid senderMemberId,
        CancellationToken cancellationToken = default) =>
        throw exception;

    public Task NotifyNewsPublishedAsync(
        int articleId,
        string title,
        CancellationToken cancellationToken = default) =>
        throw exception;
}

internal sealed class RecordingPushTransport : IPushTransport
{
    public List<(IReadOnlyList<PushDeviceToken> Tokens, PushNotificationPayload Payload)> Sends { get; } = [];

    public Exception? ThrowOnSend { get; set; }

    public int TokenSendCount => Sends.Sum(send => send.Tokens.Count);

    public void Reset()
    {
        Sends.Clear();
        ThrowOnSend = null;
    }

    public Task SendAsync(
        IReadOnlyList<PushDeviceToken> tokens,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        Sends.Add(([.. tokens], payload));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTopicWatchLookup : ITopicWatchLookup
{
    public Dictionary<int, IReadOnlyList<Guid>> Watchers { get; } = [];

    public void Reset() => Watchers.Clear();

    public Task<IReadOnlyList<Guid>> ListMemberIdsAsync(
        int topicId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            Watchers.TryGetValue(topicId, out var members)
                ? members
                : []);
}

internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, eventId, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal sealed class AlwaysWatchLookup(Guid memberId) : ITopicWatchLookup
{
    public Task<IReadOnlyList<Guid>> ListMemberIdsAsync(
        int topicId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([memberId]);
}

internal sealed class ConfigurableAlwaysWatchLookup : ITopicWatchLookup
{
    public Guid MemberId { get; set; }

    public void Reset() => MemberId = Guid.Empty;

    public Task<IReadOnlyList<Guid>> ListMemberIdsAsync(
        int topicId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(MemberId == Guid.Empty ? [] : [MemberId]);
}

internal static class DeviceTokenTestData
{
    public static DeviceTokenEntity Token(
        Guid memberId,
        QueenZone.Data.Entities.DevicePushPlatform platform,
        string token,
        string? deviceId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId ?? $"device-{Guid.NewGuid():N}",
            MemberAccountId = memberId,
            Platform = platform,
            Token = token,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    public static PushDeviceToken PushToken(
        Guid memberId,
        PushDevicePlatform platform,
        string token) =>
        new(memberId, platform, token);
}
