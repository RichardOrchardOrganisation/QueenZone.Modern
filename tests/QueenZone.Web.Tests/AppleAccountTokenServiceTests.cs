using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class AppleAccountTokenServiceTests
{
    [Fact]
    public async Task FailedRevocationRetainsTokenForRetry_ThenRemovesLogin()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new MemberAuthenticationOptions
        {
            Apple = new MemberAuthenticationOptions.AppleCredentials
            {
                ClientId = "org.queenzone.test",
                TeamId = "TEAM123456",
                KeyId = "KEY1234567",
                PrivateKey = key.ExportPkcs8PrivateKeyPem(),
            },
        });
        var repository = new InMemoryMemberAccountRepository();
        var account = await repository.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "apple@example.test",
            DisplayName = "Apple Member",
        });
        await repository.AddExternalLoginAsync(account.Id, "Apple", "apple-subject", account.Email);
        var handler = new RevocationHandler();
        var logger = new TestLogger();
        var service = new AppleAccountTokenService(
            repository,
            DataProtectionProvider.Create("QueenZone.Tests"),
            new TestHttpClientFactory(handler),
            options,
            logger);
        await service.SaveProtectedAsync(account.Id, "apple-subject", service.Protect("apple-refresh-token"));
        var now = DateTime.UtcNow;
        await repository.RequestDeletionAsync(account.Id, now, immediate: true);
        await repository.PurgeDeletedAccountsAsync(now.AddDays(-MemberAccountDeletionPolicy.RetentionDays), now);

        handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        await service.RevokePendingAsync();
        Assert.NotEmpty(handler.LastBody);
        Assert.Single(await repository.ListPendingAppleRevocationsAsync(10));
        Assert.False((await repository.GetDeletionProgressAsync(account.Id))!.IsComplete);

        handler.StatusCode = HttpStatusCode.OK;
        await service.RevokePendingAsync();
        Assert.True((await repository.ListPendingAppleRevocationsAsync(10)).Count == 0,
            string.Join("\n", logger.Errors));
        Assert.Contains("token=apple-refresh-token", handler.LastBody);
        Assert.Contains("token_type_hint=refresh_token", handler.LastBody);
        Assert.True((await repository.GetDeletionProgressAsync(account.Id))!.IsComplete);
    }

    [Fact]
    public void ClientSecretTargetsAppleRevocationAudience()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentials = new MemberAuthenticationOptions.AppleCredentials
        {
            ClientId = "org.queenzone.test",
            TeamId = "TEAM123456",
            KeyId = "KEY1234567",
            PrivateKey = key.ExportPkcs8PrivateKeyPem(),
        };

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
            AppleAccountTokenService.CreateClientSecret(credentials));

        Assert.Equal("TEAM123456", jwt.Issuer);
        Assert.Contains("https://appleid.apple.com", jwt.Audiences);
        Assert.Equal("org.queenzone.test", jwt.Subject);
        Assert.Equal("KEY1234567", jwt.Header.Kid);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RevocationHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("https://appleid.apple.com/auth/revoke", request.RequestUri?.ToString());
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode);
        }
    }

    private sealed class TestLogger : ILogger<AppleAccountTokenService>
    {
        public List<string> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Errors.Add(exception.ToString());
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
