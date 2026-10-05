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
    public int RefreshTokenReuseGraceSeconds { get; init; } = 300;

    /// <summary>
    /// After the grace window, a replayed token whose direct successor has never
    /// been used is still treated as a lost rotation response, for as long as that
    /// successor is valid: nobody else has advanced the chain, so the legitimate
    /// client most likely never received it. Set to false to revoke all grants on
    /// any replay outside the grace window.
    /// </summary>
    public bool RefreshTokenUnusedSuccessorRecovery { get; init; } = true;

    /// <summary>
    /// Maximum unused-successor recoveries per member in 24 hours before the next
    /// one is treated as theft. Bounds a thief and the real client swapping one
    /// chain back and forth. Counted per process.
    /// </summary>
    public int RefreshTokenUnusedSuccessorRecoveryDailyLimit { get; init; } = 10;

    /// <summary>HMAC-SHA256 key, at least 32 characters. Never commit a production value.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public string ResolveSigningKey(bool productionLike)
    {
        if (OptionsValidation.LooksConfigured(SigningKey))
        {
            return SigningKey.Trim();
        }

        return productionLike ? string.Empty : DevelopmentSigningKey;
    }
}
