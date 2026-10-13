extern alias web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Storage;

namespace ProofHarness;
static class Entry { static async Task Main(string[] args) {
var port = int.Parse(args.Length > 0 ? args[0] : "5199");
var imgDir = "/workspace/qz-2276-proof/img";
var items = new List<PhotoItemSeed>();
for (var i = 501; i <= 520; i++)
    items.Add(new PhotoItemSeed(i, $"Brian May crop-able {i}", $"/Brian_May/img-{i}.jpg", $"/Brian_May/img-{i}-t.jpg", new DateTime(1986, 1, 1).AddDays(i), 1600, 1067, "ProofSeed"));
items.Add(new PhotoItemSeed(530, "SMALL public 384x288 (should be hidden) 530", "/Brian_May/img-530.jpg", "/Brian_May/img-530-t.jpg", new DateTime(1980, 1, 1), 384, 288));
items.Add(new PhotoItemSeed(531, "HIDDEN non-public 0x0 row 531 (should be hidden)", "/Brian_May/img-531.jpg", "/Brian_May/img-531-t.jpg", new DateTime(1980, 1, 2), 0, 0));
var seeds = SamplePhotoData.CreateSeedCategories()
    .Select(c => c.CatId == 9 ? c with { Items = c.Items.Concat(items).ToList() } : c).ToList();
var store = new SharedPhotoStore(seeds);
store.SetVisibility(531, false, "proof@test.local");

var factory = new ProofFactory(store);
factory.UseKestrel(port);
factory.StartServer();
var gallery = factory.Services.GetRequiredService<IGalleryPhotoBlobService>();
foreach (var f in Directory.GetFiles(imgDir, "*.jpg"))
{
    await using var s = File.OpenRead(f);
    await gallery.UploadAsync("brian-may", Path.GetFileName(f), s, "image/jpeg");
}
Console.WriteLine($"READY http://127.0.0.1:{port} gallery={gallery.GetType().Name}");
await Task.Delay(Timeout.Infinite);
}}

sealed class ProofFactory(SharedPhotoStore store) : WebApplicationFactory<web::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Environment.GetEnvironmentVariable("QZ_CONTENT_ROOT") ?? "/workspace/qz-2276/src/QueenZone.Web");
        builder.UseSetting("urls", "");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SharedPhotoStore>();
            services.RemoveAll<IPhotoRepository>();
            services.RemoveAll<IAdminPhotoRepository>();
            services.RemoveAll<IPhotoArchiveDatesRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IPhotoRepository>(_ => new InMemoryPhotoRepository(store));
            services.AddSingleton<IAdminPhotoRepository>(_ => new InMemoryAdminPhotoRepository(store));
            services.AddSingleton<IPhotoArchiveDatesRepository>(_ => new InMemoryPhotoArchiveDatesRepository(store));
        });
    }
}
