using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class DirectPushTransportTests
{
    [Fact]
    public async Task MissingCredentials_IsNoOp_AndDoesNotThrow()
    {
        var handler = new RecordingHttpMessageHandler();
        var transport = CreateTransport(handler, new PushNotificationOptions());
        var memberId = Guid.NewGuid();
        const string secret = "device-token-must-not-appear";

        await transport.SendAsync(
            [DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Apns, secret),
                DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Fcm, secret + "-fcm")],
            PushNotificationPayload.News(1, "Title"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingCredentials_LogsConfiguredSkipEvents()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler();
        var transport = CreateTransport(handler, new PushNotificationOptions(), logger: logger);
        var memberId = Guid.NewGuid();

        await transport.SendAsync(
            [DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Apns, "apns-tok"),
                DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Fcm, "fcm-tok")],
            PushNotificationPayload.News(1, "Title"));

        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1500
                && entry.EventId.Name == "ApnsCredentialsNotConfigured"
                && entry.Message.Contains(
                    "PushNotifications APNs credentials are not configured; skipping APNs sends for category news.",
                    StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1503
                && entry.EventId.Name == "FcmCredentialsNotConfigured"
                && entry.Message.Contains(
                    "PushNotifications FCM credentials are not configured; skipping FCM sends for category news.",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingFcmAccessToken_LogsAndSkipsFcm()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler();
        var transport = CreateTransport(handler, CreateConfiguredOptions(), accessToken: null, logger);

        await transport.SendAsync(
            [DeviceTokenTestData.PushToken(Guid.NewGuid(), PushDevicePlatform.Fcm, "fcm-tok")],
            PushNotificationPayload.News(1, "Title"));

        Assert.Empty(handler.Requests);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1504
                && entry.EventId.Name == "FcmAccessTokenNotConfigured"
                && entry.Message.Contains(
                    "PushNotifications FCM credentials are not configured; skipping FCM sends for category news.",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApnsAndFcm_SendOneRequestPerDevice()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = CreateConfiguredOptions();
        var transport = CreateTransport(handler, options, accessToken: "ya29.test");
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await transport.SendAsync(
            [
                DeviceTokenTestData.PushToken(alice, PushDevicePlatform.Apns, "apns-alice"),
                DeviceTokenTestData.PushToken(alice, PushDevicePlatform.Fcm, "fcm-alice"),
                DeviceTokenTestData.PushToken(bob, PushDevicePlatform.Apns, "apns-bob"),
            ],
            PushNotificationPayload.ForumReply(10, 20, "Topic"));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, handler.Requests.Count(request => request.Uri.Contains("/3/device/", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Requests.Count(request => request.Uri.Contains("messages:send", StringComparison.Ordinal)));
        Assert.Contains(handler.Requests, request => request.Uri.Contains("apns-alice", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Body.Contains("\"category\":\"forumReply\"", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Body.Contains("\"topicId\":\"10\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fcm_ChunksByFiveHundred()
    {
        var handler = new RecordingHttpMessageHandler();
        var transport = CreateTransport(handler, CreateConfiguredOptions(), accessToken: "ya29.test");
        var tokens = Enumerable.Range(0, 501)
            .Select(index => DeviceTokenTestData.PushToken(
                Guid.NewGuid(),
                PushDevicePlatform.Fcm,
                $"fcm-{index}"))
            .ToList();

        await transport.SendAsync(tokens, PushNotificationPayload.News(5, "Title"));

        Assert.Equal(501, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains("messages:send", request.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApnsUnregistered_ReturnsTokenForRemoval()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Gone,
            ResponseBody = """{"reason":"Unregistered"}""",
        };
        var transport = CreateTransport(handler, CreateConfiguredOptions(), logger: logger);
        var token = DeviceTokenTestData.PushToken(Guid.NewGuid(), PushDevicePlatform.Apns, "retired-apns");

        var unregistered = await transport.SendAsync([token], PushNotificationPayload.News(1, "Title"));

        Assert.Equal(token, Assert.Single(unregistered));
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 1501);
    }

    [Fact]
    public async Task FcmUnregistered_ReturnsTokenForRemoval()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.NotFound,
            ResponseBody = """{"error":{"details":[{"errorCode":"UNREGISTERED"}]}}""",
        };
        var transport = CreateTransport(handler, CreateConfiguredOptions(), accessToken: "ya29.test", logger);
        var token = DeviceTokenTestData.PushToken(Guid.NewGuid(), PushDevicePlatform.Fcm, "retired-fcm");

        var unregistered = await transport.SendAsync([token], PushNotificationPayload.News(1, "Title"));

        Assert.Equal(token, Assert.Single(unregistered));
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 1505);
    }

    [Fact]
    public async Task ProviderError_LogsMemberAndCategory_NotToken()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = """{"reason":"BadDeviceToken","token":"apns-secret-xyz"}""",
        };
        var transport = CreateTransport(handler, CreateConfiguredOptions(), accessToken: "ya29.test", logger);
        const string secret = "apns-secret-xyz";
        var memberId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await transport.SendAsync(
            [DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Apns, secret)],
            PushNotificationPayload.PrivateMessage(Guid.NewGuid()));

        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1501
                && entry.EventId.Name == "ApnsSendFailed"
                && entry.Message.Contains(memberId.ToString(), StringComparison.Ordinal)
                && entry.Message.Contains("privateMessage", StringComparison.Ordinal)
                && entry.Message.Contains("APNs send failed", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FcmProviderError_LogsMemberAndCategory_NotToken()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = """{"error":"UNREGISTERED","token":"fcm-secret-xyz"}""",
        };
        var transport = CreateTransport(handler, CreateConfiguredOptions(), accessToken: "ya29.test", logger);
        const string secret = "fcm-secret-xyz";
        var memberId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await transport.SendAsync(
            [DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Fcm, secret)],
            PushNotificationPayload.News(1, "Title"));

        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1505
                && entry.EventId.Name == "FcmSendFailed"
                && entry.Message.Contains(memberId.ToString(), StringComparison.Ordinal)
                && entry.Message.Contains("news", StringComparison.Ordinal)
                && entry.Message.Contains("FCM send failed", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendException_IsCaughtAndLogged_ForApnsAndFcm()
    {
        var logger = new CollectingLogger<DirectPushTransport>();
        var handler = new RecordingHttpMessageHandler
        {
            ThrowException = new HttpRequestException("connection reset"),
        };
        var transport = CreateTransport(handler, CreateConfiguredOptions(), accessToken: "ya29.test", logger);
        var memberId = Guid.NewGuid();

        await transport.SendAsync(
            [
                DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Apns, "apns-throws"),
                DeviceTokenTestData.PushToken(memberId, PushDevicePlatform.Fcm, "fcm-throws"),
            ],
            PushNotificationPayload.News(1, "Title"));

        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1502
                && entry.EventId.Name == "ApnsSendFailedWithException"
                && entry.Message.Contains("APNs send failed", StringComparison.Ordinal)
                && entry.Exception is HttpRequestException);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1506
                && entry.EventId.Name == "FcmSendFailedWithException"
                && entry.Message.Contains("FCM send failed", StringComparison.Ordinal)
                && entry.Exception is HttpRequestException);
    }

    [Fact]
    public void BuildBodies_Match757Contract()
    {
        var payload = PushNotificationPayload.News(88, "Headline");
        var apns = DirectPushTransport.BuildApnsBody(payload);
        var fcm = DirectPushTransport.BuildFcmBody("fcm-token", payload);

        Assert.Contains("\"category\":\"news\"", apns, StringComparison.Ordinal);
        Assert.Contains("\"articleId\":\"88\"", apns, StringComparison.Ordinal);
        Assert.Contains("\"aps\"", apns, StringComparison.Ordinal);
        Assert.Contains("\"token\":\"fcm-token\"", fcm, StringComparison.Ordinal);
        Assert.Contains("\"articleId\":\"88\"", fcm, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactToken_RemovesDeviceToken()
    {
        Assert.Equal("bad [redacted]", DirectPushTransport.RedactToken("bad super-secret", "super-secret"));
        Assert.Equal("unknown", DirectPushTransport.RedactToken(null, "tok"));
    }

    [Fact]
    public void IsSandbox_ReadsEnvironment()
    {
        Assert.True(DirectPushTransport.IsSandbox("sandbox"));
        Assert.True(DirectPushTransport.IsSandbox("Sandbox"));
        Assert.False(DirectPushTransport.IsSandbox("production"));
        Assert.False(DirectPushTransport.IsSandbox(null));
    }

    [Fact]
    public void ApnsJwtFactory_SignsWhenConfigured()
    {
        var factory = new ApnsJwtFactory();
        var jwt = factory.TryCreateToken(CreateConfiguredOptions().Apns);

        Assert.False(string.IsNullOrWhiteSpace(jwt));
        Assert.Equal(3, jwt!.Split('.').Length);
        Assert.Null(factory.TryCreateToken(new ApnsPushOptions()));
    }

    private static DirectPushTransport CreateTransport(
        RecordingHttpMessageHandler handler,
        PushNotificationOptions options,
        string? accessToken = null,
        CollectingLogger<DirectPushTransport>? logger = null) =>
        new(
            new StubHttpClientFactory(handler),
            Options.Create(options),
            new StubFcmAccessTokenProvider(accessToken),
            logger ?? new CollectingLogger<DirectPushTransport>());

    private static PushNotificationOptions CreateConfiguredOptions()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new PushNotificationOptions
        {
            Apns = new ApnsPushOptions
            {
                TeamId = "TEAMID12",
                KeyId = "KEYID123",
                PrivateKeyPem = ecdsa.ExportPkcs8PrivateKeyPem(),
                Environment = "sandbox",
                Topic = "org.queenzone.mobile",
            },
            Fcm = new FcmPushOptions
            {
                ProjectId = "queenzone-mobile",
                ServiceAccountJson = "{}",
            },
        };
    }

    private sealed class StubFcmAccessTokenProvider(string? token) : IFcmAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<(string Uri, string Body)> Requests { get; } = [];

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public string ResponseBody { get; set; } = string.Empty;

        public Exception? ThrowException { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (ThrowException is not null)
            {
                throw ThrowException;
            }

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri?.ToString() ?? string.Empty, body));
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody),
            };
        }
    }
}
