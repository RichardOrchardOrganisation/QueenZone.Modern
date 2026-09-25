using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>Stores Apple refresh tokens encrypted and revokes them after account deletion.</summary>
public sealed class AppleAccountTokenService(
    IMemberAccountRepository accounts,
    IDataProtectionProvider protectionProvider,
    IHttpClientFactory httpClientFactory,
    IOptions<MemberAuthenticationOptions> authenticationOptions,
    ILogger<AppleAccountTokenService> logger)
{
    internal const string HttpClientName = "AppleAccountRevocation";
    private readonly IDataProtector protector = protectionProvider.CreateProtector("QueenZone.AppleRefreshToken.v1");

    public string Protect(string refreshToken) => protector.Protect(refreshToken);

    public Task SaveProtectedAsync(
        Guid memberId,
        string providerKey,
        string protectedToken,
        CancellationToken cancellationToken = default) =>
        accounts.SaveAppleRefreshTokenAsync(memberId, providerKey, protectedToken, cancellationToken);

    public async Task RevokePendingAsync(CancellationToken cancellationToken = default)
    {
        var apple = authenticationOptions.Value.Apple;
        if (apple?.IsConfigured != true)
        {
            return;
        }

        var pending = await accounts.ListPendingAppleRevocationsAsync(100, cancellationToken);
        foreach (var item in pending)
        {
            try
            {
                var refreshToken = protector.Unprotect(item.ProtectedToken);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://appleid.apple.com/auth/revoke")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = apple.ClientId!,
                        ["client_secret"] = CreateClientSecret(apple),
                        ["token"] = refreshToken,
                        ["token_type_hint"] = "refresh_token",
                    }),
                };
                using var response = await httpClientFactory.CreateClient(HttpClientName)
                    .SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                await accounts.CompleteAppleRevocationAsync(item.ExternalLoginId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The protected token stays in the database for the next hosted-service run.
                logger.LogError(ex, "Sign in with Apple revocation failed for external login {ExternalLoginId}.",
                    item.ExternalLoginId);
            }
        }
    }

    internal static string CreateClientSecret(MemberAuthenticationOptions.AppleCredentials apple)
    {
        var now = DateTimeOffset.UtcNow;
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(AppleAuthenticationSupport.NormalizePrivateKey(apple.PrivateKey!));
        var credentials = new SigningCredentials(
            new ECDsaSecurityKey(ecdsa)
            {
                KeyId = apple.KeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
            SecurityAlgorithms.EcdsaSha256);
        var token = new JwtSecurityToken(
            issuer: apple.TeamId,
            audience: "https://appleid.apple.com",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, apple.ClientId!),
                new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            ],
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
