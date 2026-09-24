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
    public async Task Does_not_sweep_during_startup_delay()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var hosted = CreateHostedService(store, recorder, startupDelay: TimeSpan.FromMinutes(5));

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(80);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(0, recorder.ListCalls);
    }

    [Fact]
    public async Task Sweeps_after_startup_delay()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var hosted = CreateHostedService(
            store,
            recorder,
            startupDelay: TimeSpan.FromMilliseconds(20),
            runInterval: Timeout.InfiniteTimeSpan);

        await hosted.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (recorder.ListCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await hosted.StopAsync(CancellationToken.None);

        Assert.True(recorder.ListCalls > 0);
    }

    [Fact]
    public async Task Disabled_option_skips_sweep_without_stopping_the_loop()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var hosted = CreateHostedService(
            store,
            recorder,
            startupDelay: TimeSpan.FromMilliseconds(20),
            runInterval: TimeSpan.FromMilliseconds(20),
            enabled: false);

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(0, recorder.ListCalls);
    }

    [Fact]
    public async Task Sweep_starts_GalleryOrphanSweep_activity_during_scoped_work()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var listener = QueenZoneActivityTestListener.Listen();
        using var hosted = CreateHostedService(
            store,
            recorder,
            startupDelay: TimeSpan.FromMilliseconds(20),
            runInterval: Timeout.InfiniteTimeSpan);

        await hosted.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (recorder.ListCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(recorder.ListCalls > 0);
        var activity = Assert.Single(listener.Started, item => item.OperationName == "GalleryOrphanSweep");
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.NotNull(recorder.ActivityDuringWork);
        Assert.Equal("GalleryOrphanSweep", recorder.ActivityDuringWork.OperationName);
        Assert.Equal(activity.Id, recorder.ActivityDuringWork.Id);
        await listener.WaitUntilStoppedAsync(activity);
        Assert.True(activity.IsStopped);
        Assert.Contains(listener.Stopped, item => item.Id == activity.Id);

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Disabled_option_does_not_start_an_activity()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var recorder = new RecordingSweepGalleryPhotoBlobService();
        using var listener = QueenZoneActivityTestListener.Listen();
        using var hosted = CreateHostedService(
            store,
            recorder,
            startupDelay: TimeSpan.FromMilliseconds(20),
            runInterval: TimeSpan.FromMilliseconds(20),
            enabled: false);

        await hosted.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await hosted.StopAsync(CancellationToken.None);

        Assert.Equal(0, recorder.ListCalls);
        Assert.DoesNotContain(listener.Started, item => item.OperationName == "GalleryOrphanSweep");
    }

    private static GalleryOrphanSweepHostedService CreateHostedService(
        SharedPhotoStore store,
        RecordingSweepGalleryPhotoBlobService galleryPhotoBlobService,
        TimeSpan startupDelay,
        TimeSpan? runInterval = null,
        bool enabled = true)
    {
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
            TimeProvider.System,
            NullLogger<GalleryOrphanSweepHostedService>.Instance)
        {
            StartupDelay = startupDelay,
            RunInterval = runInterval ?? GalleryOrphanSweepHostedService.DefaultRunInterval,
        };
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
