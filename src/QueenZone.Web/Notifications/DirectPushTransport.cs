using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

internal sealed partial class DirectPushTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<PushNotificationOptions> options,
    IFcmAccessTokenProvider fcmAccessTokenProvider,
    ILogger<DirectPushTransport> logger) : IPushTransport
{
    public const string ApnsClientName = "ApnsPush";

    public const string FcmClientName = "FcmPush";

    internal const string ProductionApnsHost = "https://api.push.apple.com";

    internal const string SandboxApnsHost = "https://api.sandbox.push.apple.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ApnsJwtFactory apnsJwtFactory = new();

    public async Task<IReadOnlyList<PushDeviceToken>> SendAsync(
        IReadOnlyList<PushDeviceToken> tokens,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (tokens.Count == 0)
        {
            return [];
        }

        var apns = tokens.Where(token => token.Platform == PushDevicePlatform.Apns).ToList();
        var fcm = tokens.Where(token => token.Platform == PushDevicePlatform.Fcm).ToList();
        var unregistered = new List<PushDeviceToken>();

        if (apns.Count > 0)
        {
            unregistered.AddRange(await SendApnsAsync(apns, payload, cancellationToken));
        }

        if (fcm.Count > 0)
        {
            unregistered.AddRange(await SendFcmAsync(fcm, payload, cancellationToken));
        }

        return unregistered;
    }

    private async Task<IReadOnlyList<PushDeviceToken>> SendApnsAsync(
        IReadOnlyList<PushDeviceToken> tokens,
        PushNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        var apns = options.Value.Apns;
        var jwt = apnsJwtFactory.TryCreateToken(apns);
        if (jwt is null)
        {
            Log.ApnsCredentialsNotConfigured(logger, payload.Category);
            return [];
        }

        var host = IsSandbox(apns.Environment) ? SandboxApnsHost : ProductionApnsHost;
        var topic = string.IsNullOrWhiteSpace(apns.Topic)
            ? PushNotificationOptions.DefaultApnsTopic
            : apns.Topic.Trim();
        var client = httpClientFactory.CreateClient(ApnsClientName);
        var body = BuildApnsBody(payload);
        var delivery = new ApnsDeliveryContext(host, topic, jwt, body, payload.Category);
        using var gate = new SemaphoreSlim(PushNotificationOptions.DefaultApnsMaxConcurrency);

        var sends = tokens.Select(async token =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await SendOneApnsAsync(client, delivery, token, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        return (await Task.WhenAll(sends)).OfType<PushDeviceToken>().ToArray();
    }

    private async Task<PushDeviceToken?> SendOneApnsAsync(
        HttpClient client,
        ApnsDeliveryContext delivery,
        PushDeviceToken device,
        CancellationToken cancellationToken)
    {
        var host = delivery.Host;
        var topic = delivery.Topic;
        var jwt = delivery.Jwt;
        var body = delivery.Body;
        var category = delivery.Category;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{device.Token}");
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
        request.Headers.TryAddWithoutValidation("apns-topic", topic);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsUnregistered(response.StatusCode, raw, PushDevicePlatform.Apns))
            {
                return device;
            }

            Log.ApnsSendFailed(logger, device.MemberAccountId, category,
                FormatProviderError(response.StatusCode, raw, device.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.ApnsSendFailedWithException(
                logger,
                ex,
                device.MemberAccountId,
                category,
                ex.Message);
        }

        return null;
    }

    private async Task<IReadOnlyList<PushDeviceToken>> SendFcmAsync(
        IReadOnlyList<PushDeviceToken> tokens,
        PushNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        var projectId = options.Value.Fcm.ProjectId?.Trim();
        if (!OptionsValidation.LooksConfigured(projectId))
        {
            Log.FcmCredentialsNotConfigured(logger, payload.Category);
            return [];
        }

        var accessToken = await fcmAccessTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Log.FcmAccessTokenNotConfigured(logger, payload.Category);
            return [];
        }

        var client = httpClientFactory.CreateClient(FcmClientName);
        var url = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";

        var unregistered = new List<PushDeviceToken>();
        foreach (var batch in tokens.Chunk(PushNotificationOptions.DefaultFcmBatchSize))
        {
            var sends = batch.Select(token =>
                SendOneFcmAsync(client, url, accessToken, token, payload, cancellationToken));
            unregistered.AddRange((await Task.WhenAll(sends)).OfType<PushDeviceToken>());
        }

        return unregistered;
    }

    private async Task<PushDeviceToken?> SendOneFcmAsync(
        HttpClient client,
        string url,
        string accessToken,
        PushDeviceToken device,
        PushNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(BuildFcmBody(device.Token, payload), Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsUnregistered(response.StatusCode, raw, PushDevicePlatform.Fcm))
            {
                return device;
            }

            Log.FcmSendFailed(logger, device.MemberAccountId, payload.Category,
                FormatProviderError(response.StatusCode, raw, device.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.FcmSendFailedWithException(
                logger,
                ex,
                device.MemberAccountId,
                payload.Category,
                ex.Message);
        }

        return null;
    }

    internal static string BuildApnsBody(PushNotificationPayload payload)
    {
        var document = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["aps"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["alert"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["title"] = payload.Title,
                    ["body"] = payload.Body,
                },
                ["sound"] = "default",
            },
        };

        foreach (var pair in payload.Data)
        {
            document[pair.Key] = pair.Value;
        }

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    internal static string BuildFcmBody(string deviceToken, PushNotificationPayload payload)
    {
        var document = new
        {
            message = new
            {
                token = deviceToken,
                notification = new { title = payload.Title, body = payload.Body },
                data = payload.Data,
            },
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    internal static bool IsSandbox(string? environment) =>
        string.Equals(environment?.Trim(), "sandbox", StringComparison.OrdinalIgnoreCase);

    private static string FormatProviderError(HttpStatusCode statusCode, string raw, string deviceToken) =>
        $"{(int)statusCode} {RedactToken(raw, deviceToken)}";

    private static bool IsUnregistered(HttpStatusCode statusCode, string raw, PushDevicePlatform platform)
    {
        if (platform == PushDevicePlatform.Apns && statusCode != HttpStatusCode.Gone
            || platform == PushDevicePlatform.Fcm && statusCode != HttpStatusCode.NotFound)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (platform == PushDevicePlatform.Apns)
            {
                return root.TryGetProperty("reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && reason.GetString() == "Unregistered";
            }

            if (!root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return details.EnumerateArray().Any(detail =>
                detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("errorCode", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() == "UNREGISTERED");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string RedactToken(string? text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
        {
            return string.IsNullOrEmpty(text) ? "unknown" : text;
        }

        return text.Replace(token, "[redacted]", StringComparison.Ordinal);
    }
    private sealed record ApnsDeliveryContext(
        string Host, string Topic, string Jwt, string Body, string Category);
}
