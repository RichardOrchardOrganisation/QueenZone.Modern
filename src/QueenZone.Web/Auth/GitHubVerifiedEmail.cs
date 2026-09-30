using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace QueenZone.Web;

/// <summary>
/// Replaces GitHub's profile email with the verified primary address from the emails API.
/// The profile payload's first address is not used, and an unverified primary is not used.
/// </summary>
internal static class GitHubVerifiedEmail
{
    internal static async Task ApplyAsync(
        ClaimsIdentity identity,
        HttpClient httpClient,
        string? accessToken,
        string? emailsEndpoint,
        string? claimsIssuer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(httpClient);

        RemoveEmailClaims(identity);

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(emailsEndpoint))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, emailsEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (payload.RootElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var address in payload.RootElement.EnumerateArray())
        {
            var primary = address.TryGetProperty("primary", out var primaryProperty)
                && primaryProperty.ValueKind == JsonValueKind.True;
            var verified = address.TryGetProperty("verified", out var verifiedProperty)
                && verifiedProperty.ValueKind == JsonValueKind.True;
            if (!primary || !verified || !address.TryGetProperty("email", out var emailProperty))
            {
                continue;
            }

            var email = emailProperty.GetString();
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            var issuer = string.IsNullOrWhiteSpace(claimsIssuer) ? ClaimsIdentity.DefaultIssuer : claimsIssuer;
            identity.AddClaim(new Claim(ClaimTypes.Email, email, ClaimValueTypes.String, issuer));
            identity.AddClaim(new Claim(
                ExternalLoginEmail.EmailVerifiedClaimType,
                "true",
                ClaimValueTypes.String,
                issuer));
            return;
        }
    }

    private static void RemoveEmailClaims(ClaimsIdentity identity)
    {
        foreach (var claim in identity.FindAll(ClaimTypes.Email).ToArray())
        {
            identity.RemoveClaim(claim);
        }

        foreach (var claim in identity.FindAll(ExternalLoginEmail.EmailVerifiedClaimType).ToArray())
        {
            identity.RemoveClaim(claim);
        }
    }
}
