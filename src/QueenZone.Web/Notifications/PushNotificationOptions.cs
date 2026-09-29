namespace QueenZone.Web;

public sealed class PushNotificationOptions
{
    public const string SectionName = "PushNotifications";

    public const string DefaultApnsTopic = "org.queenzone.mobile";

    public const int DefaultFcmBatchSize = 500;

    public const int DefaultApnsMaxConcurrency = 20;

    public ApnsPushOptions Apns { get; set; } = new();

    public FcmPushOptions Fcm { get; set; } = new();
}

public sealed class ApnsPushOptions
{
    public string? TeamId { get; set; }

    public string? KeyId { get; set; }

    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// <c>sandbox</c> or <c>production</c> (exact lowercase). The property initializer
    /// is <c>production</c>. DI registration defaults Development hosts to
    /// <c>sandbox</c> and every other host to <c>production</c> before bind; an
    /// explicit setting wins. Empty or whitespace values fail startup validation.
    /// </summary>
    public string Environment { get; set; } = "production";

    /// <summary>APNs topic / bundle id. Defaults to <c>org.queenzone.mobile</c>.</summary>
    public string Topic { get; set; } = PushNotificationOptions.DefaultApnsTopic;
}

public sealed class FcmPushOptions
{
    public string? ProjectId { get; set; }

    public string? ServiceAccountJson { get; set; }
}
