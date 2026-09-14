namespace QueenZone.Web;

public sealed class MobileAuthOptions
{
    public const string SectionName = "MobileAuth";

    /// <summary>
    /// Fallback HMAC key used only in Development/Testing/E2E when <see cref="SigningKey"/>
    /// is blank. Production-like hosts must supply a real key before issuing tokens, but a
    /// missing key must not prevent the public site from starting.
    /// </summary>
    public const string DevelopmentSigningKey = "queenzone-dev-mobile-auth-signing-key!";

    public const string DefaultClientId = "queenzone-mobile";

    public string ClientId { get; init; } = DefaultClientId;

    public string[] RedirectUris { get; init; } = ["queenzone://auth/callback"];

    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    public int AuthorizationCodeLifetimeMinutes { get; init; } = 5;

    public int RefreshTokenLifetimeDays { get; init; } = 30;

    /// <summary>
    /// How long after a refresh token is rotated away a replay of it is still
    /// treated as a client that lost its rotation response (killed mid launch,
    /// dropped connection, ...) rather than a stolen token. Set to 0 to disable
    /// and revoke on first reuse, as before.
    /// </summary>
    public int RefreshTokenReuseGraceSeconds { get; init; } = 30;

    /// <summary>HMAC-SHA256 key, at least 32 characters. Never commit a production value.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public string ResolveSigningKey(bool productionLike) =>
        OptionsValidation.LooksConfigured(SigningKey)
            ? SigningKey.Trim()
            : productionLike
                ? string.Empty
                : DevelopmentSigningKey;
}
