using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Puts the sample legacy forum files into the in-memory blob store so Testing
/// downloads stream bytes instead of 404. Never runs against Azure Blob.
/// </summary>
public sealed class SampleLegacyForumAttachmentSeedHostedService(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    internal const string ScanBytes = "scan-bytes";

    internal const string NotesBytes = "%PDF-notes";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var backend = scope.ServiceProvider.GetRequiredService<IBlobStorageBackend>();
        await SeedAsync(backend, "anoto-setlist-scan.jpg", "image/jpeg", ScanBytes, cancellationToken);
        await SeedAsync(backend, "opera-side-two-notes.pdf", "application/pdf", NotesBytes, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedAsync(
        IBlobStorageBackend backend,
        string fileName,
        string contentType,
        string payload,
        CancellationToken cancellationToken)
    {
        var existing = await backend.OpenReadAsync(
            ForumAttachmentPaths.LegacyContainerName,
            fileName,
            cancellationToken);
        if (existing is not null)
        {
            await existing.DisposeAsync();
            return;
        }

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
        await backend.UploadAsync(
            ForumAttachmentPaths.LegacyContainerName,
            fileName,
            stream,
            contentType,
            cancellationToken);
    }
}
