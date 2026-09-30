using System.Text;

namespace QueenZone.NewsAgent;

public interface INewsDiscoveryHttpClient
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default);

    Task<NewsDiscoveryHttpResponse> GetAsync(string url, CancellationToken cancellationToken = default);
}

public sealed record NewsDiscoveryHttpResponse(
    string FinalUrl,
    string ContentType,
    string Body);

public sealed class NewsDiscoveryHttpClient(HttpClient httpClient) : INewsDiscoveryHttpClient
{
    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync(url, cancellationToken);
        return response.Body;
    }

    public async Task<NewsDiscoveryHttpResponse> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        OutboundUrlSafety.EnsureAllowedHttpUrl(url);

        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
        OutboundUrlSafety.EnsureAllowedHttpUrl(finalUrl);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        if (!OutboundUrlSafety.IsAllowedTextContentType(contentType))
        {
            throw new InvalidOperationException(
                $"Discovery response from '{finalUrl}' has unsupported content type '{contentType}'.");
        }

        if (response.Content.Headers.ContentLength is long length
            && length > OutboundUrlSafety.DefaultMaxResponseBytes)
        {
            throw new InvalidOperationException(
                $"Discovery response from '{finalUrl}' exceeds the {OutboundUrlSafety.DefaultMaxResponseBytes}-byte limit.");
        }

        // Cap body size to limit memory/DoS from a malicious feed/page.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > OutboundUrlSafety.DefaultMaxResponseBytes)
            {
                throw new InvalidOperationException(
                    $"Discovery response from '{finalUrl}' exceeds the {OutboundUrlSafety.DefaultMaxResponseBytes}-byte limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = ResolveEncoding(charset);
        var body = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return new NewsDiscoveryHttpResponse(finalUrl, contentType, body);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"', '\''));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
