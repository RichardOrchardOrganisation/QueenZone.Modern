using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace QueenZone.Web.Tests;

public sealed class StartupOptionsRegistrationTests
{
    [Theory]
    [InlineData("NewsSuggestions:MaxSubmissionsPerMemberPerDay", "0", typeof(NewsSuggestionOptions))]
    [InlineData("FanPerformanceSubmissions:StaleAfterDays", "0", typeof(FanPerformanceSubmissionOptions))]
    [InlineData("HelpRequests:MaxAnonymousPerIpPerHour", "0", typeof(HelpRequestOptions))]
    [InlineData("HelpRequests:MaxPerMemberPerMinute", "0", typeof(HelpRequestOptions))]
    [InlineData("HelpRequests:MaxPerEmailPerDay", "0", typeof(HelpRequestOptions))]
    [InlineData("HelpRequests:MaxPerMemberPerDay", "0", typeof(HelpRequestOptions))]
    [InlineData("HelpRequests:MinimumDwellSeconds", "-1", typeof(HelpRequestOptions))]
    [InlineData("RateLimiting:PrivateMessages:WindowMinutes", "0", typeof(PrivateMessageRateLimitOptions))]
    [InlineData("RateLimiting:PrivateMessages:MaxMessagesPerWindow", "0", typeof(PrivateMessageRateLimitOptions))]
    [InlineData("RateLimiting:PrivateMessages:MaxNewRecipientsPerWindow", "0", typeof(PrivateMessageRateLimitOptions))]
    [InlineData("RateLimiting:PrivateMessages:MaxDuplicateMessagesPerWindow", "0", typeof(PrivateMessageRateLimitOptions))]
    [InlineData("RateLimiting:PrivateMessages:NewAccountAgeDays", "0", typeof(PrivateMessageRateLimitOptions))]
    [InlineData("RateLimiting:PrivateMessages:NewAccountMaxMessagesPerWindow", "0", typeof(PrivateMessageRateLimitOptions))]
    [InlineData(
        "RateLimiting:PrivateMessages:NewAccountMaxNewRecipientsPerWindow",
        "0",
        typeof(PrivateMessageRateLimitOptions))]
    public async Task StartAsync_InvalidNumericOption_ThrowsWithConfigurationPath(
        string key,
        string value,
        Type optionsType)
    {
        using var host = CreateOptionsHost(
            "Development",
            new Dictionary<string, string?> { [key] = value });

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal(optionsType, ex.OptionsType);
        Assert.Contains(key, string.Join(Environment.NewLine, ex.Failures));
    }

    [Fact]
    public async Task StartAsync_DevelopmentWithoutApnsEnvironment_UsesSandbox()
    {
        using var host = CreateOptionsHost("Development", new Dictionary<string, string?>());
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value;
        Assert.Equal("sandbox", options.Apns.Environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData("E2E")]
    public async Task StartAsync_NonDevelopmentWithoutApnsEnvironment_UsesProduction(string environmentName)
    {
        using var host = CreateOptionsHost(environmentName, new Dictionary<string, string?>());
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value;
        Assert.Equal("production", options.Apns.Environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("sandox")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task StartAsync_InvalidApnsEnvironment_ThrowsWithConfigurationPath(string environment)
    {
        using var host = CreateOptionsHost(
            "Development",
            new Dictionary<string, string?>
            {
                ["PushNotifications:Apns:Environment"] = environment,
            });

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal(typeof(PushNotificationOptions), ex.OptionsType);
        Assert.Contains("PushNotifications:Apns:Environment", string.Join(Environment.NewLine, ex.Failures));
    }

    [Fact]
    public async Task StartAsync_ProductionWithoutMobileSigningKey_Succeeds()
    {
        using var host = CreateOptionsHost(
            "Production",
            new Dictionary<string, string?>
            {
                ["MobileAuth:SigningKey"] = string.Empty,
            });

        await host.StartAsync();

        var mobile = host.Services.GetRequiredService<IOptions<MobileAuthOptions>>().Value;
        Assert.True(string.IsNullOrWhiteSpace(mobile.SigningKey));
    }

    [Fact]
    public async Task StartAsync_DevelopmentWithExplicitProductionApns_UsesProduction()
    {
        using var host = CreateOptionsHost(
            "Development",
            new Dictionary<string, string?>
            {
                ["PushNotifications:Apns:Environment"] = "production",
            });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value;
        Assert.Equal("production", options.Apns.Environment);
    }

    [Fact]
    public async Task StartAsync_ProductionWithExplicitSandboxApns_UsesSandbox()
    {
        using var host = CreateOptionsHost(
            "Production",
            new Dictionary<string, string?>
            {
                ["PushNotifications:Apns:Environment"] = "sandbox",
            });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value;
        Assert.Equal("sandbox", options.Apns.Environment);
    }

    [Fact]
    public async Task StartAsync_LaterConfigurationProvider_WinsForApnsEnvironment()
    {
        using var host = CreateOptionsHost(
            "Development",
            new Dictionary<string, string?>
            {
                ["PushNotifications:Apns:Environment"] = "sandbox",
            },
            laterOverrides: new Dictionary<string, string?>
            {
                ["PushNotifications:Apns:Environment"] = "production",
            });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value;
        Assert.Equal("production", options.Apns.Environment);
    }

    private static IHost CreateOptionsHost(
        string environmentName,
        IDictionary<string, string?> overrides,
        IDictionary<string, string?>? laterOverrides = null)
    {
        var settings = new HostApplicationBuilderSettings
        {
            ApplicationName = "QueenZone.Web.Tests",
            EnvironmentName = environmentName,
        };
        var builder = Host.CreateEmptyApplicationBuilder(settings);

        var values = CreateBaseSettings(environmentName);
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(values);
        if (laterOverrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(laterOverrides);
        }

        builder.Services.AddQueenZoneWebOptions(builder.Configuration);
        return builder.Build();
    }

    private static Dictionary<string, string?> CreateBaseSettings(string environmentName)
    {
        if (environmentName is "Production" or "Staging" or "Preview")
        {
            return new Dictionary<string, string?>(ProductionHostSettings.Values)
            {
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
            };
        }

        return new Dictionary<string, string?>
        {
            ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
            ["QueenZoneHostFiltering:AllowedHosts"] = "localhost;127.0.0.1",
        };
    }
}
