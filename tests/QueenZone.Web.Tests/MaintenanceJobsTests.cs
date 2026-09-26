using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

[Collection(MaintenanceJobActivityCollection.Name)]
public sealed class MaintenanceJobsTests
{
    private static readonly Type[] RetentionHostedServiceTypes =
    [
        typeof(MemberAccountDeletionHostedService),
        typeof(PrivateMessageReportPurgeHostedService),
        typeof(FanPerformanceSubmissionPurgeHostedService),
        typeof(GalleryOrphanSweepHostedService),
    ];

    [Fact]
    public void Schedules_keep_the_former_hosted_service_intervals()
    {
        Assert.Equal(MemberAccountDeletionHostedService.DefaultRunInterval, TimeSpan.FromHours(6));
        Assert.Equal(GalleryOrphanSweepHostedService.DefaultRunInterval, TimeSpan.FromHours(6));
        Assert.Equal(
            [MaintenanceJobs.MemberAccountDeletion, MaintenanceJobs.GalleryOrphanSweep],
            MaintenanceJobs.SixHourly);

        Assert.Equal(PrivateMessageReportPurgeHostedService.DefaultRunInterval, TimeSpan.FromHours(24));
        Assert.Equal(FanPerformanceSubmissionPurgeHostedService.DefaultRunInterval, TimeSpan.FromHours(24));
        Assert.Equal(
            [MaintenanceJobs.PrivateMessageReportPurge, MaintenanceJobs.FanPerformanceSubmissionPurge],
            MaintenanceJobs.Daily);

        Assert.Equal(4, MaintenanceJobs.All.Count);
    }

    [Theory]
    [InlineData("six-hourly", "member-account-deletion,gallery-orphan-sweep")]
    [InlineData("daily", "private-message-report-purge,fan-performance-submission-purge")]
    [InlineData("all", "member-account-deletion,gallery-orphan-sweep,private-message-report-purge,fan-performance-submission-purge")]
    [InlineData("gallery-orphan-sweep", "gallery-orphan-sweep")]
    public void Parse_expands_selectors(string selector, string expected)
    {
        var options = MaintenanceJobCommandOptions.Parse(["run-jobs", selector]);

        Assert.NotNull(options);
        Assert.Equal(expected.Split(','), options.Jobs);
    }

    [Fact]
    public void Parse_combines_selectors_without_duplicates()
    {
        var options = MaintenanceJobCommandOptions.Parse(
            ["run-jobs", "gallery-orphan-sweep", "six-hourly", "private-message-report-purge"]);

        Assert.NotNull(options);
        Assert.Equal(
            [
                MaintenanceJobs.GalleryOrphanSweep,
                MaintenanceJobs.MemberAccountDeletion,
                MaintenanceJobs.PrivateMessageReportPurge,
            ],
            options.Jobs);
    }

    [Theory]
    [InlineData]
    [InlineData("run-jobs")]
    [InlineData("reindex", "all")]
    [InlineData("run-jobs", "weekly")]
    [InlineData("run-jobs", "daily", "Gallery-Orphan-Sweep")]
    public void Parse_rejects_invalid_arguments(params string[] args)
    {
        Assert.Null(MaintenanceJobCommandOptions.Parse(args));
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_job_names()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => MaintenanceJobs.RunAsync(
            "weekly",
            new ServiceCollection().BuildServiceProvider(),
            TimeProvider.System,
            NullLogger.Instance,
            CancellationToken.None));
    }

    [Fact]
    public async Task Worker_runs_every_job_against_sample_data()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildWorkerProvider(logs);

        var exitCode = await provider.GetRequiredService<MaintenanceJobRunner>().RunAsync(MaintenanceJobs.All);

        Assert.Equal(0, exitCode);
        foreach (var job in MaintenanceJobs.All)
        {
            Assert.Contains($"Maintenance job {job} completed", logs.Text);
        }
    }

    [Fact]
    public async Task Worker_keeps_running_later_jobs_when_one_fails()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildWorkerProvider(
            logs,
            services => services.AddScoped<IPrivateMessageModerationRepository>(
                _ => throw new InvalidOperationException("Simulated repository failure.")));

        var exitCode = await provider.GetRequiredService<MaintenanceJobRunner>().RunAsync(MaintenanceJobs.All);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Maintenance job {MaintenanceJobs.PrivateMessageReportPurge} failed", logs.Text);
        Assert.Contains($"Maintenance job {MaintenanceJobs.MemberAccountDeletion} completed", logs.Text);
        Assert.Contains($"Maintenance job {MaintenanceJobs.GalleryOrphanSweep} completed", logs.Text);
        Assert.Contains($"Maintenance job {MaintenanceJobs.FanPerformanceSubmissionPurge} completed", logs.Text);
    }

    [Fact]
    public async Task Worker_stops_when_cancelled()
    {
        await using var provider = BuildWorkerProvider(new CapturingLoggerProvider());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<MaintenanceJobRunner>().RunAsync(MaintenanceJobs.All, cancellation.Token));
    }

    [Fact]
    public void Worker_composition_registers_jobs_without_hosted_services_or_apple_revocation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueenZoneMaintenanceWorker(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(AppleAccountTokenService));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.NotNull(provider.GetRequiredService<MaintenanceJobRunner>());
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MemberAccountService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FanPerformanceSubmissionPurgeService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GalleryOrphanSweepService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPrivateMessageRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPrivateMessageModerationRepository>());
    }

    [Theory]
    [InlineData("QueenZoneLegacy")]
    [InlineData("BlobStorage")]
    public void Worker_composition_requires_sql_and_blob_storage_together(string onlyConnectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{onlyConnectionString}"] = "configured",
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddQueenZoneMaintenanceWorker(configuration));
        Assert.Contains("ConnectionStrings:BlobStorage", error.Message);
    }

    [Fact]
    public void Production_web_composition_does_not_register_retention_hosted_services()
    {
        var services = new ServiceCollection();
        services.AddQueenZoneMaintenanceHostedServices(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Full_web_composition_does_not_register_retention_hosted_services_by_default()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:AllowedEmails:0"] = "admin@test.local",
                ["Site:PublicBaseUrl"] = "https://www.queenzone.org",
            })
            .Build();

        services.AddQueenZoneWebComposition(configuration, new FakeHostEnvironment("Testing"));

        foreach (var hostedServiceType in RetentionHostedServiceTypes)
        {
            Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == hostedServiceType);
        }
    }

    [Fact]
    public void Web_host_runs_retention_hosted_services_when_enabled()
    {
        var services = new ServiceCollection();
        services.AddQueenZoneMaintenanceHostedServices(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MaintenanceJobs.RunInWebHostConfigurationKey] = "true",
                ["Authentication:Apple:ClientId"] = "org.queenzone.signin",
                ["Authentication:Apple:TeamId"] = "TEAM123456",
                ["Authentication:Apple:KeyId"] = "KEY1234567",
                ["Authentication:Apple:PrivateKey"] = "-----BEGIN PRIVATE KEY-----abc-----END PRIVATE KEY-----",
            })
            .Build());

        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        Assert.Equal(RetentionHostedServiceTypes, registered);
    }

    [Fact]
    public void Web_host_keeps_only_apple_revocation_when_apple_is_configured()
    {
        var services = new ServiceCollection();
        services.AddQueenZoneMaintenanceHostedServices(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Apple:ClientId"] = "org.queenzone.signin",
                ["Authentication:Apple:TeamId"] = "TEAM123456",
                ["Authentication:Apple:KeyId"] = "KEY1234567",
                ["Authentication:Apple:PrivateKey"] = "-----BEGIN PRIVATE KEY-----abc-----END PRIVATE KEY-----",
            })
            .Build());

        var hosted = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Equal(typeof(AppleTokenRevocationHostedService), hosted.ImplementationType);
    }

    [Fact]
    public void Development_settings_enable_web_hosted_jobs_and_base_settings_do_not()
    {
        var baseSettings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var developmentSettings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        Assert.False(baseSettings.GetValue<bool>(MaintenanceJobs.RunInWebHostConfigurationKey));
        Assert.True(developmentSettings.GetValue<bool>(MaintenanceJobs.RunInWebHostConfigurationKey));
    }

    [Fact]
    public void Apple_revocation_startup_delay_is_longer_than_app_service_container_start_limit()
    {
        Assert.True(AppleTokenRevocationHostedService.DefaultStartupDelay > TimeSpan.FromSeconds(230));
        Assert.Equal(TimeSpan.FromHours(6), AppleTokenRevocationHostedService.DefaultRunInterval);
    }

    [Fact]
    public async Task Apple_revocation_runs_after_startup_delay()
    {
        using var listener = QueenZoneActivityTestListener.Listen();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddHttpClient();
        services.AddOptions<MemberAuthenticationOptions>();
        services.AddSingleton<IMemberAccountRepository, InMemoryMemberAccountRepository>();
        services.AddScoped<AppleAccountTokenService>();
        await using var provider = services.BuildServiceProvider();
        var clock = new TimerAwareFakeTimeProvider();
        using var hosted = new AppleTokenRevocationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AppleTokenRevocationHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(AppleTokenRevocationHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        var activity = Assert.Single(listener.Started, item => item.OperationName == "AppleTokenRevocation");
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.True(activity.IsStopped);
    }

    [Fact]
    public async Task Apple_revocation_failure_is_logged_without_stopping_the_host()
    {
        using var listener = QueenZoneActivityTestListener.Listen();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var clock = new TimerAwareFakeTimeProvider();
        using var hosted = new AppleTokenRevocationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AppleTokenRevocationHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(AppleTokenRevocationHostedService.DefaultStartupDelay);

        // The run throws (no AppleAccountTokenService registered); a second wait means the loop
        // logged the failure and kept its schedule.
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        var activity = Assert.Single(listener.Started, item => item.OperationName == "AppleTokenRevocation");
        Assert.True(activity.IsStopped);
    }

    [Fact]
    public async Task Apple_revocation_stop_during_startup_delay_does_not_run()
    {
        using var listener = QueenZoneActivityTestListener.Listen();
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var clock = new TimerAwareFakeTimeProvider();
        using var hosted = new AppleTokenRevocationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AppleTokenRevocationHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        await hosted.StopAsync(CancellationToken.None);
        clock.Advance(AppleTokenRevocationHostedService.DefaultStartupDelay);

        Assert.DoesNotContain(listener.Started, item => item.OperationName == "AppleTokenRevocation");
    }

    private static ServiceProvider BuildWorkerProvider(
        CapturingLoggerProvider logs,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddQueenZoneMaintenanceWorker(new ConfigurationBuilder().Build());
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> messages = [];

        public string Text
        {
            get
            {
                lock (messages)
                {
                    return string.Join(Environment.NewLine, messages);
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
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
                lock (owner.messages)
                {
                    owner.messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
