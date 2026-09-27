using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace QueenZone.Web.Tests;

public sealed class AuthRateLimitRejectionTests
{
    [Theory]
    [InlineData("/api/v1/auth/authorize", true)]
    [InlineData("/api/v1/auth/callback", true)]
    [InlineData("/api/v1/auth/token", true)]
    [InlineData("/api/v1/auth/session", false)]
    [InlineData("/api/v1/admin", false)]
    [InlineData("/account/login", false)]
    public void IsOauthAuthPath_matches_rfc6749_auth_routes(string path, bool expected)
    {
        Assert.Equal(expected, AuthRateLimitRejection.IsOauthAuthPath(path));
    }

    [Fact]
    public async Task WriteAsync_logs_path_and_ip_without_query_or_secrets()
    {
        var logger = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILoggerFactory>(new RecordingLoggerFactory(logger));
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/auth/token";
        http.Request.QueryString = new QueryString("?refresh_token=super-secret-refresh");
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
                    QueenZoneRateLimitPolicies.AuthenticatedWrite)),
            "authenticated mutation"));
        http.Response.Body = new MemoryStream();

        await AuthRateLimitRejection.WriteAsync(
            new Microsoft.AspNetCore.RateLimiting.OnRejectedContext
            {
                HttpContext = http,
                Lease = new EmptyRateLimitLease(),
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, http.Response.StatusCode);
        http.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(http.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("temporarily_unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-refresh", body, StringComparison.Ordinal);
        Assert.Contains("/api/v1/auth/token", logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains(QueenZoneRateLimitPolicies.AuthenticatedWrite, logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("member-and-client-ip", logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("203.0.113.9", logger.Messages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-refresh", logger.Messages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("refresh_token", logger.Messages[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(QueenZoneRateLimitPolicies.AnonymousWrite, "client-ip")]
    [InlineData(QueenZoneRateLimitPolicies.AuthenticatedWrite, "member-and-client-ip")]
    [InlineData(QueenZoneRateLimitPolicies.MemberWrite, "member-or-client-ip")]
    [InlineData("custom-policy", "policy-defined")]
    public void PartitionType_describes_policy_without_exposing_partition_key(
        string policyName,
        string expected)
    {
        Assert.Equal(expected, AuthRateLimitRejection.PartitionType(policyName));
    }

    [Fact]
    public async Task WriteAsync_uses_problem_details_for_non_oauth_api_paths()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Request.Method = "GET";
        http.Request.Path = "/api/v1/auth/session";
        http.Response.Body = new MemoryStream();

        await AuthRateLimitRejection.WriteAsync(
            new Microsoft.AspNetCore.RateLimiting.OnRejectedContext
            {
                HttpContext = http,
                Lease = new EmptyRateLimitLease(),
            },
            CancellationToken.None);

        Assert.Equal("application/problem+json", http.Response.ContentType?.Split(';')[0].Trim());
    }

    [Fact]
    public async Task WriteAsync_sets_retry_after_and_leaves_web_response_body_empty()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Request.Method = "POST";
        http.Request.Path = "/contact";
        http.Response.Body = new MemoryStream();

        await AuthRateLimitRejection.WriteAsync(
            new Microsoft.AspNetCore.RateLimiting.OnRejectedContext
            {
                HttpContext = http,
                Lease = new RetryAfterRateLimitLease(TimeSpan.FromSeconds(17.2)),
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, http.Response.StatusCode);
        Assert.Equal("18", http.Response.Headers.RetryAfter);
        Assert.Equal(0, http.Response.Body.Length);
    }

    private sealed class EmptyRateLimitLease : System.Threading.RateLimiting.RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }

    private sealed class RetryAfterRateLimitLease(TimeSpan retryAfter)
        : System.Threading.RateLimiting.RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames =>
            [System.Threading.RateLimiting.MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == System.Threading.RateLimiting.MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter;
                return true;
            }

            metadata = null;
            return false;
        }
    }

    private sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
