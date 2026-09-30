using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

[Collection(MaintenanceJobActivityCollection.Name)]
public sealed class GalleryOrphanSweepHostedServiceTests
{
    [Fact]
    public void StartupDelay_is_longer_than_app_service_container_start_limit()
    {
        Assert.True(
            GalleryOrphanSweepHostedService.DefaultStartupDelay > TimeSpan.FromSeconds(230));
        Assert.Equal(TimeSpan.FromMinutes(5), GalleryOrphanSweepHostedService.DefaultStartupDelay);
    }

    [Fact]
    public async Task Does_not_sweep_until_the_full_startup_delay_has_elapsed()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var hosted = CreateHostedService(recorder, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(GalleryOrphanSweepHostedService.DefaultStartupDelay - TimeSpan.FromTicks(1));

        Assert.Equal(0, recorder.ListCalls);

        clock.Advance(TimeSpan.FromTicks(1));
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        Assert.True(recorder.ListCalls > 0);
    }

    [Fact]
    public async Task Sweeps_after_startup_delay_and_again_after_each_run_interval()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var hosted = CreateHostedService(recorder, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(GalleryOrphanSweepHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        var callsAfterFirstRun = recorder.ListCalls;

        clock.Advance(GalleryOrphanSweepHostedService.DefaultRunInterval);
        await clock.WaitForTimersCreatedAsync(3);
        await hosted.StopAsync(CancellationToken.None);

        Assert.True(callsAfterFirstRun > 0);
        Assert.Equal(callsAfterFirstRun * 2, recorder.ListCalls);
    }

    [Fact]
    public async Task Disabled_option_skips_sweep_without_stopping_the_loop()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var hosted = CreateHostedService(recorder, clock, enabled: false);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(GalleryOrphanSweepHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        clock.Advance(GalleryOrphanSweepHostedService.DefaultRunInterval);

        // A third wait means the loop kept its schedule through two skipped runs.
        await clock.WaitForTimersCreatedAsync(3);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(0, recorder.ListCalls);
    }

    [Fact]
    public async Task Sweep_starts_GalleryOrphanSweep_activity_during_scoped_work()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var listener = QueenZoneActivityTestListener.Listen();
        using var hosted = CreateHostedService(recorder, clock);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(GalleryOrphanSweepHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        Assert.True(recorder.ListCalls > 0);
        var activity = Assert.Single(listener.Started, item => item.OperationName == "GalleryOrphanSweep");
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.NotNull(recorder.ActivityDuringWork);
        Assert.Equal("GalleryOrphanSweep", recorder.ActivityDuringWork.OperationName);
        Assert.Equal(activity.Id, recorder.ActivityDuringWork.Id);
        Assert.True(activity.IsStopped);
        Assert.Contains(listener.Stopped, item => item.Id == activity.Id);
    }

    [Fact]
    public async Task Disabled_option_does_not_start_an_activity()
    {
        var clock = new TimerAwareFakeTimeProvider();
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var listener = QueenZoneActivityTestListener.Listen();
        using var hosted = CreateHostedService(recorder, clock, enabled: false);

        await hosted.StartAsync(CancellationToken.None);
        await clock.WaitForTimersCreatedAsync(1);
        clock.Advance(GalleryOrphanSweepHostedService.DefaultStartupDelay);
        await clock.WaitForTimersCreatedAsync(2);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(0, recorder.ListCalls);
        Assert.DoesNotContain(listener.Started, item => item.OperationName == "GalleryOrphanSweep");
    }

    private static GalleryOrphanSweepHostedService CreateHostedService(
        RecordingSweepGalleryPhotoBlobService galleryPhotoBlobService,
        TimeProvider clock,
        bool enabled = true)
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var services = new ServiceCollection();
        services.AddSingleton<IAdminPhotoRepository>(new InMemoryAdminPhotoRepository(store));
        services.AddSingleton<IGalleryPhotoBlobService>(galleryPhotoBlobService);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new GalleryOrphanSweepOptions { Enabled = enabled, DryRun = true }));
        services.AddSingleton<ILogger<GalleryOrphanSweepService>>(NullLogger<GalleryOrphanSweepService>.Instance);
        services.AddTransient<GalleryOrphanSweepService>();
        var provider = services.BuildServiceProvider();

        return new GalleryOrphanSweepHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<GalleryOrphanSweepOptions>>(),
            clock,
            NullLogger<GalleryOrphanSweepHostedService>.Instance);
    }

    private sealed class RecordingSweepGalleryPhotoBlobService : IGalleryPhotoBlobService
    {
        public int ListCalls;

        public Activity? ActivityDuringWork;

        public bool IsConfigured => true;

        public Task UploadAsync(
            string containerName,
            string blobName,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
            string containerName,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ListCalls);
            ActivityDuringWork = Activity.Current;
            return AsyncEnumerable.Empty<GalleryBlobDescriptor>();
        }
    }
}
